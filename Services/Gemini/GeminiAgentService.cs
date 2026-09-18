using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;
using CityPulseAI.Infrastructure.Data;
using CityPulseAI.Services.Hubs;
using CityPulseAI.Services.Maps;
using CityPulseAI.Endpoints;

namespace CityPulseAI.Services.Gemini;

public class GeminiAgentService
{
    private readonly AppDbContext _dbContext;
    private readonly SpatialService _spatialService;
    private readonly IHubContext<CityPulseHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiAgentService> _logger;
    private string? _currentUsername;
    private string? _currentImageUrl;

    public GeminiAgentService(
        AppDbContext dbContext,
        SpatialService spatialService,
        IHubContext<CityPulseHub> hubContext,
        IConfiguration configuration,
        ILogger<GeminiAgentService> logger)
    {
        _dbContext = dbContext;
        _spatialService = spatialService;
        _hubContext = hubContext;
        _configuration = configuration;
        _httpClient = new HttpClient();
        _logger = logger;
    }

    public async Task<string> ProcessUserMessageAsync(string userMessage, string? username = null, string? imageUrl = null)
    {
        _currentUsername = username;
        _currentImageUrl = imageUrl;
        var apiKey = _configuration["GEMINI_API_KEY"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                     ?? _configuration["GROQ_API_KEY"] ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "YOUR_GEMINI_API_KEY_HERE" || apiKey == "YOUR_GROQ_API_KEY_HERE")
        {
            return "API Key was not found (.env file should contain GEMINI_API_KEY or GROQ_API_KEY). Please check your configuration.";
        }

        bool isGroq = apiKey.StartsWith("gsk_");
        _logger.LogInformation($"Using {(isGroq ? "Groq" : "Gemini")} API endpoint.");

        try
        {
            var url = isGroq 
                ? "https://api.groq.com/openai/v1/chat/completions"
                : $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            if (isGroq)
            {
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
            }

            var requestBody = isGroq 
                ? BuildGroqRequestPayload(userMessage)
                : BuildGeminiRequestPayload(userMessage);

            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"API Error: {response.StatusCode} - {responseJson}");
                return $"API Error: {response.StatusCode} - {responseJson}";
            }

            using var jsonDoc = JsonDocument.Parse(responseJson);
            var root = jsonDoc.RootElement;

            string replyText = "";
            bool hasFunctionCall = false;
            string functionName = "";
            JsonElement functionArgs = default;
            string toolCallId = "";

