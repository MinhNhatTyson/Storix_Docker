using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IReportingService
    {
        Task<ReportDetailDto> CreateReportAsync(int companyId, int createdByUserId, CreateReportRequest payload);
        Task<ReportDetailDto?> GetReportAsync(int companyId, int reportId);
        Task<ReportDetailDto> UpdateAiRecommendationAsync(int companyId, int reportId, IReadOnlyList<AiRecommendationItemDto> recommendations);
        Task<ReportPdfArtifactDto> ExportReportPdfAsync(int companyId, int reportId);

        Task<List<ReportRequestListItemDto>> ListReportsAsync(
            int companyId,
            string? reportType,
            int? warehouseId,
            DateTime? from,
            DateTime? to,
            int skip,
            int take);
    }

    public static class ReportTypes
    {
        public const string InventorySnapshot = "InventorySnapshot";
        public const string InventoryLedger = "InventoryLedger";
        public const string InventoryInOutBalance = "InventoryInOutBalance";
        public const string InventoryTracking = "InventoryTracking";
        public const string ReplenishmentRecommendation = "ReplenishmentRecommendation";
    }

    public static class ReportStatus
    {
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
    }

    public static class ReportSchemaVersions
    {
        public const string InventorySnapshot = "1";
        public const string InventoryLedger = "1";
        public const string InventoryInOutBalance = "1";
        public const string InventoryTracking = "1";
        public const string ReplenishmentRecommendation = "1";
    }

    public sealed record CreateReportRequest(
        string ReportType,
        int? WarehouseId,
        int? ProductId,
        int? InventoryCountTicketId,
        DateTime TimeFrom,
        DateTime TimeTo,
        int? ForecastHorizonDays = null,
        int? DefaultLeadTimeDays = null,
        double? ServiceLevel = null,
        bool? UseAiExplanation = null);

    public sealed record ReportRequestListItemDto(
        int Id,
        string? ReportType,
        int? WarehouseId,
        string? Status,
        DateTime? TimeFrom,
        DateTime? TimeTo,
        DateTime? CreatedAt,
        DateTime? CompletedAt,
        string? ErrorMessage);

    public sealed record ReportResultDto(
        JsonElement? Summary,
        JsonElement? Data,
        string? SchemaVersion);

    public sealed record ReportPdfArtifactDto(
        string? Url,
        string? FileName,
        string? ContentHash,
        DateTime? GeneratedAt);

    public sealed record AiRecommendationItemDto(
        int ProductId,
        int ForecastedQuantity,
        string Reason);

    public sealed record AiRecommendationResponseDto(
        int ProductId,
        string? ProductName,
        int ForecastedQuantity,
        string Reason);

    public sealed record ReportDetailDto(
        int Id,
        string? ReportType,
        int CompanyId,
        int? WarehouseId,
        string? Status,
        DateTime? TimeFrom,
        DateTime? TimeTo,
        DateTime? CreatedAt,
        DateTime? CompletedAt,
        string? ErrorMessage,
        ReportResultDto? Result,
        ReportPdfArtifactDto? Pdf);
}
