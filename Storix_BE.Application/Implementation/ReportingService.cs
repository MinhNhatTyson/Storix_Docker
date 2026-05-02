using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
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
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class ReportingService : IReportingService
    {
        private const string AiRecommendationSchemaVersion = "ai-recommendation-v1";
        private const string AiRecommendationFeSchemaVersion = "ai-recommendation-fe-v1";
        private const string AiRecommendationBasicSource = "AI_RECOMMENDATION_BASIC";
        private const string AiRecommendationFeSource = "FE_AI_RECOMMENDATION";

        private static readonly HashSet<string> SupportedReportTypes = new(StringComparer.Ordinal)
        {
            ReportTypes.InventorySnapshot,
            ReportTypes.InventoryLedger,
            ReportTypes.InventoryOverallLedger,
            ReportTypes.InventoryInOutBalance,
            ReportTypes.InventoryTracking,
            ReportTypes.ReplenishmentRecommendation
        };

        private static readonly Dictionary<string, string> ReportTypeAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["inventorysnapshot"] = ReportTypes.InventorySnapshot,
            ["inventory-snapshot"] = ReportTypes.InventorySnapshot,
            ["inventoryledger"] = ReportTypes.InventoryLedger,
            ["inventory-ledger"] = ReportTypes.InventoryLedger,
            ["inventoryoverallledger"] = ReportTypes.InventoryOverallLedger,
            ["inventory-overall-ledger"] = ReportTypes.InventoryOverallLedger,
            ["inventoryinoutbalance"] = ReportTypes.InventoryInOutBalance,
            ["inventory-in-out-balance"] = ReportTypes.InventoryInOutBalance,
            ["inventory_tracking"] = ReportTypes.InventoryTracking,
            ["inventorytracking"] = ReportTypes.InventoryTracking,
            ["inventory-tracking"] = ReportTypes.InventoryTracking,
            ["stocktakevariance"] = ReportTypes.InventoryTracking,
            ["replenishmentrecommendation"] = ReportTypes.ReplenishmentRecommendation,
            ["replenishment-recommendation"] = ReportTypes.ReplenishmentRecommendation
        };

        private readonly IReportingRepository _repo;
        private readonly Cloudinary _cloudinary;
        private readonly ILogger<ReportingService> _logger;

        public ReportingService(IReportingRepository repo, Cloudinary cloudinary, ILogger<ReportingService> logger)
        {
            _repo = repo;
            _cloudinary = cloudinary;
            _logger = logger;
        }

        public async Task<ReportDetailDto> CreateReportAsync(int companyId, int createdByUserId, CreateReportRequest payload)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (createdByUserId <= 0) throw new ArgumentException("Invalid createdByUserId.", nameof(createdByUserId));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrWhiteSpace(payload.ReportType)) throw new ArgumentException("ReportType is required.", nameof(payload.ReportType));
            var timeFrom = NormalizeToUnspecified(payload.TimeFrom);
            var timeTo = NormalizeToUnspecified(payload.TimeTo);
            if (timeTo < timeFrom) throw new ArgumentException("TimeTo must be >= TimeFrom.");
            await ValidateCreatePayloadScopeAsync(companyId, payload).ConfigureAwait(false);
            var normalizedReportType = NormalizeReportType(payload.ReportType);

            var createdAt = NormalizeToUnspecified(DateTime.UtcNow);

            var paramsOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

            var report = new Report
            {
                CompanyId = companyId,
                CreatedByUserId = createdByUserId,
                ReportType = normalizedReportType,
                WarehouseId = payload.WarehouseId,
                ProductId = payload.ProductId,
                InventoryCountTicketId = payload.InventoryCountTicketId,
                TimeFrom = timeFrom,
                TimeTo = timeTo,
                Status = ReportStatus.Running,
                CreatedAt = createdAt,
                ParametersJson = JsonSerializer.Serialize(new
                {
                    reportType = normalizedReportType,
                    warehouseId = payload.WarehouseId,
                    productId = payload.ProductId,
                    inventoryCountTicketId = payload.InventoryCountTicketId,
                    timeFrom = payload.TimeFrom,
                    timeTo = payload.TimeTo,
                    forecastHorizonDays = payload.ForecastHorizonDays,
                    defaultLeadTimeDays = payload.DefaultLeadTimeDays,
                    serviceLevel = payload.ServiceLevel,
                    useAiExplanation = payload.UseAiExplanation
                }, paramsOptions)
            };

            _logger.LogInformation(
                "Creating report draft. CompanyId={CompanyId}, UserId={UserId}, ReportType={ReportType}, WarehouseId={WarehouseId}, ProductId={ProductId}, InventoryCountTicketId={InventoryCountTicketId}, TimeFrom={TimeFrom}, TimeTo={TimeTo}",
                companyId,
                createdByUserId,
                normalizedReportType,
                payload.WarehouseId,
                payload.ProductId,
                payload.InventoryCountTicketId,
                payload.TimeFrom,
                payload.TimeTo);

            try
            {
                report = await _repo.CreateReportAsync(report).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist report draft. CompanyId={CompanyId}, UserId={UserId}, ReportType={ReportType}",
                    companyId,
                    createdByUserId,
                    normalizedReportType);
                throw;
            }

            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };

                if (string.Equals(normalizedReportType, ReportTypes.InventorySnapshot, StringComparison.Ordinal))
                {
                    var snapshot = await _repo.GetInventorySnapshotAsync(companyId, payload.WarehouseId, timeFrom, timeTo)
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
                else if (string.Equals(normalizedReportType, ReportTypes.InventoryLedger, StringComparison.Ordinal))
                {
                    var ledger = await _repo.GetInventoryLedgerAsync(companyId, payload.WarehouseId, payload.ProductId, timeFrom, timeTo)
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
                else if (string.Equals(normalizedReportType, ReportTypes.InventoryOverallLedger, StringComparison.Ordinal))
                {
                    var overall = await _repo.GetInventoryOverallLedgerAsync(companyId, payload.WarehouseId, timeFrom, timeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalOpeningQty = overall.TotalOpeningQty,
                        totalInboundQty = overall.TotalInboundQty,
                        totalOutboundQty = overall.TotalOutboundQty,
                        totalClosingQty = overall.TotalClosingQty,
                        totalClosingValue = overall.TotalClosingValue
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(overall, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.InventoryOverallLedger;
                }
                else if (string.Equals(normalizedReportType, ReportTypes.InventoryInOutBalance, StringComparison.Ordinal))
                {
                    var inOut = await _repo.GetInventoryInOutBalanceAsync(companyId, payload.WarehouseId, timeFrom, timeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalTransactions = inOut.TotalTransactions,
                        totalInboundTransactions = inOut.TotalInboundTransactions,
                        totalOutboundTransactions = inOut.TotalOutboundTransactions,
                        totalItemLines = inOut.TotalItemLines
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(inOut, jsonOptions);
                    report.SchemaVersion = "inout-balance-v2";
                }
                else if (string.Equals(normalizedReportType, ReportTypes.InventoryTracking, StringComparison.Ordinal))
                {
                    var stocktake = await _repo.GetStocktakeVarianceAsync(companyId, payload.WarehouseId, payload.InventoryCountTicketId, timeFrom, timeTo)
                        .ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalItems = stocktake.TotalItems,
                        totalVarianceQty = stocktake.TotalVarianceQty,
                        totalVarianceValue = stocktake.TotalVarianceValue
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(stocktake, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.InventoryTrackingGrouped;
                }
                else if (string.Equals(normalizedReportType, ReportTypes.ReplenishmentRecommendation, StringComparison.Ordinal))
                {
                    var forecastHorizonDays = payload.ForecastHorizonDays.GetValueOrDefault(14);
                    var defaultLeadTimeDays = payload.DefaultLeadTimeDays.GetValueOrDefault(7);
                    var serviceLevel = payload.ServiceLevel.GetValueOrDefault(0.95);
                    var useAiExplanation = payload.UseAiExplanation.GetValueOrDefault(true);

                    var recommendation = await _repo.GetReplenishmentRecommendationDataAsync(
                        companyId,
                        payload.WarehouseId,
                        timeFrom,
                        timeTo,
                        forecastHorizonDays,
                        defaultLeadTimeDays,
                        serviceLevel,
                        useAiExplanation).ConfigureAwait(false);

                    report.SummaryJson = JsonSerializer.Serialize(new
                    {
                        totalSkusAnalyzed = recommendation.Summary.TotalSkusAnalyzed,
                        totalSkusRecommended = recommendation.Summary.TotalSkusRecommended,
                        totalRecommendedQty = recommendation.Summary.TotalRecommendedQty,
                        highRiskSkus = recommendation.Summary.HighRiskSkus
                    }, jsonOptions);
                    report.DataJson = JsonSerializer.Serialize(recommendation, jsonOptions);
                    report.SchemaVersion = ReportSchemaVersions.ReplenishmentRecommendation;
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported report type '{normalizedReportType}'.");
                }

                report.Status = ReportStatus.Succeeded;
                report.CompletedAt = NormalizeToUnspecified(DateTime.UtcNow);
                report.ErrorMessage = null;
                _logger.LogInformation(
                    "Report generation succeeded. ReportId={ReportId}, ReportType={ReportType}, Status={Status}",
                    report.Id,
                    report.ReportType,
                    report.Status);
                await _repo.UpdateReportAsync(report).ConfigureAwait(false);

                return new ReportDetailDto(
                    report.Id,
                    NormalizeReportTypeForResponse(report.ReportType),
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
                report.CompletedAt = NormalizeToUnspecified(DateTime.UtcNow);
                report.ErrorMessage = ex.Message;
                _logger.LogError(
                    ex,
                    "Report generation failed. ReportId={ReportId}, ReportType={ReportType}, CompanyId={CompanyId}, ExceptionType={ExceptionType}, InnerExceptionType={InnerExceptionType}",
                    report.Id,
                    report.ReportType,
                    companyId,
                    ex.GetType().FullName,
                    ex.InnerException?.GetType().FullName);

                try
                {
                    await _repo.UpdateReportAsync(report).ConfigureAwait(false);
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(
                        updateEx,
                        "Failed to persist report failure state. ReportId={ReportId}, OriginalError={OriginalError}",
                        report.Id,
                        ex.Message);
                }

                throw;
            }
        }

        public async Task<ReportDetailDto?> GetReportAsync(int companyId, int reportId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (reportId <= 0) throw new ArgumentException("Invalid reportId.", nameof(reportId));

            var report = await _repo.GetReportByIdAsync(companyId, reportId).ConfigureAwait(false);
            if (report == null) return null;

            var normalizedDataJson = NormalizeReportDataForRead(report.DataJson, report.SchemaVersion);
            var resultDto = (string.IsNullOrWhiteSpace(report.SummaryJson) && string.IsNullOrWhiteSpace(report.DataJson) && string.IsNullOrWhiteSpace(report.SchemaVersion))
                ? null
                : new ReportResultDto(TryParseJson(report.SummaryJson), TryParseJson(normalizedDataJson), report.SchemaVersion);

            var pdfDto = string.IsNullOrWhiteSpace(report.PdfUrl)
                ? null
                : new ReportPdfArtifactDto(report.PdfUrl, report.PdfFileName, report.PdfContentHash, report.PdfGeneratedAt);

            return new ReportDetailDto(
                report.Id,
                NormalizeReportTypeForResponse(report.ReportType),
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

        public async Task<ReportDetailDto> UpdateAiRecommendationAsync(int companyId, int reportId, IReadOnlyList<AiRecommendationItemDto> recommendations)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (reportId <= 0) throw new ArgumentException("Invalid reportId.", nameof(reportId));
            if (recommendations == null) throw new ArgumentNullException(nameof(recommendations));
            if (!recommendations.Any()) throw new ArgumentException("At least one recommendation is required.", nameof(recommendations));

            var report = await _repo.GetReportByIdAsync(companyId, reportId).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Report not found.");

            var savedAt = NormalizeToUnspecified(DateTime.UtcNow);
            var aiPayload = new
            {
                source = AiRecommendationBasicSource,
                items = recommendations.Select(x => new
                {
                    productId = x.ProductId,
                    forecastedQuantity = Math.Max(0, x.ForecastedQuantity),
                    reason = x.Reason
                }).ToList(),
                savedAt,
                version = AiRecommendationSchemaVersion
            };

            report.DataJson = MergeAiRecommendationIntoReportData(report.DataJson, aiPayload);
            report.SchemaVersion = AiRecommendationSchemaVersion;
            report.CompletedAt = savedAt;
            report.Status = ReportStatus.Succeeded;
            report.ErrorMessage = null;
            await _repo.UpdateReportAsync(report).ConfigureAwait(false);

            var normalizedDataJson = NormalizeReportDataForRead(report.DataJson, report.SchemaVersion);
            return new ReportDetailDto(
                report.Id,
                NormalizeReportTypeForResponse(report.ReportType),
                report.CompanyId,
                report.WarehouseId,
                report.Status,
                report.TimeFrom,
                report.TimeTo,
                report.CreatedAt,
                report.CompletedAt,
                report.ErrorMessage,
                new ReportResultDto(TryParseJson(report.SummaryJson), TryParseJson(normalizedDataJson), report.SchemaVersion),
                report.PdfUrl == null ? null : new ReportPdfArtifactDto(report.PdfUrl, report.PdfFileName, report.PdfContentHash, report.PdfGeneratedAt));
        }

        public async Task<ReportDetailDto> SaveAiRecommendationPayloadAsync(int companyId, SaveAiRecommendationPayloadRequest request)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.ReportId <= 0) throw new ArgumentException("Invalid reportId.", nameof(request.ReportId));
            if (request.Recommendations == null || request.Recommendations.Count == 0)
                throw new ArgumentException("At least one recommendation is required.", nameof(request.Recommendations));

            var report = await _repo.GetReportByIdAsync(companyId, request.ReportId).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Report not found.");

            var normalized = request.Recommendations.Select(x => new
            {
                productId = x.ProductId,
                productName = x.ProductName,
                forecastedQuantity = Math.Max(0, x.ForecastedQuantity),
                isSlowMoving = x.IsSlowMoving,
                slowMovingWarning = x.SlowMovingWarning,
                needsRestock = x.NeedsRestock,
                suggestedRestockQuantity = Math.Max(0, x.SuggestedRestockQuantity),
                reason = x.Reason
            }).ToList();

            var summary = new
            {
                totalItems = normalized.Count,
                restockItems = normalized.Count(x => x.needsRestock),
                slowMovingItems = normalized.Count(x => x.isSlowMoving),
                totalSuggestedRestock = normalized.Sum(x => x.suggestedRestockQuantity)
            };

            var savedAt = NormalizeToUnspecified(DateTime.UtcNow);
            var aiRecommendation = new
            {
                source = AiRecommendationFeSource,
                items = normalized,
                savedAt,
                version = AiRecommendationFeSchemaVersion
            };

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            report.DataJson = MergeAiRecommendationIntoReportData(report.DataJson, aiRecommendation);
            report.SummaryJson = JsonSerializer.Serialize(summary, options);
            report.SchemaVersion = AiRecommendationFeSchemaVersion;
            report.Status = ReportStatus.Succeeded;
            report.CompletedAt = savedAt;
            report.ErrorMessage = null;

            await _repo.UpdateReportAsync(report).ConfigureAwait(false);

            var normalizedDataJson = NormalizeReportDataForRead(report.DataJson, report.SchemaVersion);
            return new ReportDetailDto(
                report.Id,
                NormalizeReportTypeForResponse(report.ReportType),
                report.CompanyId,
                report.WarehouseId,
                report.Status,
                report.TimeFrom,
                report.TimeTo,
                report.CreatedAt,
                report.CompletedAt,
                report.ErrorMessage,
                new ReportResultDto(TryParseJson(report.SummaryJson), TryParseJson(normalizedDataJson), report.SchemaVersion),
                report.PdfUrl == null ? null : new ReportPdfArtifactDto(report.PdfUrl, report.PdfFileName, report.PdfContentHash, report.PdfGeneratedAt));
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

            if (string.IsNullOrWhiteSpace(report.ReportType))
                throw new InvalidOperationException("Report type is missing.");

            if (string.IsNullOrWhiteSpace(report.DataJson))
                throw new InvalidOperationException("Report result data is missing.");

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            byte[] pdfBytes;
            if (string.Equals(report.ReportType, ReportTypes.InventorySnapshot, StringComparison.Ordinal))
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
            else if (string.Equals(report.ReportType, ReportTypes.InventoryOverallLedger, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<InventoryOverallLedgerReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateInventoryOverallLedgerPdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.InventoryInOutBalance, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<InventoryInOutBalanceReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateInventoryInOutBalancePdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.InventoryTracking, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<StocktakeVarianceReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateStocktakeVariancePdf(report, data);
            }
            else if (string.Equals(report.ReportType, ReportTypes.ReplenishmentRecommendation, StringComparison.Ordinal))
            {
                var data = JsonSerializer.Deserialize<ReplenishmentRecommendationReportData>(report.DataJson, jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize report data.");
                pdfBytes = GenerateReplenishmentRecommendationPdf(report, data);
            }
            else
            {
                throw new InvalidOperationException($"PDF export is not implemented for report type '{report.ReportType}'.");
            }

            var fileName = $"report_{report.Id}_{NormalizeToUnspecified(DateTime.UtcNow):yyyyMMdd_HHmmss}.pdf";
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

            var now = NormalizeToUnspecified(DateTime.UtcNow);
            var pdfUrl = uploadResult.SecureUrl?.ToString();
            if (string.IsNullOrWhiteSpace(pdfUrl))
                throw new InvalidOperationException("Cloudinary did not return a PDF URL.");

            report.PdfUrl = pdfUrl;
            report.PdfFileName = fileName;
            report.PdfContentHash = contentHash;
            report.PdfGeneratedAt = now;
            await _repo.UpdateReportAsync(report).ConfigureAwait(false);

            return new ReportPdfArtifactDto(pdfUrl, fileName, contentHash, now);
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

        private static string? NormalizeReportDataForRead(string? dataJson, string? schemaVersion)
        {
            if (string.IsNullOrWhiteSpace(dataJson))
                return dataJson;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(dataJson);
            }
            catch
            {
                return dataJson;
            }

            if (node == null)
                return dataJson;

            if (node is JsonArray arrayNode)
            {
                var wrapped = new JsonObject
                {
                    ["aiRecommendation"] = BuildAiRecommendationObject(
                        AiRecommendationBasicSource,
                        arrayNode,
                        null,
                        string.IsNullOrWhiteSpace(schemaVersion) ? AiRecommendationSchemaVersion : schemaVersion)
                };
                return wrapped.ToJsonString();
            }

            if (node is JsonObject objectNode)
            {
                if (objectNode["aiRecommendation"] is JsonObject)
                    return objectNode.ToJsonString();

                if (objectNode["items"] is JsonArray legacyItems &&
                    objectNode["source"] != null)
                {
                    var source = objectNode["source"]?.GetValue<string>() ?? AiRecommendationBasicSource;
                    objectNode["aiRecommendation"] = BuildAiRecommendationObject(
                        source,
                        legacyItems,
                        objectNode["savedAt"],
                        string.IsNullOrWhiteSpace(schemaVersion) ? AiRecommendationSchemaVersion : schemaVersion);
                    objectNode.Remove("source");
                    objectNode.Remove("items");
                    objectNode.Remove("savedAt");
                    objectNode.Remove("version");
                }

                NormalizeLegacyStocktakeShape(objectNode);
                return objectNode.ToJsonString();
            }

            return dataJson;
        }

        private static string MergeAiRecommendationIntoReportData(string? existingDataJson, object aiRecommendationPayload)
        {
            var baseObject = ToJsonObject(existingDataJson);
            var aiNode = JsonSerializer.SerializeToNode(aiRecommendationPayload) ?? new JsonObject();
            baseObject["aiRecommendation"] = aiNode;
            return baseObject.ToJsonString();
        }

        private static JsonObject ToJsonObject(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new JsonObject();

            try
            {
                var parsed = JsonNode.Parse(json);
                if (parsed is JsonObject obj)
                    return obj;

                var wrapper = new JsonObject();
                if (parsed is JsonArray array)
                {
                    wrapper["legacyAiRecommendationItems"] = array;
                }
                else if (parsed != null)
                {
                    wrapper["legacyData"] = parsed;
                }

                return wrapper;
            }
            catch
            {
                return new JsonObject();
            }
        }

        private static JsonObject BuildAiRecommendationObject(string source, JsonArray items, JsonNode? savedAt, string version)
        {
            return new JsonObject
            {
                ["source"] = source,
                ["items"] = items,
                ["savedAt"] = savedAt,
                ["version"] = version
            };
        }

        private static void NormalizeLegacyStocktakeShape(JsonObject objectNode)
        {
            if (objectNode["totalVarianceQty"] == null)
                return;

            if (objectNode["rows"] is JsonArray)
                return;

            if (objectNode["items"] is not JsonArray legacyItems)
                return;

            var group = new JsonObject
            {
                ["date"] = objectNode["timeFrom"]?.DeepClone(),
                ["ticketId"] = objectNode["inventoryCountTicketId"]?.DeepClone(),
                ["ticketName"] = "Stocktake",
                ["createdBy"] = null,
                ["assignedStaff"] = null,
                ["items"] = legacyItems.DeepClone()
            };

            objectNode["rows"] = new JsonArray(group);
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
                        x.Span(NormalizeToUnspecified(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss")).SemiBold();
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
                        x.Span(NormalizeToUnspecified(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss")).SemiBold();
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
                        x.Span(NormalizeToUnspecified(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss")).SemiBold();
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

        private static byte[] GenerateInventoryOverallLedgerPdf(Report report, InventoryOverallLedgerReportData data)
        {
            var chart = BuildInventoryOverallLedgerChart(data);
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

        private static byte[]? BuildInventoryOverallLedgerChart(InventoryOverallLedgerReportData data)
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

        private static byte[] GenerateInventoryInOutBalancePdf(Report report, InventoryInOutBalanceReportData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Column(col =>
                    {
                        col.Item().Text($"Report: {report.ReportType} (#{report.Id})").SemiBold().FontSize(14);
                        col.Item().Text($"Range: {data.TimeFrom:yyyy-MM-dd} -> {data.TimeTo:yyyy-MM-dd}");
                        col.Item().Text($"Transactions: {data.TotalTransactions} | Inbound: {data.TotalInboundTransactions} | Outbound: {data.TotalOutboundTransactions}");
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(8);

                        foreach (var row in data.Rows)
                        {
                            col.Item().Border(1).Padding(6).Column(c =>
                            {
                                c.Spacing(4);
                                c.Item().Text($"{row.Date:yyyy-MM-dd HH:mm} | {row.TransactionType} | Người tạo: {row.NguoiTao ?? "-"} | Nhân viên: {row.NhanVien ?? "-"}");
                                c.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(cd =>
                                    {
                                        cd.RelativeColumn();
                                        cd.ConstantColumn(70);
                                        cd.ConstantColumn(130);
                                    });

                                    table.Header(h =>
                                    {
                                        h.Cell().Text("Product name").SemiBold();
                                        h.Cell().AlignRight().Text("Quantity").SemiBold();
                                        h.Cell().Text("SKU id").SemiBold();
                                    });

                                    foreach (var item in row.Items)
                                    {
                                        table.Cell().Text(item.ProductName ?? "(unknown)");
                                        table.Cell().AlignRight().Text(item.Quantity.ToString());
                                        table.Cell().Text(item.SkuId ?? "-");
                                    }
                                });
                            });
                        }
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static byte[] GenerateStocktakeVariancePdf(Report report, StocktakeVarianceReportData data)
        {
            var rows = data.Rows;
            if (rows == null || rows.Count == 0)
            {
                rows = new[]
                {
                    new StocktakeVarianceGroupRow(
                        data.TimeFrom,
                        data.InventoryCountTicketId.GetValueOrDefault(0),
                        "Stocktake",
                        null,
                        null,
                        data.Items ?? Array.Empty<StocktakeVarianceItemRow>())
                };
            }

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
                        foreach (var row in rows)
                        {
                            col.Item().Border(1).Padding(6).Column(block =>
                            {
                                block.Spacing(4);
                                block.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(90);
                                        c.RelativeColumn();
                                        c.ConstantColumn(100);
                                        c.ConstantColumn(110);
                                    });
                                    table.Header(h =>
                                    {
                                        h.Cell().Text("Date").SemiBold();
                                        h.Cell().Text("Tên phiếu").SemiBold();
                                        h.Cell().Text("Người tạo").SemiBold();
                                        h.Cell().Text("Nhân viên phụ trách").SemiBold();
                                    });

                                    table.Cell().Text(row.Date.ToString("yyyy-MM-dd"));
                                    table.Cell().Text(row.TicketName ?? $"Stocktake #{row.TicketId}");
                                    table.Cell().Text(row.CreatedBy ?? "-");
                                    table.Cell().Text(row.AssignedStaff ?? "-");
                                });

                                block.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn();
                                        c.ConstantColumn(85);
                                        c.ConstantColumn(95);
                                        c.ConstantColumn(85);
                                    });
                                    table.Header(h =>
                                    {
                                        h.Cell().Text("Product name").SemiBold();
                                        h.Cell().AlignRight().Text("System quantity").SemiBold();
                                        h.Cell().AlignRight().Text("Counted quantity").SemiBold();
                                        h.Cell().AlignRight().Text("Discrepancy").SemiBold();
                                    });

                                    foreach (var item in row.Items)
                                    {
                                        table.Cell().Text($"{item.ProductName ?? "(unknown)"} ({item.Sku ?? "-"})");
                                        table.Cell().AlignRight().Text(item.SystemQty.ToString());
                                        table.Cell().AlignRight().Text(item.CountedQty.ToString());
                                        table.Cell().AlignRight().Text(item.VarianceQty.ToString());
                                    }
                                });
                            });
                        }
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static byte[] GenerateReplenishmentRecommendationPdf(Report report, ReplenishmentRecommendationReportData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Column(col =>
                    {
                        col.Item().Text($"Report: {report.ReportType} (#{report.Id})").SemiBold().FontSize(14);
                        col.Item().Text(
                            $"Warehouse: {(data.Meta.WarehouseId?.ToString() ?? "All")} | Horizon: {data.Meta.ForecastHorizonDays}d | Service Level: {data.Meta.ServiceLevel:0.##}");
                        col.Item().Text($"Range: {data.Meta.TimeFrom:yyyy-MM-dd} -> {data.Meta.TimeTo:yyyy-MM-dd}");
                    });

                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Text(
                            $"SKU analyzed: {data.Summary.TotalSkusAnalyzed} | SKU recommended: {data.Summary.TotalSkusRecommended} | Total recommended qty: {data.Summary.TotalRecommendedQty}");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.ConstantColumn(45);
                                c.ConstantColumn(55);
                                c.ConstantColumn(45);
                                c.ConstantColumn(45);
                                c.ConstantColumn(45);
                                c.ConstantColumn(55);
                                c.ConstantColumn(45);
                            });

                            table.Header(h =>
                            {
                                h.Cell().Text("Product");
                                h.Cell().AlignRight().Text("OnHand");
                                h.Cell().AlignRight().Text("Forecast");
                                h.Cell().AlignRight().Text("Safety");
                                h.Cell().AlignRight().Text("Reorder");
                                h.Cell().AlignRight().Text("Recom.");
                                h.Cell().Text("Risk");
                                h.Cell().AlignRight().Text("Conf.");
                            });

                            foreach (var item in data.Items.Take(60))
                            {
                                table.Cell().Text($"{item.ProductName ?? "(unknown)"} ({item.Sku ?? "-"})");
                                table.Cell().AlignRight().Text(item.OnHandQty.ToString());
                                table.Cell().AlignRight().Text(item.ForecastDemandQty.ToString());
                                table.Cell().AlignRight().Text(item.SafetyStock.ToString());
                                table.Cell().AlignRight().Text(item.ReorderPoint.ToString());
                                table.Cell().AlignRight().Text(item.RecommendedQty.ToString());
                                table.Cell().Text(item.RiskLevel);
                                table.Cell().AlignRight().Text(item.Confidence.ToString("0.##"));
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
                NormalizeReportTypeForResponse(r.ReportType),
                r.WarehouseId,
                r.Status,
                r.TimeFrom,
                r.TimeTo,
                r.CreatedAt,
                r.CompletedAt,
                r.ErrorMessage)).ToList();
        }

        private async Task ValidateCreatePayloadScopeAsync(int companyId, CreateReportRequest payload)
        {
            var reportType = NormalizeReportType(payload.ReportType);
            if (!SupportedReportTypes.Contains(reportType))
            {
                throw new ArgumentException($"Unsupported report type '{reportType}'.", nameof(payload.ReportType));
            }

            if (string.Equals(reportType, ReportTypes.ReplenishmentRecommendation, StringComparison.Ordinal))
            {
                var horizon = payload.ForecastHorizonDays.GetValueOrDefault(14);
                var leadTime = payload.DefaultLeadTimeDays.GetValueOrDefault(7);
                var serviceLevel = payload.ServiceLevel.GetValueOrDefault(0.95);
                if (horizon <= 0)
                    throw new ArgumentException("ForecastHorizonDays must be greater than 0.", nameof(payload.ForecastHorizonDays));
                if (leadTime <= 0)
                    throw new ArgumentException("DefaultLeadTimeDays must be greater than 0.", nameof(payload.DefaultLeadTimeDays));
                if (serviceLevel <= 0 || serviceLevel >= 1)
                    throw new ArgumentException("ServiceLevel must be between 0 and 1.", nameof(payload.ServiceLevel));
            }

            if (payload.WarehouseId.HasValue)
            {
                if (payload.WarehouseId.Value <= 0)
                    throw new ArgumentException("WarehouseId must be greater than 0 when provided.", nameof(payload.WarehouseId));

                var warehouseExists = await _repo
                    .WarehouseBelongsToCompanyAsync(companyId, payload.WarehouseId.Value)
                    .ConfigureAwait(false);
                if (!warehouseExists)
                    throw new InvalidOperationException("Warehouse does not exist or does not belong to this company.");
            }

            if (payload.ProductId.HasValue)
            {
                if (payload.ProductId.Value <= 0)
                    throw new ArgumentException("ProductId must be greater than 0 when provided.", nameof(payload.ProductId));

                var productExists = await _repo
                    .ProductBelongsToCompanyAsync(companyId, payload.ProductId.Value)
                    .ConfigureAwait(false);
                if (!productExists)
                    throw new InvalidOperationException("Product does not exist or does not belong to this company.");
            }

            if (payload.InventoryCountTicketId.HasValue)
            {
                if (payload.InventoryCountTicketId.Value <= 0)
                {
                    throw new ArgumentException(
                        "InventoryCountTicketId must be greater than 0 when provided.",
                        nameof(payload.InventoryCountTicketId));
                }

                var ticketExists = await _repo
                    .InventoryCountTicketBelongsToCompanyAsync(companyId, payload.InventoryCountTicketId.Value)
                    .ConfigureAwait(false);
                if (!ticketExists)
                {
                    throw new InvalidOperationException(
                        "Inventory count ticket does not exist or does not belong to this company.");
                }

                if (payload.WarehouseId.HasValue)
                {
                    var ticketMatchesWarehouse = await _repo
                        .InventoryCountTicketBelongsToWarehouseAsync(
                            payload.InventoryCountTicketId.Value,
                            payload.WarehouseId.Value)
                        .ConfigureAwait(false);
                    if (!ticketMatchesWarehouse)
                    {
                        throw new InvalidOperationException(
                            "Inventory count ticket does not belong to the selected warehouse.");
                    }
                }
            }
        }

        private static string NormalizeReportType(string? reportType)
        {
            var trimmed = reportType?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            if (SupportedReportTypes.Contains(trimmed))
                return trimmed;

            var key = trimmed.Replace("_", string.Empty).Replace("-", string.Empty);
            if (!ReportTypeAliases.TryGetValue(key, out var normalized))
                return trimmed;

            return normalized;
        }

        private static string? NormalizeReportTypeForResponse(string? reportType)
        {
            if (string.IsNullOrWhiteSpace(reportType))
                return reportType;

            return reportType;
        }

        private static DateTime NormalizeToUnspecified(DateTime value)
            => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    }
}
