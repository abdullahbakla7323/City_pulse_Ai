using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using CityPulseAI.Services.Gemini;

namespace CityPulseAI.Endpoints;

public static class GeminiEndpoints
{
    public static void MapGeminiEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/chat", async (ChatRequest request, GeminiAgentService agent) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest("Message field cannot be empty.");
            }

            var response = await agent.ProcessUserMessageAsync(request.Message, request.Username, request.ImageUrl);
            return Results.Ok(new ChatResponse { Response = response });
        });
    }
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? Username { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

public class ChatResponse
{
    public string Response { get; set; } = string.Empty;
}
