using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IReportingRepository
    {
        Task<Report> CreateReportAsync(Report report);
        Task UpdateReportAsync(Report report);

        Task<Report?> GetReportByIdAsync(int companyId, int reportId);

        Task<List<Report>> ListReportsAsync(
            int companyId,
            string? reportType,
            int? warehouseId,
            DateTime? from,
            DateTime? to,
            int skip,
            int take);

        Task<OutboundKpiBasicReportData> GetOutboundKpiBasicAsync(int companyId, int? warehouseId, DateTime from, DateTime to);
        Task<InventoryTrackingReportData> GetInventoryTrackingAsync(int companyId, int? warehouseId, DateTime from, DateTime to);
        Task<InboundKpiBasicReportData> GetInboundKpiBasicAsync(int companyId, int? warehouseId, DateTime from, DateTime to);
    }

    // ── OutboundKpiBasic ──────────────────────────────────────────────────────

    public sealed record OutboundKpiBasicPoint(DateTime Day, int Count, double? AvgLeadTimeHours);

    public sealed record OutboundKpiBasicStaffThroughput(int StaffId, string? StaffName, int CompletedCount, double? AvgLeadTimeHours);

    public sealed record OutboundKpiBasicReportData(
        DateTime TimeFrom,
        DateTime TimeTo,
        int? WarehouseId,
        int TotalCompleted,
        double? OverallAvgLeadTimeHours,
        IReadOnlyList<OutboundKpiBasicPoint> ByDay,
        IReadOnlyList<OutboundKpiBasicStaffThroughput> ByStaff);

    // ── InventoryTracking ─────────────────────────────────────────────────────

    public sealed record InventoryTrackingDayPoint(DateTime Day, int InboundCount, int OutboundCount, int InboundQty, int OutboundQty);

    public sealed record InventoryTrackingProductRow(int ProductId, string? ProductName, string? Sku, int InboundQty, int OutboundQty, int NetChange, int? CurrentStock);

    public sealed record InventoryTrackingReportData(
        DateTime TimeFrom,
        DateTime TimeTo,
        int? WarehouseId,
        int TotalInboundTransactions,
        int TotalOutboundTransactions,
        int TotalInboundQty,
        int TotalOutboundQty,
        IReadOnlyList<InventoryTrackingDayPoint> ByDay,
        IReadOnlyList<InventoryTrackingProductRow> TopProducts);

    // ── InboundKpiBasic ───────────────────────────────────────────────────────

    public sealed record InboundKpiBasicDayPoint(DateTime Day, int Count, int ReceivedQty);

    public sealed record InboundKpiBasicSupplierRow(int SupplierId, string? SupplierName, int CompletedCount, int ReceivedQty);

    public sealed record InboundKpiBasicReportData(
        DateTime TimeFrom,
        DateTime TimeTo,
        int? WarehouseId,
        int TotalCompleted,
        int TotalReceivedQty,
        IReadOnlyList<InboundKpiBasicDayPoint> ByDay,
        IReadOnlyList<InboundKpiBasicSupplierRow> BySupplier);
}