            if (isGroq)
            {
                var choice = root.GetProperty("choices")[0];
                var message = choice.GetProperty("message");
                
                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                {
                    var toolCall = toolCalls[0];
                    toolCallId = toolCall.GetProperty("id").GetString() ?? "";
                    var function = toolCall.GetProperty("function");
                    functionName = function.GetProperty("name").GetString() ?? "";
                    
                    var argsString = function.GetProperty("arguments").GetString() ?? "{}";
                    var parsedArgs = JsonDocument.Parse(argsString);
                    functionArgs = parsedArgs.RootElement;
                    hasFunctionCall = true;
                }
                else if (message.TryGetProperty("content", out var contentProp) && contentProp.ValueKind != JsonValueKind.Null)
                {
                    replyText = contentProp.GetString() ?? "";
                }
            }
            else
            {
                var firstCandidate = root.GetProperty("candidates")[0];
                var parts = firstCandidate.GetProperty("content").GetProperty("parts");

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textProp))
                    {
                        replyText += textProp.GetString();
                    }
                    else if (part.TryGetProperty("functionCall", out var funcProp))
                    {
                        hasFunctionCall = true;
                        functionName = funcProp.GetProperty("name").GetString() ?? "";
                        functionArgs = funcProp.GetProperty("args");
                        break;
                    }
                }
            }

            if (hasFunctionCall)
            {
                _logger.LogInformation($"Model requested function execution: {functionName}");
                
                // Execute C# function based on LLM choice
                var executionResult = await ExecuteFunctionAsync(functionName, functionArgs);

                // Send execution result back for a natural language summary
                using var summaryRequest = new HttpRequestMessage(HttpMethod.Post, url);
                if (isGroq)
                {
                    summaryRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                }

                var summaryPayload = isGroq
                    ? BuildGroqSummaryPayload(userMessage, toolCallId, functionName, executionResult)
                    : BuildGeminiSummaryPayload(userMessage, functionName, executionResult);

                summaryRequest.Content = new StringContent(JsonSerializer.Serialize(summaryPayload), Encoding.UTF8, "application/json");

                var summaryResponse = await _httpClient.SendAsync(summaryRequest);
                var summaryResponseJson = await summaryResponse.Content.ReadAsStringAsync();

                if (summaryResponse.IsSuccessStatusCode)
                {
                    using var summaryDoc = JsonDocument.Parse(summaryResponseJson);
                    var summaryRoot = summaryDoc.RootElement;
                    
                    if (isGroq)
                    {
                        replyText = summaryRoot.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "Operation completed successfully.";
                    }
                    else
                    {
                        var summaryParts = summaryRoot.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
                        replyText = summaryParts[0].GetProperty("text").GetString() ?? "Operation completed successfully.";
                    }
                }
                else
                {
                    replyText = $"Operation was executed, but summary could not be generated. Details: {executionResult}";
                }
            }

            return replyText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message with API");
            return $"An error occurred while processing your request: {ex.Message}";
        }
    }

    private async Task<string> ExecuteFunctionAsync(string functionName, JsonElement args)
    {
        try
        {
            switch (functionName)
            {
                case "report_incident":
                    string title = args.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "Incident Report" : "Incident Report";
                    string desc = args.TryGetProperty("description", out var dProp) ? dProp.GetString() ?? "" : "";
                    double lat = GetDoubleProperty(args, "latitude");
                    double lng = GetDoubleProperty(args, "longitude");
                    
                    return await HandleReportIncidentAsync(title, desc, lat, lng);

                case "get_crews_status":
                    return await HandleGetCrewsStatusAsync();

                case "assign_crew_to_incident":
                    int incidentId = GetIntProperty(args, "incidentId");
                    int? crewId = GetNullableIntProperty(args, "crewId");
                    
                    return await HandleAssignCrewAsync(incidentId, crewId);

                case "get_system_summary":
                    return await HandleGetSystemSummaryAsync();

                case "get_active_incidents":
                    return await HandleGetActiveIncidentsAsync();

                default:
                    return $"Unknown function name: {functionName}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to execute function {functionName}");
            return $"Error occurred while executing function: {ex.Message}";
        }
    }

    private async Task<string> HandleReportIncidentAsync(string title, string desc, double lat, double lng)
    {
        var point = _spatialService.CreatePoint(lat, lng);
        var matchedType = IncidentEndpoints.DetectCrewType(title, desc);
        Department? dept = null;
        if (matchedType.HasValue)
        {
            dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Type == matchedType.Value);
        }

        User? user = null;
        if (!string.IsNullOrEmpty(_currentUsername))
        {
            user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == _currentUsername.ToLower());
        }

        var incident = new Incident
        {
            Title = title,
            Description = desc,
            Location = point,
            Status = "New",
            CreatedAt = DateTime.UtcNow,
            DepartmentId = dept?.Id,
            ReportedById = user?.Id,
            ImageUrl = _currentImageUrl
        };

        _dbContext.Incidents.Add(incident);
        await _dbContext.SaveChangesAsync();

        // Broadcast to SignalR client (Leaflet map updates)
        await _hubContext.Clients.All.SendAsync("IncidentReported", new {
            id = incident.Id,
            title = incident.Title,
            description = incident.Description,
            lat = lat,
            lng = lng,
            status = incident.Status,
            createdAt = incident.CreatedAt,
            departmentName = dept?.Name ?? "General Coordination Department",
            imageUrl = incident.ImageUrl
        });

        // Automatically call spatial service to search for nearest crew of matched type
        var nearestCrew = await _spatialService.GetNearestCrewAsync(lat, lng, matchedType);

        var mediaInfo = !string.IsNullOrEmpty(_currentImageUrl) 
            ? " The attached photo/video was also successfully forwarded to the department." 
            : "";

        if (nearestCrew != null)
        {
            if (SystemSettings.AutoPilot && nearestCrew.Status == CrewStatus.Idle)
            {
                var workOrder = new WorkOrder
                {
                    IncidentId = incident.Id,
                    CrewId = nearestCrew.Id,
                    Status = "Assigned",
                    AssignedAt = DateTime.UtcNow
                };
                nearestCrew.Status = CrewStatus.OnWay;
                incident.Status = "InProgress";
                _dbContext.WorkOrders.Add(workOrder);
                await _dbContext.SaveChangesAsync();

                // Broadcast assigning via SignalR
                await _hubContext.Clients.All.SendAsync("CrewAssigned", new {
                    incidentId = incident.Id,
                    incidentTitle = incident.Title,
                    crewId = nearestCrew.Id,
                    crewName = nearestCrew.Name,
                    lat = nearestCrew.Location.Y,
                    lng = nearestCrew.Location.X,
                    incidentLat = incident.Location.Y,
                    incidentLng = incident.Location.X,
                    status = nearestCrew.Status.ToString()
                });

                return JsonSerializer.Serialize(new {
                    success = true,
                    message = $"[Auto-Pilot] New incident created (ID: {incident.Id}) and '{nearestCrew.Name}' ({nearestCrew.Type}) was automatically dispatched.{mediaInfo}",
                    incidentId = incident.Id,
                    title = incident.Title,
                    lat = lat,
                    lng = lng,
                    departmentName = dept?.Name ?? "General Coordination Department",
                    nearestCrewFound = true,
                    nearestCrewName = nearestCrew.Name,
                    nearestCrewType = nearestCrew.Type.ToString(),
                    nearestCrewId = nearestCrew.Id,
                    autoDispatched = true
                });
            }

            return JsonSerializer.Serialize(new {
                success = true,
                message = $"New incident successfully reported (ID: {incident.Id}) and assigned to {dept?.Name ?? "General Coordination Department"}.{mediaInfo}",
                incidentId = incident.Id,
                title = incident.Title,
                lat = lat,
                lng = lng,
                departmentName = dept?.Name ?? "General Coordination Department",
                nearestCrewFound = true,
                nearestCrewName = nearestCrew.Name,
                nearestCrewType = nearestCrew.Type.ToString(),
                nearestCrewId = nearestCrew.Id,
                recommendation = $"Nearest available crew identified as '{nearestCrew.Name}' ({nearestCrew.Type}). Call assign_crew_to_incident to dispatch."
            });
        }

        return JsonSerializer.Serialize(new {
            success = true,
            message = $"New incident successfully reported (ID: {incident.Id}) and assigned to {dept?.Name ?? "General Coordination Department"}.{mediaInfo}",
            incidentId = incident.Id,
            title = incident.Title,
            lat = lat,
            lng = lng,
            departmentName = dept?.Name ?? "General Coordination Department",
            nearestCrewFound = false,
            recommendation = "No idle crew currently available in the area for this incident type."
        });
    }

    private async Task<string> HandleGetCrewsStatusAsync()
    {
        var crewsList = await _dbContext.Crews
            .Include(c => c.Department)
            .ToListAsync();

        try
        {
            var pdfBytes = Services.Reports.ReportPdfService.GenerateCrewsStatusPdf(crewsList);
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temp_reports");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            var filePath = Path.Combine(tempDir, "Crews_Status_Report.pdf");
            await File.WriteAllBytesAsync(filePath, pdfBytes);

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating or launching Crews Status PDF");
        }

        var crewsJson = crewsList.Select(c => new {
            c.Id,
            c.Name,
            Type = c.Type.ToString(),
            Status = c.Status.ToString(),
            Lat = c.Location.Y,
            Lng = c.Location.X
        });

        return JsonSerializer.Serialize(crewsJson);
    }

    private async Task<string> HandleGetActiveIncidentsAsync()
    {
        var incidents = await _dbContext.Incidents
            .Where(i => i.Status != "Resolved")
            .Select(i => new {
                i.Id,
                i.Title,
                i.Description,
                i.Status,
                Lat = i.Location.Y,
                Lng = i.Location.X,
                i.CreatedAt
            })
            .ToListAsync();

        return JsonSerializer.Serialize(incidents);
    }

    private async Task<string> HandleAssignCrewAsync(int incidentId, int? crewId)
    {
        var incident = await _dbContext.Incidents.FindAsync(incidentId);
        if (incident == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "Specified incident not found." });
        }

        if (incident.Status == "Resolved")
        {
            return JsonSerializer.Serialize(new { success = false, error = "This incident is already resolved." });
        }

        Crew? crew = null;
        if (crewId.HasValue)
        {
            crew = await _dbContext.Crews.FindAsync(crewId.Value);
        }
        else
        {
            // Auto-assign closest crew
            var matchedType = IncidentEndpoints.DetectCrewType(incident.Title, incident.Description);
            crew = await _spatialService.GetNearestCrewAsync(incident.Location.Y, incident.Location.X, matchedType);
        }

        if (crew == null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "No available crew found to assign." });
        }

        if (crew.Status != CrewStatus.Idle)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Crew '{crew.Name}' is not idle. Status: {crew.Status}" });
        }

        // Create work order
        var workOrder = new WorkOrder
        {
            IncidentId = incident.Id,
            CrewId = crew.Id,
            Status = "Assigned",
            AssignedAt = DateTime.UtcNow
        };

        crew.Status = CrewStatus.OnWay;
        incident.Status = "InProgress";

        _dbContext.WorkOrders.Add(workOrder);
        await _dbContext.SaveChangesAsync();

        // Broadcast assigning via SignalR
        await _hubContext.Clients.All.SendAsync("CrewAssigned", new {
            incidentId = incident.Id,
            incidentTitle = incident.Title,
            crewId = crew.Id,
            crewName = crew.Name,
            lat = crew.Location.Y,
            lng = crew.Location.X,
            incidentLat = incident.Location.Y,
            incidentLng = incident.Location.X,
            status = crew.Status.ToString()
        });

        return JsonSerializer.Serialize(new {
            success = true,
            message = $"Crew '{crew.Name}' successfully assigned to incident '{incident.Title}'. Crew is en route.",
            workOrderId = workOrder.Id,
            crewName = crew.Name,
            incidentTitle = incident.Title
        });
    }

    private async Task<string> HandleGetSystemSummaryAsync()
    {
        int totalIncidents = await _dbContext.Incidents.CountAsync();
        int activeIncidents = await _dbContext.Incidents.CountAsync(i => i.Status != "Resolved");
        int resolvedIncidents = await _dbContext.Incidents.CountAsync(i => i.Status == "Resolved");
        
        int idleCrews = await _dbContext.Crews.CountAsync(c => c.Status == CrewStatus.Idle);
        int busyCrews = await _dbContext.Crews.CountAsync(c => c.Status == CrewStatus.Busy || c.Status == CrewStatus.OnWay);

        decimal totalBudget = await _dbContext.WorkOrders
            .Where(w => w.Status == "Completed")
            .SumAsync(w => w.BudgetSpent);

        // Fetch top 10 latest active incidents
        var activeIncidentsList = await _dbContext.Incidents
            .Include(i => i.Department)
            .Where(i => i.Status != "Resolved")
            .OrderByDescending(i => i.CreatedAt)
            .Take(10)
            .ToListAsync();

        try
        {
            var pdfBytes = Services.Reports.ReportPdfService.GenerateSystemSummaryPdf(
                totalIncidents,
                activeIncidents,
                resolvedIncidents,
                idleCrews,
                busyCrews,
                totalBudget,
                activeIncidentsList
            );
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temp_reports");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            var filePath = Path.Combine(tempDir, "System_General_Status_Report.pdf");
            await File.WriteAllBytesAsync(filePath, pdfBytes);

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating or launching System Summary PDF");
        }

        // Check for budget limit warnings (spent >= 85% of limit)
        var budgetWarnings = new List<string>();
        var deptsList = await _dbContext.Departments.ToListAsync();
        foreach (var d in deptsList)
        {
            if (d.BudgetSpent >= d.BudgetLimit * 0.85m)
            {
                budgetWarnings.Add($"WARNING: {d.Name} budget has exceeded 85% limit! (Limit: ${d.BudgetLimit:N2}, Spent: ${d.BudgetSpent:N2}). Urgent budget transfer required.");
            }
        }

        return JsonSerializer.Serialize(new {
            totalIncidents,
            activeIncidents,
            resolvedIncidents,
            idleCrews,
            busyCrews,
            totalBudgetSpent = totalBudget,
            budgetWarnings
        });
    }

    private object BuildGeminiRequestPayload(string userMessage)
    {
        return new
        {
            contents = new object[]
            {
                new {
                    role = "user",
                    parts = new object[] {
                        new { text = userMessage }
                    }
                }
            },
            systemInstruction = new {
                parts = new object[] {
                    new {
                        text = "You are the AI Assistant for CityPulse AI Smart City Automation. " +
                               "There are 4 crew types in the system: Sanitation, Zoning, PublicWorks, Parks. " +
                               "There are 4 departments: Sanitation Department (trash/street cleaning/waste/sewers), Urban Planning & Zoning Department (permits/zoning/building plans), Public Works & Infrastructure Department (roads/sidewalks/asphalt/potholes/construction/internet/cables/electrical/utilities), and Parks & Recreation Department (parks/trees/greenery/gardens). " +
                               "When reporting or summarizing an incident, always inform the user which department received the report. " +
                               "If the user attached a photo or video with the report, confirm to the user that the media has been forwarded to the relevant department. " +
                               "When users report new incidents, call the 'report_incident' function. " +
                               "If no coordinates are provided by the user, pick a realistic latitude between 40.98 and 41.02 and longitude between 29.02 and 29.08. " +
                               "To list crews, call 'get_crews_status'. " +
                               "To list active unresolved incidents and their IDs, call 'get_active_incidents'. " +
                               "To assign a crew to an incident, call 'assign_crew_to_incident'. " +
                               "IMPORTANT: 'assign_crew_to_incident' requires a numeric integer 'incidentId'. " +
                               "If you do not know the numeric ID, first call 'get_active_incidents' to find the exact integer ID. " +
                               "When overall system statistics are requested, call 'get_system_summary'. " +
                               "Always keep your responses helpful, professional, concise, and in English."
                    }
                }
            },
            tools = new object[]
            {
                new {
                    functionDeclarations = new object[]
                    {
                        new {
                            name = "report_incident",
                            description = "Creates and records a new urban incident report in the city system.",
                            parameters = new {
                                type = "object",
                                properties = new {
                                    title = new {
                                        type = "string",
                                        description = "Title of the incident (e.g., Sewer Overflow, Road Pothole, Fallen Power Line)."
                                    },
                                    description = new {
                                        type = "string",
                                        description = "Detailed description of the issue."
                                    },
                                    latitude = new {
                                        type = "string",
                                        description = "Latitude coordinate (e.g. \"40.9912\"). Can be number or string."
                                    },
                                    longitude = new {
                                        type = "string",
                                        description = "Longitude coordinate (e.g. \"29.0233\"). Can be number or string."
                                    }
                                },
                                required = new[] { "title", "description", "latitude", "longitude" }
                            }
                        },
                        new {
                            name = "get_active_incidents",
                            description = "Returns the list of all active unresolved incidents with their integer IDs and coordinates.",
                            parameters = new {
                                type = "object",
                                properties = new { }
                            }
                        },
                        new {
                            name = "get_crews_status",
                            description = "Returns the list of all field crews, their types, status, and locations.",
                            parameters = new {
                                type = "object",
                                properties = new { }
                            }
                        },
                        new {
                            name = "assign_crew_to_incident",
                            description = "Dispatches a crew to a specific incident and creates a work order.",
                            parameters = new {
                                type = "object",
                                properties = new {
                                    incidentId = new {
                                        type = "integer",
                                        description = "The NUMERIC integer ID of the incident. E.g. 1, 2. If unknown, call get_active_incidents first."
                                    },
                                    crewId = new {
                                        type = "integer",
                                        description = "The ID of the crew to dispatch. If omitted, the nearest available crew is selected automatically."
                                    }
                                },
                                required = new[] { "incidentId" }
                            }
                        },
                        new {
                            name = "get_system_summary",
                            description = "Returns overall smart city statistics including total incidents, active work orders, available crews, and total spent budget.",
                            parameters = new {
                                type = "object",
                                properties = new { }
                            }
                        }
                    }
                }
            }
        };
    }

    private object BuildGeminiSummaryPayload(string userMessage, string functionName, string executionResult)
    {
        return new
        {
            contents = new object[]
            {
                new {
                    role = "user",
                    parts = new object[] {
                        new { text = userMessage }
                    }
                },
                new {
                    role = "model",
                    parts = new object[] {
                        new {
                            functionCall = new {
                                name = functionName,
                                args = new { } 
                            }
                        }
                    }
                },
                new {
                    role = "user",
                    parts = new object[] {
                        new {
                            functionResponse = new {
                                name = functionName,
                                response = new { result = executionResult }
                            }
                        }
                    }
                }
            },
            systemInstruction = new {
                parts = new object[] {
                    new {
                        text = "You are the AI Assistant for CityPulse AI Smart City Automation. " +
                               "Summarize the function execution result in English in a fluent, professional, and helpful tone. " +
                               "Include relevant budget details, assigned crew names, and actions taken. " +
                               "If the result contains 'budgetWarnings', display them prominently at the top with a bold ⚠️ WARNING badge."
                    }
                }
            }
        };
    }

    private object BuildGroqRequestPayload(string userMessage)
    {
        return new
        {
            model = "llama-3.3-70b-versatile",
            messages = new object[]
            {
                new {
                    role = "system",
                    content = "You are the AI Assistant for CityPulse AI Smart City Automation. " +
                              "There are 4 crew types in the system: Sanitation, Zoning, PublicWorks, Parks. " +
                              "There are 4 departments: Sanitation Department (trash/street cleaning/waste/sewers), Urban Planning & Zoning Department (permits/zoning/building plans), Public Works & Infrastructure Department (roads/sidewalks/asphalt/potholes/construction/internet/cables/electrical/utilities), and Parks & Recreation Department (parks/trees/greenery/gardens). " +
                              "When reporting or summarizing an incident, always inform the user which department received the report. " +
                              "If the user attached a photo or video with the report, confirm to the user that the media has been forwarded to the relevant department. " +
                              "When users report new incidents, call the 'report_incident' function. " +
                              "If no coordinates are provided by the user, pick a realistic latitude between 40.98 and 41.02 and longitude between 29.02 and 29.08. " +
                              "To list crews, call 'get_crews_status'. " +
                              "To list active unresolved incidents and their IDs, call 'get_active_incidents'. " +
                              "To assign a crew to an incident, call 'assign_crew_to_incident'. " +
                              "IMPORTANT: 'assign_crew_to_incident' requires a numeric integer 'incidentId'. " +
                              "If you do not know the numeric ID, first call 'get_active_incidents' to find the exact integer ID. " +
                              "When overall system statistics are requested, call 'get_system_summary'. " +
                              "Always keep your responses helpful, professional, concise, and in English."
                },
                new {
                    role = "user",
                    content = userMessage
                }
            },
            tools = new object[]
            {
                new {
                    type = "function",
                    function = new {
                        name = "report_incident",
                        description = "Creates and records a new urban incident report in the city system.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                title = new {
                                    type = "string",
                                    description = "Title of the incident (e.g., Sewer Overflow, Road Pothole, Fallen Power Line)."
                                },
                                description = new {
                                    type = "string",
                                    description = "Detailed description of the issue."
                                },
                                latitude = new {
                                    type = "string",
                                    description = "Latitude coordinate (e.g. \"40.9912\"). Can be number or string."
                                },
                                longitude = new {
                                    type = "string",
                                    description = "Longitude coordinate (e.g. \"29.0233\"). Can be number or string."
                                }
                            },
                            required = new[] { "title", "description", "latitude", "longitude" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_active_incidents",
                        description = "Returns the list of all active unresolved incidents with their integer IDs and coordinates.",
                        parameters = new {
                            type = "object",
                            properties = new { }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_crews_status",
                        description = "Returns the list of all field crews, their types, status, and locations.",
                        parameters = new {
                            type = "object",
                            properties = new { }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "assign_crew_to_incident",
                        description = "Dispatches a crew to a specific incident and creates a work order.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                incidentId = new {
                                    type = "integer",
                                    description = "The NUMERIC integer ID of the incident. E.g. 1, 2. If unknown, call get_active_incidents first."
                                },
                                crewId = new {
                                    type = "integer",
                                    description = "The ID of the crew to dispatch. If omitted, the nearest available crew is selected automatically."
                                }
                            },
                            required = new[] { "incidentId" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_system_summary",
                        description = "Returns overall smart city statistics including total incidents, active work orders, available crews, and total spent budget.",
                        parameters = new {
                            type = "object",
                            properties = new { }
                        }
                    }
                }
            }
        };
    }

    private object BuildGroqSummaryPayload(string userMessage, string toolCallId, string functionName, string executionResult)
    {
        return new
        {
            model = "llama-3.3-70b-versatile",
            messages = new object[]
            {
                new {
                    role = "system",
                    content = "You are the AI Assistant for CityPulse AI Smart City Automation. " +
                              "Summarize the function execution result in English in a fluent, professional, and helpful tone. " +
                              "Include relevant budget details, assigned crew names, and actions taken. " +
                              "If the result contains 'budgetWarnings', display them prominently at the top with a bold ⚠️ WARNING badge."
                },
                new {
                    role = "user",
                    content = userMessage
                },
                new {
                    role = "assistant",
                    tool_calls = new object[] {
                        new {
                            id = toolCallId,
                            type = "function",
                            function = new {
                                name = functionName,
                                arguments = "{}"
                            }
                        }
                    }
                },
                new {
                    role = "tool",
                    tool_call_id = toolCallId,
                    name = functionName,
                    content = executionResult
                }
            }
        };
    }

    private double GetDoubleProperty(JsonElement element, string propertyName, double defaultValue = 0)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetDouble();
            }
            if (prop.ValueKind == JsonValueKind.String)
            {
                string val = prop.GetString() ?? "";
                if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
                {
                    return result;
                }
            }
        }
        return defaultValue;
    }

    private int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            if (prop.ValueKind == JsonValueKind.String)
            {
                string val = prop.GetString() ?? "";
                if (int.TryParse(val, out int result))
                {
                    return result;
                }
            }
        }
        return defaultValue;
    }

    private int? GetNullableIntProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            if (prop.ValueKind == JsonValueKind.String)
            {
                string val = prop.GetString() ?? "";
                if (int.TryParse(val, out int result))
                {
                    return result;
                }
            }
        }
        return null;
    }
}
