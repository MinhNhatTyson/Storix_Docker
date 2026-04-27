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
        public const string StocktakeVariance = "StocktakeVariance";
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
        public const string StocktakeVariance = "1";
    }

    public sealed record CreateReportRequest(
        string ReportType,
        int? WarehouseId,
        int? ProductId,
        int? InventoryCountTicketId,
        DateTime TimeFrom,
        DateTime TimeTo);

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

