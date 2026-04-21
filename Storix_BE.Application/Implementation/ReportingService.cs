using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ScottPlot;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class ReportingService : IReportingService
    {
        private readonly IReportingRepository _repo;
        private readonly Cloudinary _cloudinary;

        public ReportingService(IReportingRepository repo, Cloudinary cloudinary)
        {
            _repo = repo;
            _cloudinary = cloudinary;
        }

        public async Task<ReportDetailDto> CreateReportAsync(int companyId, int createdByUserId, CreateReportRequest payload)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (createdByUserId <= 0) throw new ArgumentException("Invalid createdByUserId.", nameof(createdByUserId));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrWhiteSpace(payload.ReportType)) throw new ArgumentException("ReportType is required.", nameof(payload.ReportType));
            if (payload.TimeTo < payload.TimeFrom) throw new ArgumentException("TimeTo must be >= TimeFrom.");

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            var paramsOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

            var report = new Report
            {
                CompanyId = companyId,
                CreatedByUserId = createdByUserId,
                ReportType = payload.ReportType,
                WarehouseId = payload.WarehouseId,
                TimeFrom = payload.TimeFrom,
                TimeTo = payload.TimeTo,
                Status = ReportStatus.Running,
                CreatedAt = now,
                ParametersJson = JsonSerializer.Serialize(new
                {
                    reportType = payload.ReportType,
                    warehouseId = payload.WarehouseId,
                    timeFrom = payload.TimeFrom,
                    timeTo = payload.TimeTo
                }, paramsOptions)
            };

            report = await _repo.CreateReportAsync(report).ConfigureAwait(false);

            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };

                if (string.Equals(payload.ReportType, ReportTypes.OutboundKpiBasic, StringComparison.Ordinal))
                {
                    var kpi = await _repo.GetOutboundKpiBasicAsync(companyId, payload.WarehouseId, payload.TimeFrom, payload.TimeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalCompleted = kpi.TotalCompleted,
                        overallAvgLeadTimeHours = kpi.OverallAvgLeadTimeHours
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(kpi, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.OutboundKpiBasic;
                }
                else if (string.Equals(payload.ReportType, ReportTypes.InventoryTracking, StringComparison.Ordinal))
                {
                    var inv = await _repo.GetInventoryTrackingAsync(companyId, payload.WarehouseId, payload.TimeFrom, payload.TimeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalInboundTransactions = inv.TotalInboundTransactions,
                        totalOutboundTransactions = inv.TotalOutboundTransactions,
                        totalInboundQty = inv.TotalInboundQty,
                        totalOutboundQty = inv.TotalOutboundQty
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(inv, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.InventoryTracking;
                }
                else if (string.Equals(payload.ReportType, ReportTypes.InboundKpiBasic, StringComparison.Ordinal))
                {
                    var inbound = await _repo.GetInboundKpiBasicAsync(companyId, payload.WarehouseId, payload.TimeFrom, payload.TimeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalCompleted = inbound.TotalCompleted,
                        totalReceivedQty = inbound.TotalReceivedQty
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(inbound, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.InboundKpiBasic;
                }
                else if (string.Equals(payload.ReportType, ReportTypes.InventorySnapshot, StringComparison.Ordinal))
                {
                    var snapshot = await _repo.GetInventorySnapshotAsync(companyId, payload.WarehouseId, payload.TimeFrom, payload.TimeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalSkus = snapshot.TotalSkus,
                        totalQuantity = snapshot.TotalQuantity,
                        totalValue = snapshot.TotalValue
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(snapshot, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.InventorySnapshot;
                }
                else if (string.Equals(payload.ReportType, ReportTypes.InventoryLedger, StringComparison.Ordinal))
                {
                    var ledger = await _repo.GetInventoryLedgerAsync(companyId, payload.WarehouseId, payload.ProductId, payload.TimeFrom, payload.TimeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        openingQuantity = ledger.OpeningQuantity,
                        closingQuantity = ledger.ClosingQuantity,
                        entries = ledger.Rows.Count
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(ledger, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.InventoryLedger;
                }
                else if (string.Equals(payload.ReportType, ReportTypes.InventoryInOutBalance, StringComparison.Ordinal))
                {
                    var inOut = await _repo.GetInventoryInOutBalanceAsync(companyId, payload.WarehouseId, payload.TimeFrom, payload.TimeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalOpeningQty = inOut.TotalOpeningQty,
                        totalInboundQty = inOut.TotalInboundQty,
                        totalOutboundQty = inOut.TotalOutboundQty,
                        totalClosingQty = inOut.TotalClosingQty,
                        totalClosingValue = inOut.TotalClosingValue
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(inOut, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.InventoryInOutBalance;
                }
                else if (string.Equals(payload.ReportType, ReportTypes.StocktakeVariance, StringComparison.Ordinal))
                {
                    var stocktake = await _repo.GetStocktakeVarianceAsync(companyId, payload.WarehouseId, payload.InventoryCountTicketId, payload.TimeFrom, payload.TimeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalItems = stocktake.TotalItems,
                        totalVarianceQty = stocktake.TotalVarianceQty,
                        totalVarianceValue = stocktake.TotalVarianceValue
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(stocktake, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.StocktakeVariance;
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported report type '{payload.ReportType}'.");
                }

                report.Status = ReportStatus.Succeeded;
                report.CompletedAt = now;
                report.ErrorMessage = null;
                await _repo.UpdateReportAsync(report).ConfigureAwait(false);

                return new ReportDetailDto(
                    report.Id,
                    report.ReportType,
                    report.CompanyId,
                    report.WarehouseId,
                    report.Status,
                    report.TimeFrom,
                    report.TimeTo,
                    report.CreatedAt,
                    report.CompletedAt,
                    report.ErrorMessage,
                    new ReportResultDto(TryParseJson(report.SummaryJson), TryParseJson(report.DataJson), report.SchemaVersion),
                    report.PdfUrl == null ? null : new ReportPdfArtifactDto(report.PdfUrl, report.PdfFileName, report.PdfContentHash, report.PdfGeneratedAt));
            }
            catch (Exception ex)
            {
                report.Status = ReportStatus.Failed;
                report.CompletedAt = now;
                report.ErrorMessage = ex.Message;
                await _repo.UpdateReportAsync(report).ConfigureAwait(false);
                throw;
            }
        }

        public async Task<ReportDetailDto?> GetReportAsync(int companyId, int reportId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (reportId <= 0) throw new ArgumentException("Invalid reportId.", nameof(reportId));

            var report = await _repo.GetReportByIdAsync(companyId, reportId).ConfigureAwait(false);
            if (report == null) return null;

            var resultDto = (string.IsNullOrWhiteSpace(report.SummaryJson) && string.IsNullOrWhiteSpace(report.DataJson) && string.IsNullOrWhiteSpace(report.SchemaVersion))
                ? null
                : new ReportResultDto(TryParseJson(report.SummaryJson), TryParseJson(report.DataJson), report.SchemaVersion);

            var pdfDto = string.IsNullOrWhiteSpace(report.PdfUrl)
                ? null
                : new ReportPdfArtifactDto(report.PdfUrl, report.PdfFileName, report.PdfContentHash, report.PdfGeneratedAt);

            return new ReportDetailDto(
                report.Id,
                report.ReportType,
                report.CompanyId,
                report.WarehouseId,
                report.Status,
                report.TimeFrom,
                report.TimeTo,
                report.CreatedAt,
                report.CompletedAt,
                report.ErrorMessage,
                resultDto,
                pdfDto);
        }

        public async Task<ReportPdfArtifactDto> ExportReportPdfAsync(int companyId, int reportId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (reportId <= 0) throw new ArgumentException("Invalid reportId.", nameof(reportId));

            var report = await _repo.GetReportByIdAsync(companyId, reportId).ConfigureAwait(false);
            if (report == null)
                throw new InvalidOperationException("Report not found.");

            if (!string.Equals(report.Status, ReportStatus.Succeeded, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Report is not ready for export. Current status: '{report.Status}'.");

            if (string.IsNullOrWhiteSpace(report.DataJson))
                throw new InvalidOperationException("Report result data is missing.");

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            byte[] pdfBytes;
            if (string.Equals(report.ReportType, ReportTypes.OutboundKpiBasic, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<OutboundKpiBasicReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateOutboundKpiBasicPdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.InventoryTracking, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<InventoryTrackingReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateInventoryTrackingPdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.InboundKpiBasic, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<InboundKpiBasicReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateInboundKpiBasicPdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.InventorySnapshot, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<InventorySnapshotReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateInventorySnapshotPdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.InventoryLedger, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<InventoryLedgerReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateInventoryLedgerPdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.InventoryInOutBalance, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<InventoryInOutBalanceReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateInventoryInOutBalancePdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.StocktakeVariance, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<StocktakeVarianceReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateStocktakeVariancePdf(report, data);
            }
            else
            {
                throw new InvalidOperationException($"PDF export is not implemented for report type '{report.ReportType}'.");
            }

            var fileName = $"report_{report.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
            var contentHash = ComputeSha256Hex(pdfBytes);

            await using var stream = new MemoryStream(pdfBytes);
            var uploadParams = new RawUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = "reports",
                UseFilename = true,
                UniqueFilename = true
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams).ConfigureAwait(false);
            if (uploadResult.Error != null)
                throw new InvalidOperationException(uploadResult.Error.Message);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            report.PdfUrl = uploadResult.SecureUrl?.ToString();
            report.PdfFileName = fileName;
            report.PdfContentHash = contentHash;
            report.PdfGeneratedAt = now;
            await _repo.UpdateReportAsync(report).ConfigureAwait(false);

            return new ReportPdfArtifactDto(report.PdfUrl, report.PdfFileName, report.PdfContentHash, report.PdfGeneratedAt);
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static JsonElement? TryParseJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                // If stored JSON is invalid/corrupt, don't break the whole response.
                return null;
            }
        }

        private static byte[] GenerateOutboundKpiBasicPdf(Report report, OutboundKpiBasicReportData data)
        {
            var completedChart = BuildCompletedCountByDayChart(data);
            var leadTimeChart = BuildAvgLeadTimeByDayChart(data);

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text($"Report: {report.ReportType}").FontSize(16).SemiBold();
                        col.Item().Text($"ReportId: {report.Id}   CompanyId: {report.CompanyId}   WarehouseId: {(report.WarehouseId?.ToString() ?? "All")}");
                        col.Item().Text($"Range: {data.TimeFrom:yyyy-MM-dd} -> {data.TimeTo:yyyy-MM-dd}");
                        col.Item().PaddingTop(5).LineHorizontal(1);
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Border(1).Padding(8).Column(box =>
                            {
                                box.Item().Text("Total completed").SemiBold();
                                box.Item().Text(data.TotalCompleted.ToString()).FontSize(14);
                            });

                            row.RelativeItem().Border(1).Padding(8).Column(box =>
                            {
                                box.Item().Text("Avg lead time (hours)").SemiBold();
                                box.Item().Text(data.OverallAvgLeadTimeHours?.ToString("0.##") ?? "-").FontSize(14);
                            });
                        });

                        if (completedChart != null)
                        {
                            col.Item().Text("Completed count by day").SemiBold();
                            col.Item().Image(completedChart);
                        }
                        else
                        {
                            col.Item().Text("No completed orders in selected range.");
                        }

                        if (leadTimeChart != null)
                        {
                            col.Item().Text("Average lead time (hours) by day").SemiBold();
                            col.Item().Image(leadTimeChart);
                        }

                        col.Item().Text("Top staff throughput").SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(110);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Staff");
                                header.Cell().Element(CellStyle).AlignRight().Text("Completed");
                                header.Cell().Element(CellStyle).AlignRight().Text("Avg lead time (h)");
                            });

                            foreach (var s in data.ByStaff.Take(10))
                            {
                                table.Cell().Element(CellStyle).Text($"{s.StaffName ?? "(unknown)"} (#{s.StaffId})");
                                table.Cell().Element(CellStyle).AlignRight().Text(s.CompletedCount.ToString());
                                table.Cell().Element(CellStyle).AlignRight().Text(s.AvgLeadTimeHours?.ToString("0.##") ?? "-");
                            }

                            static IContainer CellStyle(IContainer container)
                            {
                                return container.BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(2);
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Generated at ");
                        x.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).SemiBold();
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static byte[]? BuildCompletedCountByDayChart(OutboundKpiBasicReportData data)
        {
            if (data.ByDay == null || data.ByDay.Count == 0) return null;

            var days = data.ByDay.Select(x => x.Day).ToList();
            var positions = Enumerable.Range(0, days.Count).Select(i => (double)i).ToArray();
            var values = data.ByDay.Select(x => (double)x.Count).ToArray();

            var plot = new Plot();
            plot.Add.Bars(positions, values);
            plot.Axes.Margins(bottom: 0);
            plot.Title("Completed orders");
            plot.YLabel("Count");

            var ticks = new ScottPlot.TickGenerators.NumericManual();
            for (var i = 0; i < days.Count; i++)
                ticks.AddMajor(i, days[i].ToString("MM-dd"));
            plot.Axes.Bottom.TickGenerator = ticks;
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;

            return plot.GetImageBytes(800, 350, ScottPlot.ImageFormat.Png);
        }

        private static byte[]? BuildAvgLeadTimeByDayChart(OutboundKpiBasicReportData data)
        {
            if (data.ByDay == null || data.ByDay.Count == 0) return null;

            var days = data.ByDay.Select(x => x.Day).ToList();
            var positions = Enumerable.Range(0, days.Count).Select(i => (double)i).ToArray();
            var values = data.ByDay.Select(x => x.AvgLeadTimeHours ?? 0).ToArray();

            var plot = new Plot();
            plot.Add.Scatter(positions, values);
            plot.Title("Average lead time");
            plot.YLabel("Hours");

            var ticks = new ScottPlot.TickGenerators.NumericManual();
            for (var i = 0; i < days.Count; i++)
                ticks.AddMajor(i, days[i].ToString("MM-dd"));
            plot.Axes.Bottom.TickGenerator = ticks;
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;

            return plot.GetImageBytes(800, 350, ScottPlot.ImageFormat.Png);
        }

        private static byte[] GenerateInventoryTrackingPdf(Report report, InventoryTrackingReportData data)
        {
            var inboundChart = BuildInventoryDailyChart(data);

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text($"Report: {report.ReportType}").FontSize(16).SemiBold();
                        col.Item().Text($"ReportId: {report.Id}   CompanyId: {report.CompanyId}   WarehouseId: {(report.WarehouseId?.ToString() ?? "All")}");
                        col.Item().Text($"Range: {data.TimeFrom:yyyy-MM-dd} -> {data.TimeTo:yyyy-MM-dd}");
                        col.Item().PaddingTop(5).LineHorizontal(1);
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Border(1).Padding(8).Column(box =>
                            {
                                box.Item().Text("Inbound transactions").SemiBold();
                                box.Item().Text(data.TotalInboundTransactions.ToString()).FontSize(14);
                                box.Item().Text($"Total qty: {data.TotalInboundQty}");
                            });
                            row.RelativeItem().Border(1).Padding(8).Column(box =>
                            {
                                box.Item().Text("Outbound transactions").SemiBold();
                                box.Item().Text(data.TotalOutboundTransactions.ToString()).FontSize(14);
                                box.Item().Text($"Total qty: {data.TotalOutboundQty}");
                            });
                        });

                        if (inboundChart != null)
                        {
                            col.Item().Text("Daily inbound / outbound (transaction count)").SemiBold();
                            col.Item().Image(inboundChart);
                        }
                        else
                        {
                            col.Item().Text("No transactions in selected range.");
                        }

                        col.Item().Text("Top products by movement").SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(65);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Product");
                                header.Cell().Element(CellStyle).AlignRight().Text("In qty");
                                header.Cell().Element(CellStyle).AlignRight().Text("Out qty");
                                header.Cell().Element(CellStyle).AlignRight().Text("Net");
                                header.Cell().Element(CellStyle).AlignRight().Text("Stock now");
                            });

                            foreach (var p in data.TopProducts)
                            {
                                table.Cell().Element(CellStyle).Text($"{p.ProductName ?? "(unknown)"} ({p.Sku ?? "-"})");
                                table.Cell().Element(CellStyle).AlignRight().Text(p.InboundQty.ToString());
                                table.Cell().Element(CellStyle).AlignRight().Text(p.OutboundQty.ToString());
                                table.Cell().Element(CellStyle).AlignRight().Text(p.NetChange.ToString());
                                table.Cell().Element(CellStyle).AlignRight().Text(p.CurrentStock?.ToString() ?? "-");
                            }

                            static IContainer CellStyle(IContainer container)
                                => container.BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(2);
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Generated at ");
                        x.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).SemiBold();
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static byte[]? BuildInventoryDailyChart(InventoryTrackingReportData data)
        {
            if (data.ByDay == null || data.ByDay.Count == 0) return null;

            var days = data.ByDay.Select(x => x.Day).ToList();
            var positions = Enumerable.Range(0, days.Count).Select(i => (double)i).ToArray();
            var inCounts = data.ByDay.Select(x => (double)x.InboundCount).ToArray();
            var outCounts = data.ByDay.Select(x => (double)x.OutboundCount).ToArray();

            var plot = new Plot();
            var barsIn = plot.Add.Bars(positions, inCounts);
            barsIn.LegendText = "Inbound";
            var barsOut = plot.Add.Bars(positions.Select(p => p + 0.4).ToArray(), outCounts);
            barsOut.LegendText = "Outbound";
            plot.ShowLegend();
            plot.Title("Daily transactions");
            plot.YLabel("Count");

            var ticks = new ScottPlot.TickGenerators.NumericManual();
            for (var i = 0; i < days.Count; i++)
                ticks.AddMajor(i + 0.2, days[i].ToString("MM-dd"));
            plot.Axes.Bottom.TickGenerator = ticks;
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;

            return plot.GetImageBytes(800, 350, ScottPlot.ImageFormat.Png);
        }

        private static byte[] GenerateInboundKpiBasicPdf(Report report, InboundKpiBasicReportData data)
        {
            var dailyChart = BuildInboundDailyChart(data);

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text($"Report: {report.ReportType}").FontSize(16).SemiBold();
                        col.Item().Text($"ReportId: {report.Id}   CompanyId: {report.CompanyId}   WarehouseId: {(report.WarehouseId?.ToString() ?? "All")}");
                        col.Item().Text($"Range: {data.TimeFrom:yyyy-MM-dd} -> {data.TimeTo:yyyy-MM-dd}");
                        col.Item().PaddingTop(5).LineHorizontal(1);
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Border(1).Padding(8).Column(box =>
                            {
                                box.Item().Text("Total completed inbound orders").SemiBold();
                                box.Item().Text(data.TotalCompleted.ToString()).FontSize(14);
                            });
                            row.RelativeItem().Border(1).Padding(8).Column(box =>
                            {
                                box.Item().Text("Total received qty").SemiBold();
                                box.Item().Text(data.TotalReceivedQty.ToString()).FontSize(14);
                            });
                        });

                        if (dailyChart != null)
                        {
                            col.Item().Text("Completed inbound orders by day").SemiBold();
                            col.Item().Image(dailyChart);
                        }
                        else
                        {
                            col.Item().Text("No completed inbound orders in selected range.");
                        }

                        col.Item().Text("Top suppliers").SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(90);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Supplier");
                                header.Cell().Element(CellStyle).AlignRight().Text("Orders");
                                header.Cell().Element(CellStyle).AlignRight().Text("Received qty");
                            });

                            foreach (var s in data.BySupplier.Take(10))
                            {
                                table.Cell().Element(CellStyle).Text($"{s.SupplierName ?? "(unknown)"} (#{s.SupplierId})");
                                table.Cell().Element(CellStyle).AlignRight().Text(s.CompletedCount.ToString());
                                table.Cell().Element(CellStyle).AlignRight().Text(s.ReceivedQty.ToString());
                            }

                            static IContainer CellStyle(IContainer container)
                                => container.BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(2);
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Generated at ");
                        x.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).SemiBold();
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static byte[]? BuildInboundDailyChart(InboundKpiBasicReportData data)
        {
            if (data.ByDay == null || data.ByDay.Count == 0) return null;

            var days = data.ByDay.Select(x => x.Day).ToList();
            var positions = Enumerable.Range(0, days.Count).Select(i => (double)i).ToArray();
            var values = data.ByDay.Select(x => (double)x.Count).ToArray();

            var plot = new Plot();
            plot.Add.Bars(positions, values);
            plot.Axes.Margins(bottom: 0);
            plot.Title("Completed inbound orders");
            plot.YLabel("Count");

            var ticks = new ScottPlot.TickGenerators.NumericManual();
            for (var i = 0; i < days.Count; i++)
                ticks.AddMajor(i, days[i].ToString("MM-dd"));
            plot.Axes.Bottom.TickGenerator = ticks;
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;

            return plot.GetImageBytes(800, 350, ScottPlot.ImageFormat.Png);
        }

        private static byte[] GenerateInventorySnapshotPdf(Report report, InventorySnapshotReportData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Text($"Report: {report.ReportType} (#{report.Id})").SemiBold().FontSize(14);
                    page.Content().Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Text($"Total SKU: {data.TotalSkus} | Total Qty: {data.TotalQuantity} | Total Value: {data.TotalValue:0.##}");
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(80); c.ConstantColumn(80); c.ConstantColumn(90); });
                            table.Header(h =>
                            {
                                h.Cell().Text("Product");
                                h.Cell().AlignRight().Text("Qty");
                                h.Cell().AlignRight().Text("Unit cost");
                                h.Cell().AlignRight().Text("Value");
                            });
                            foreach (var item in data.Items.Take(30))
                            {
                                table.Cell().Text($"{item.ProductName ?? "(unknown)"} ({item.Sku ?? "-"})");
                                table.Cell().AlignRight().Text(item.Quantity.ToString());
                                table.Cell().AlignRight().Text(item.UnitCost.ToString("0.##"));
                                table.Cell().AlignRight().Text(item.InventoryValue.ToString("0.##"));
                            }
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static byte[] GenerateInventoryLedgerPdf(Report report, InventoryLedgerReportData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Text($"Report: {report.ReportType} (#{report.Id})").SemiBold().FontSize(14);
                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Text($"Opening: {data.OpeningQuantity} | Closing: {data.ClosingQuantity}");
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.ConstantColumn(70); c.RelativeColumn(); c.ConstantColumn(65); c.ConstantColumn(55); c.ConstantColumn(55); c.ConstantColumn(70); });
                            table.Header(h =>
                            {
                                h.Cell().Text("Date");
                                h.Cell().Text("Product");
                                h.Cell().Text("Type");
                                h.Cell().AlignRight().Text("In");
                                h.Cell().AlignRight().Text("Out");
                                h.Cell().AlignRight().Text("Running");
                            });
                            foreach (var r in data.Rows.Take(120))
                            {
                                table.Cell().Text(r.Day.ToString("yyyy-MM-dd"));
                                table.Cell().Text($"{r.ProductName ?? "(unknown)"} ({r.Sku ?? "-"})");
                                table.Cell().Text(r.TransactionType ?? "-");
                                table.Cell().AlignRight().Text(r.QuantityIn.ToString());
                                table.Cell().AlignRight().Text(r.QuantityOut.ToString());
                                table.Cell().AlignRight().Text(r.RunningQuantity.ToString());
                            }
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static byte[] GenerateInventoryInOutBalancePdf(Report report, InventoryInOutBalanceReportData data)
        {
            var chart = BuildInventoryInOutBalanceChart(data);
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Text($"Report: {report.ReportType} (#{report.Id})").SemiBold().FontSize(14);
                    page.Content().Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Text($"Opening: {data.TotalOpeningQty} | In: {data.TotalInboundQty} | Out: {data.TotalOutboundQty} | Closing: {data.TotalClosingQty} | Closing value: {data.TotalClosingValue:0.##}");
                        if (chart != null) col.Item().Image(chart);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(55); c.ConstantColumn(55); c.ConstantColumn(55); c.ConstantColumn(55); c.ConstantColumn(80); });
                            table.Header(h =>
                            {
                                h.Cell().Text("Product");
                                h.Cell().AlignRight().Text("Open");
                                h.Cell().AlignRight().Text("In");
                                h.Cell().AlignRight().Text("Out");
                                h.Cell().AlignRight().Text("Close");
                                h.Cell().AlignRight().Text("Value");
                            });
                            foreach (var p in data.ByProduct.Take(30))
                            {
                                table.Cell().Text($"{p.ProductName ?? "(unknown)"} ({p.Sku ?? "-"})");
                                table.Cell().AlignRight().Text(p.OpeningQty.ToString());
                                table.Cell().AlignRight().Text(p.InboundQty.ToString());
                                table.Cell().AlignRight().Text(p.OutboundQty.ToString());
                                table.Cell().AlignRight().Text(p.ClosingQty.ToString());
                                table.Cell().AlignRight().Text(p.ClosingValue.ToString("0.##"));
                            }
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static byte[]? BuildInventoryInOutBalanceChart(InventoryInOutBalanceReportData data)
        {
            if (data.ByDay == null || data.ByDay.Count == 0) return null;
            var days = data.ByDay.Select(d => d.Day).ToList();
            var x = Enumerable.Range(0, days.Count).Select(i => (double)i).ToArray();
            var inValues = data.ByDay.Select(d => (double)d.InboundQty).ToArray();
            var outValues = data.ByDay.Select(d => (double)d.OutboundQty).ToArray();

            var plot = new Plot();
            var inBars = plot.Add.Bars(x, inValues);
            inBars.LegendText = "Inbound";
            var outBars = plot.Add.Bars(x.Select(v => v + 0.4).ToArray(), outValues);
            outBars.LegendText = "Outbound";
            plot.ShowLegend();
            var ticks = new ScottPlot.TickGenerators.NumericManual();
            for (int i = 0; i < days.Count; i++) ticks.AddMajor(i + 0.2, days[i].ToString("MM-dd"));
            plot.Axes.Bottom.TickGenerator = ticks;
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
            return plot.GetImageBytes(800, 320, ScottPlot.ImageFormat.Png);
        }

        private static byte[] GenerateStocktakeVariancePdf(Report report, StocktakeVarianceReportData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Text($"Report: {report.ReportType} (#{report.Id})").SemiBold().FontSize(14);
                    page.Content().Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Text($"Items: {data.TotalItems} | Total variance qty: {data.TotalVarianceQty} | Total variance value: {data.TotalVarianceValue:0.##}");
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(55); c.ConstantColumn(55); c.ConstantColumn(55); c.ConstantColumn(80); });
                            table.Header(h =>
                            {
                                h.Cell().Text("Product");
                                h.Cell().AlignRight().Text("System");
                                h.Cell().AlignRight().Text("Counted");
                                h.Cell().AlignRight().Text("Var");
                                h.Cell().AlignRight().Text("Var value");
                            });
                            foreach (var r in data.Items.Take(40))
                            {
                                table.Cell().Text($"{r.ProductName ?? "(unknown)"} ({r.Sku ?? "-"})");
                                table.Cell().AlignRight().Text(r.SystemQty.ToString());
                                table.Cell().AlignRight().Text(r.CountedQty.ToString());
                                table.Cell().AlignRight().Text(r.VarianceQty.ToString());
                                table.Cell().AlignRight().Text(r.VarianceValue.ToString("0.##"));
                            }
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }

        public async Task<List<ReportRequestListItemDto>> ListReportsAsync(
            int companyId,
            string? reportType,
            int? warehouseId,
            DateTime? from,
            DateTime? to,
            int skip,
            int take)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (skip < 0) skip = 0;
            if (take <= 0) take = 50;
            if (take > 200) take = 200;

            var items = await _repo.ListReportsAsync(companyId, reportType, warehouseId, from, to, skip, take)
                .ConfigureAwait(false);

            return items.Select(r => new ReportRequestListItemDto(
                r.Id,
                r.ReportType,
                r.WarehouseId,
                r.Status,
                r.TimeFrom,
                r.TimeTo,
                r.CreatedAt,
                r.CompletedAt,
                r.ErrorMessage)).ToList();
        }

    }
}

