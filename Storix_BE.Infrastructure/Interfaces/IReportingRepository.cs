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

        Task<InventorySnapshotReportData> GetInventorySnapshotAsync(int companyId, int? warehouseId, DateTime from, DateTime to);
        Task<InventoryLedgerReportData> GetInventoryLedgerAsync(int companyId, int? warehouseId, int? productId, DateTime from, DateTime to);
        Task<InventoryInOutBalanceReportData> GetInventoryInOutBalanceAsync(int companyId, int? warehouseId, DateTime from, DateTime to);
        Task<StocktakeVarianceReportData> GetStocktakeVarianceAsync(int companyId, int? warehouseId, int? inventoryCountTicketId, DateTime from, DateTime to);
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

    public sealed record InventorySnapshotRow(
        int ProductId,
        string? ProductName,
        string? Sku,
        int Quantity,
        decimal UnitCost,
        decimal InventoryValue);

    public sealed record InventorySnapshotReportData(
        DateTime TimeFrom,
        DateTime TimeTo,
        int? WarehouseId,
        int TotalSkus,
        int TotalQuantity,
        decimal TotalValue,
        IReadOnlyList<InventorySnapshotRow> Items);

    public sealed record InventoryLedgerRow(
        DateTime Day,
        int ProductId,
        string? ProductName,
        string? Sku,
        string? TransactionType,
        int QuantityIn,
        int QuantityOut,
        int RunningQuantity);

    public sealed record InventoryLedgerReportData(
        DateTime TimeFrom,
        DateTime TimeTo,
        int? WarehouseId,
        int? ProductId,
        int OpeningQuantity,
        int ClosingQuantity,
        IReadOnlyList<InventoryLedgerRow> Rows);

    public sealed record InventoryInOutBalanceDayPoint(DateTime Day, int InboundQty, int OutboundQty);

    public sealed record InventoryInOutBalanceProductRow(
        int ProductId,
        string? ProductName,
        string? Sku,
        int OpeningQty,
        int InboundQty,
        int OutboundQty,
        int ClosingQty,
        decimal UnitCost,
        decimal ClosingValue);

    public sealed record InventoryInOutBalanceReportData(
        DateTime TimeFrom,
        DateTime TimeTo,
        int? WarehouseId,
        int TotalOpeningQty,
        int TotalInboundQty,
        int TotalOutboundQty,
        int TotalClosingQty,
        decimal TotalClosingValue,
        IReadOnlyList<InventoryInOutBalanceDayPoint> ByDay,
        IReadOnlyList<InventoryInOutBalanceProductRow> ByProduct);

    public sealed record StocktakeVarianceRow(
        int ProductId,
        string? ProductName,
        string? Sku,
        int SystemQty,
        int CountedQty,
        int VarianceQty,
        decimal UnitCost,
        decimal VarianceValue);

    public sealed record StocktakeVarianceReportData(
        DateTime TimeFrom,
        DateTime TimeTo,
        int? WarehouseId,
        int? InventoryCountTicketId,
        int TotalItems,
        int TotalVarianceQty,
        decimal TotalVarianceValue,
        IReadOnlyList<StocktakeVarianceRow> Items);
}

