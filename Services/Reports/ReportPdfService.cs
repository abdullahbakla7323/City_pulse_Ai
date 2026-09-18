using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;

namespace CityPulseAI.Services.Reports;

public class ReportPdfService
{
    static ReportPdfService()
    {
        // Set QuestPDF Community License
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] GenerateReportPdf(ArchiveReport report)
    {
        var document = new OperationReportDocument(report);
        return document.GeneratePdf();
    }

    private class OperationReportDocument : IDocument
    {
        private readonly ArchiveReport _report;

        public OperationReportDocument(ArchiveReport report)
        {
            _report = report;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.BorderBottom(1.5f).BorderColor(Colors.Grey.Darken2).PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().AlignCenter().Text("MUNICIPALITY OF CITYPULSE").Bold();
                    col.Item().AlignCenter().Text("METROPOLITAN SMART CITY OPERATIONS").FontSize(12).Bold();
                    col.Item().AlignCenter().Text("Department of IT & Automated City Dispatch").FontSize(9).Italic();
                    
                    col.Item().PaddingTop(10).Row(headerRow =>
                    {
                        headerRow.RelativeItem().Text($"Ref: CP-SCO-2026-{_report.Id:D4}").FontSize(8).FontColor(Colors.Grey.Darken1);
                        headerRow.RelativeItem().AlignRight().Text($"Date: {_report.ResolvedAt.ToLocalTime():yyyy-MM-dd}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.PaddingVertical(20).Column(col =>
            {
                col.Spacing(12);

                // Document Title
                col.Item().AlignCenter().Text("SMART CITY OPERATION REPORT").FontSize(14).Bold().FontColor(Colors.Blue.Darken4);

                // Introduction Paragraph
                col.Item().Text(
                    $"The urban incident and service request detailed below was successfully received via the CityPulse AI " +
                    $"Smart City Management Platform, resolved through automated dispatch and spatial coordination, and archived."
                ).FontSize(9.5f).Italic().LineHeight(1.3f);

                // Section: Incident Information Table
                col.Item().Text("1. INCIDENT & REPAIR DETAILS").FontSize(11).Bold().FontColor(Colors.Blue.Darken3).Underline();

                col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(140);
                        columns.RelativeColumn();
                    });

                    // Row 1
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Incident / Report ID").Bold();
                    table.Cell().Padding(6).Text($"#REP-{_report.Id:D6}");

                    // Row 2
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Incident Record ID").Bold();
                    table.Cell().Padding(6).Text($"#INC-{_report.IncidentId:D6}");

                    // Row 3
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Incident Title").Bold();
                    table.Cell().Padding(6).Text(_report.IncidentTitle).Bold();

                    // Row 4
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Detailed Description").Bold();
                    table.Cell().Padding(6).Text(_report.IncidentDescription);

                    // Row 5
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Assigned Department").Bold();
                    table.Cell().Padding(6).Text(_report.Department?.Name ?? "General Coordination Department");

                    // Row 6
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Reported By").Bold();
                    table.Cell().Padding(6).Text(string.IsNullOrEmpty(_report.ReportedBy) ? "Anonymous Citizen" : _report.ReportedBy);

                    // Row 7
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Responding Crew").Bold();
                    table.Cell().Padding(6).Text(_report.ResolvedByCrew);

                    // Row 8
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Municipal Budget Spent").Bold();
                    table.Cell().Padding(6).Text($"${_report.BudgetSpent:N2}").FontColor(Colors.Green.Darken3).Bold();

                    // Row 9
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Report Timestamp").Bold();
                    table.Cell().Padding(6).Text(_report.ReportedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));

                    // Row 10
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Resolution Timestamp").Bold();
                    table.Cell().Padding(6).Text(_report.ResolvedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                });

                // Section: Image (if exists)
                if (!string.IsNullOrEmpty(_report.ImageUrl))
                {
                    var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var relativePath = _report.ImageUrl.TrimStart('/');
                    var imagePath = Path.Combine(webRootPath, relativePath);

                    if (File.Exists(imagePath))
                    {
                        col.Item().PaddingTop(10).Text("2. APPENDIX: PHOTOGRAPHIC / VISUAL EVIDENCE").FontSize(11).Bold().FontColor(Colors.Blue.Darken3).Underline();
                        col.Item().Padding(4).Border(0.5f).BorderColor(Colors.Grey.Lighten1).AlignCenter().MaxHeight(180).Image(imagePath);
                    }
                }

                // Official Signatures
                col.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().Column(signCol =>
                    {
                        signCol.Item().AlignCenter().Text("Processing System").Bold();
                        signCol.Item().AlignCenter().Text("CityPulse AI Operations Center");
                        signCol.Item().AlignCenter().Text("Digital Assistant").Italic();
                        
                        // Signature stamp
                        signCol.Item().PaddingTop(15).AlignCenter().Text("[DIGITALLY SIGNED]").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    row.RelativeItem().Column(signCol =>
                    {
                        signCol.Item().AlignCenter().Text("Operation Approval Authority").Bold();
                        signCol.Item().AlignCenter().Text("Smart City Coordination Office");
                        signCol.Item().AlignCenter().Text("Duty Operations Director").Italic();
                        
                        // Signature stamp
                        signCol.Item().PaddingTop(15).AlignCenter().Text("[DIGITALLY SIGNED]").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text("This document has been digitally generated and validated by CityPulse AI Operations.").FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                row.ConstantItem(60).AlignRight().Text(x =>
                {
                    x.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }
    }

    public static byte[] GenerateCrewsStatusPdf(List<Crew> crews)
    {
        var document = new CrewsStatusReportDocument(crews);
        return document.GeneratePdf();
    }

    public static byte[] GenerateSystemSummaryPdf(
        int totalIncidents,
        int activeIncidents,
        int resolvedIncidents,
        int idleCrews,
        int busyCrews,
        decimal totalBudgetSpent,
        List<Incident> activeIncidentsList)
    {
        var document = new SystemSummaryReportDocument(totalIncidents, activeIncidents, resolvedIncidents, idleCrews, busyCrews, totalBudgetSpent, activeIncidentsList);
        return document.GeneratePdf();
    }

    private class CrewsStatusReportDocument : IDocument
    {
        private readonly List<Crew> _crews;

        public CrewsStatusReportDocument(List<Crew> crews)
        {
            _crews = crews;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.BorderBottom(1.5f).BorderColor(Colors.Grey.Darken2).PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().AlignCenter().Text("MUNICIPALITY OF CITYPULSE").Bold();
                    col.Item().AlignCenter().Text("METROPOLITAN SMART CITY OPERATIONS").FontSize(12).Bold();
                    col.Item().AlignCenter().Text("Department of IT & Automated City Dispatch").FontSize(9).Italic();
                    
                    col.Item().PaddingTop(10).Row(headerRow =>
                    {
                        headerRow.RelativeItem().Text($"Report Type: Crews Status Report").FontSize(8).FontColor(Colors.Grey.Darken1);
                        headerRow.RelativeItem().AlignRight().Text($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.PaddingVertical(20).Column(col =>
            {
                col.Spacing(12);

                // Document Title
                col.Item().AlignCenter().Text("SMART CITY CREWS STATUS REPORT").FontSize(14).Bold().FontColor(Colors.Blue.Darken4);

                // Introduction Paragraph
                col.Item().Text(
                    "The current operational statuses, specialized departments, and locations of field crews " +
                    "serving under the CityPulse AI platform are detailed below."
                ).FontSize(9.5f).Italic().LineHeight(1.3f);

                // Section: Statistics Summary
                col.Item().Text("1. SUMMARY STATISTICS").FontSize(11).Bold().FontColor(Colors.Blue.Darken3).Underline();

                int totalCrews = _crews.Count;
                int idleCrews = _crews.Count(c => c.Status == CrewStatus.Idle);
                int busyCrews = _crews.Count(c => c.Status == CrewStatus.Busy);
                int onWayCrews = _crews.Count(c => c.Status == CrewStatus.OnWay);

                col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignCenter().Text("Total Crews").Bold();
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignCenter().Text("Idle Crews").Bold();
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignCenter().Text("Busy (Working) Crews").Bold();
                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).AlignCenter().Text("En Route Crews").Bold();

                    table.Cell().Padding(6).AlignCenter().Text($"{totalCrews}");
                    table.Cell().Padding(6).AlignCenter().Text($"{idleCrews}").FontColor(Colors.Green.Darken3).Bold();
                    table.Cell().Padding(6).AlignCenter().Text($"{busyCrews}").FontColor(Colors.Red.Darken3).Bold();
                    table.Cell().Padding(6).AlignCenter().Text($"{onWayCrews}").FontColor(Colors.Orange.Darken3).Bold();
                });

                // Section: Crews Table
                col.Item().Text("2. CREW STATUS DETAILS").FontSize(11).Bold().FontColor(Colors.Blue.Darken3).Underline();

                col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(40); // ID
                        columns.RelativeColumn(2f);  // Crew Name
                        columns.RelativeColumn(2.5f); // Department / Type
                        columns.RelativeColumn(1.5f); // Status
                        columns.RelativeColumn(2f);  // Location (Lat/Lng)
                    });

                    // Headers
                    table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("ID").Bold().FontColor(Colors.White);
                    table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Crew Name").Bold().FontColor(Colors.White);
                    table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Department / Type").Bold().FontColor(Colors.White);
                    table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Status").Bold().FontColor(Colors.White);
                    table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Location").Bold().FontColor(Colors.White);

                    foreach (var crew in _crews)
                    {
                        table.Cell().Padding(5).Text($"#{crew.Id:D3}");
                        table.Cell().Padding(5).Text(crew.Name).Bold();
                        table.Cell().Padding(5).Text($"{crew.Department?.Name ?? crew.Type.ToString()}");

                        var statusText = crew.Status switch
                        {
                            CrewStatus.Idle => "Idle",
                            CrewStatus.OnWay => "En Route",
                            CrewStatus.Busy => "Busy",
                            _ => crew.Status.ToString()
                        };
                        var statusColor = crew.Status switch
                        {
                            CrewStatus.Idle => Colors.Green.Darken3,
                            CrewStatus.OnWay => Colors.Orange.Darken3,
                            CrewStatus.Busy => Colors.Red.Darken3,
                            _ => Colors.Grey.Darken3
                        };

                        table.Cell().Padding(5).Text(statusText).Bold().FontColor(statusColor);
                        table.Cell().Padding(5).Text($"{crew.Location?.Y:N5}, {crew.Location?.X:N5}");
                    }
                });

                // Official Signatures
                col.Item().PaddingTop(25).Row(row =>
                {
                    row.RelativeItem().Column(signCol =>
                    {
                        signCol.Item().AlignCenter().Text("System Operator").Bold();
                        signCol.Item().AlignCenter().Text("CityPulse AI Operations Center");
                        signCol.Item().AlignCenter().Text("Digital Assistant").Italic();
                        signCol.Item().PaddingTop(15).AlignCenter().Text("[DIGITALLY SIGNED]").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    row.RelativeItem().Column(signCol =>
                    {
                        signCol.Item().AlignCenter().Text("Approval Authority").Bold();
                        signCol.Item().AlignCenter().Text("Smart City Coordination Office");
                        signCol.Item().AlignCenter().Text("Duty Operations Director").Italic();
                        signCol.Item().PaddingTop(15).AlignCenter().Text("[DIGITALLY SIGNED]").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text("This document has been digitally generated and validated by CityPulse AI Operations.").FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                row.ConstantItem(60).AlignRight().Text(x =>
                {
                    x.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }
    }

    private class SystemSummaryReportDocument : IDocument
    {
        private readonly int _totalIncidents;
        private readonly int _activeIncidents;
        private readonly int _resolvedIncidents;
        private readonly int _idleCrews;
        private readonly int _busyCrews;
        private readonly decimal _totalBudgetSpent;
        private readonly List<Incident> _activeIncidentsList;

        public SystemSummaryReportDocument(
            int totalIncidents,
            int activeIncidents,
            int resolvedIncidents,
            int idleCrews,
            int busyCrews,
            decimal totalBudgetSpent,
            List<Incident> activeIncidentsList)
        {
            _totalIncidents = totalIncidents;
            _activeIncidents = activeIncidents;
            _resolvedIncidents = resolvedIncidents;
            _idleCrews = idleCrews;
            _busyCrews = busyCrews;
            _totalBudgetSpent = totalBudgetSpent;
            _activeIncidentsList = activeIncidentsList;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.BorderBottom(1.5f).BorderColor(Colors.Grey.Darken2).PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().AlignCenter().Text("MUNICIPALITY OF CITYPULSE").Bold();
                    col.Item().AlignCenter().Text("METROPOLITAN SMART CITY OPERATIONS").FontSize(12).Bold();
                    col.Item().AlignCenter().Text("Department of IT & Automated City Dispatch").FontSize(9).Italic();
                    
                    col.Item().PaddingTop(10).Row(headerRow =>
                    {
                        headerRow.RelativeItem().Text($"Report Type: System Summary Report").FontSize(8).FontColor(Colors.Grey.Darken1);
                        headerRow.RelativeItem().AlignRight().Text($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.PaddingVertical(20).Column(col =>
            {
                col.Spacing(12);

                // Document Title
                col.Item().AlignCenter().Text("SMART CITY SYSTEM GENERAL STATUS REPORT").FontSize(14).Bold().FontColor(Colors.Blue.Darken4);

                // Introduction Paragraph
                col.Item().Text(
                    "The live summary of urban incidents, active field operations, and budget expenditures managed " +
                    "by the CityPulse AI Smart City Platform is detailed below."
                ).FontSize(9.5f).Italic().LineHeight(1.3f);

                // Section: KPI Indicators Table
                col.Item().Text("1. KEY SYSTEM INDICATORS (KPIs)").FontSize(11).Bold().FontColor(Colors.Blue.Darken3).Underline();

                col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Total Registered Incidents").Bold();
                    table.Cell().Padding(6).Text($"{_totalIncidents}");

                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Active (Unresolved) Incidents").Bold();
                    table.Cell().Padding(6).Text($"{_activeIncidents}").FontColor(Colors.Red.Darken3).Bold();

                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Resolved Incidents").Bold();
                    table.Cell().Padding(6).Text($"{_resolvedIncidents}").FontColor(Colors.Green.Darken3).Bold();

                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Available (Idle) Crews").Bold();
                    table.Cell().Padding(6).Text($"{_idleCrews}").FontColor(Colors.Green.Darken3).Bold();

                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Active Crews (Busy / En Route)").Bold();
                    table.Cell().Padding(6).Text($"{_busyCrews}");

                    table.Cell().Background(Colors.Grey.Lighten4).Padding(6).Text("Total Budget Spent").Bold();
                    table.Cell().Padding(6).Text($"${_totalBudgetSpent:N2}").FontColor(Colors.Blue.Darken3).Bold();
                });

                // Section: Active Incidents Table
                col.Item().Text($"2. ACTIVE INCIDENTS LIST (Latest {_activeIncidentsList.Count} Records)").FontSize(11).Bold().FontColor(Colors.Blue.Darken3).Underline();

                if (_activeIncidentsList.Count == 0)
                {
                    col.Item().Text("There are currently no unresolved active incidents in the system.").Italic();
                }
                else
                {
                    col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(40); // ID
                            columns.RelativeColumn(2.5f); // Title
                            columns.RelativeColumn(2.5f); // Description
                            columns.RelativeColumn(1.5f); // Department
                            columns.RelativeColumn(1.2f); // Status
                            columns.RelativeColumn(2);  // Created At
                        });

                        // Headers
                        table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("ID").Bold().FontColor(Colors.White);
                        table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Incident Title").Bold().FontColor(Colors.White);
                        table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Detailed Description").Bold().FontColor(Colors.White);
                        table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Department").Bold().FontColor(Colors.White);
                        table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Status").Bold().FontColor(Colors.White);
                        table.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Report Date").Bold().FontColor(Colors.White);

                        foreach (var inc in _activeIncidentsList)
                        {
                            table.Cell().Padding(5).Text($"#{inc.Id:D4}");
                            table.Cell().Padding(5).Text(inc.Title).Bold();
                            table.Cell().Padding(5).Text(inc.Description.Length > 40 ? inc.Description.Substring(0, 37) + "..." : inc.Description);
                            table.Cell().Padding(5).Text($"{inc.Department?.Name ?? "General"}");

                            var statusText = inc.Status switch
                            {
                                "New" => "New",
                                "InProgress" => "In Progress",
                                "Resolved" => "Resolved",
                                _ => inc.Status
                            };

                            var statusColor = inc.Status switch
                            {
                                "New" => Colors.Red.Darken3,
                                "InProgress" => Colors.Orange.Darken3,
                                "Resolved" => Colors.Green.Darken3,
                                _ => Colors.Grey.Darken3
                            };

                            table.Cell().Padding(5).Text(statusText).Bold().FontColor(statusColor);
                            table.Cell().Padding(5).Text(inc.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                        }
                    });
                }

                // Official Signatures
                col.Item().PaddingTop(25).Row(row =>
                {
                    row.RelativeItem().Column(signCol =>
                    {
                        signCol.Item().AlignCenter().Text("System Operator").Bold();
                        signCol.Item().AlignCenter().Text("CityPulse AI Operations Center");
                        signCol.Item().AlignCenter().Text("Digital Assistant").Italic();
                        signCol.Item().PaddingTop(15).AlignCenter().Text("[DIGITALLY SIGNED]").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    row.RelativeItem().Column(signCol =>
                    {
                        signCol.Item().AlignCenter().Text("Approval Authority").Bold();
                        signCol.Item().AlignCenter().Text("Smart City Coordination Office");
                        signCol.Item().AlignCenter().Text("Duty Operations Director").Italic();
                        signCol.Item().PaddingTop(15).AlignCenter().Text("[DIGITALLY SIGNED]").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text("This document has been digitally generated and validated by CityPulse AI Operations.").FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                row.ConstantItem(60).AlignRight().Text(x =>
                {
                    x.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }
    }
}
