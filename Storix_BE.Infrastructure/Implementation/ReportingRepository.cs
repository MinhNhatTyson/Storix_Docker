using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class ReportingRepository : IReportingRepository
    {
        private readonly StorixDbContext _context;

        public ReportingRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<Report> CreateReportAsync(Report report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            _context.Reports.Add(report);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return report;
        }

        public async Task UpdateReportAsync(Report report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            _context.Reports.Update(report);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task<Report?> GetReportByIdAsync(int companyId, int reportId)
        {
            return await _context.Reports
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId)
                .ConfigureAwait(false);
        }

        public async Task<List<Report>> ListReportsAsync(
            int companyId,
            string? reportType,
            int? warehouseId,
            DateTime? from,
            DateTime? to,
            int skip,
            int take)
        {
            var query = _context.Reports
                .AsNoTracking()
                .Where(r => r.CompanyId == companyId);

            if (!string.IsNullOrWhiteSpace(reportType))
            {
                query = query.Where(r => r.ReportType == reportType);
            }

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                query = query.Where(r => r.WarehouseId == warehouseId.Value);
            }

            if (from.HasValue)
            {
                query = query.Where(r => r.CreatedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(r => r.CreatedAt <= to.Value);
            }

            return await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip < 0 ? 0 : skip)
                .Take(take <= 0 ? 50 : take)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<OutboundKpiBasicReportData> GetOutboundKpiBasicAsync(int companyId, int? warehouseId, DateTime from, DateTime to)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (to < from) throw new ArgumentException("TimeTo must be >= TimeFrom.");

            var baseQuery = _context.OutboundOrders
                .AsNoTracking()
                .Where(o => o.Warehouse != null && o.Warehouse.CompanyId == companyId);

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                baseQuery = baseQuery.Where(o => o.WarehouseId == warehouseId.Value);
            }

            // Resolve CompletedAt by joining status history (preferred) then falling back to inventory transactions.
            // Using explicit joins avoids correlated subqueries per row.
            var statusCompletedAt = _context.OutboundOrderStatusHistories
                .Where(h => h.NewStatus == "Completed")
                .GroupBy(h => h.OutboundOrderId)
                .Select(g => new { OutboundOrderId = g.Key, CompletedAt = g.Max(h => (DateTime?)h.ChangedAt) });

            var txCompletedAt = _context.InventoryTransactions
                .Where(t => t.TransactionType == "Outbound")
                .GroupBy(t => t.ReferenceId)
                .Select(g => new { ReferenceId = g.Key, CompletedAt = g.Max(t => (DateTime?)t.CreatedAt) });

            var withCompletedAt = baseQuery
                .GroupJoin(statusCompletedAt,
                    o => o.Id,
                    s => s.OutboundOrderId,
                    (o, statuses) => new { Order = o, StatusCompleted = statuses.FirstOrDefault() })
                .GroupJoin(txCompletedAt,
                    x => x.Order.Id,
                    t => t.ReferenceId,
                    (x, txs) => new { x.Order, x.StatusCompleted, TxCompleted = txs.FirstOrDefault() })
                .Select(x => new
                {
                    x.Order.Id,
                    x.Order.WarehouseId,
                    x.Order.StaffId,
                    StaffName = x.Order.Staff != null ? x.Order.Staff.FullName : null,
                    x.Order.CreatedAt,
                    CompletedAt = x.StatusCompleted != null
                        ? x.StatusCompleted.CompletedAt
                        : (x.TxCompleted != null ? x.TxCompleted.CompletedAt : (DateTime?)null)
                })
                .Where(x =>
                    x.CompletedAt.HasValue &&
                    x.CompletedAt.Value >= from &&
                    x.CompletedAt.Value <= to);

            var rows = await withCompletedAt.ToListAsync().ConfigureAwait(false);

            static double? AvgHours(IEnumerable<(DateTime CreatedAt, DateTime CompletedAt)> items)
            {
                var list = items.ToList();
                if (!list.Any()) return null;
                var avg = list.Average(x => (x.CompletedAt - x.CreatedAt).TotalHours);
                return double.IsFinite(avg) ? avg : null;
            }

            var completedRows = rows
                .Where(r => r.CreatedAt.HasValue && r.CompletedAt.HasValue)
                .Select(r => new
                {
                    r.Id,
                    r.StaffId,
                    r.StaffName,
                    CreatedAt = r.CreatedAt!.Value,
                    CompletedAt = r.CompletedAt!.Value
                })
                .ToList();


            var byDay = completedRows
                .GroupBy(r => r.CompletedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var avg = AvgHours(g.Select(x => (x.CreatedAt, x.CompletedAt)));
                    return new OutboundKpiBasicPoint(g.Key, g.Count(), avg);
                })
                .ToList();

            var byStaff = completedRows
                .Where(r => r.StaffId.HasValue && r.StaffId.Value > 0)
                .GroupBy(r => new { StaffId = r.StaffId!.Value, r.StaffName })
                .Select(g =>
                {
                    var avg = AvgHours(g.Select(x => (x.CreatedAt, x.CompletedAt)));
                    return new OutboundKpiBasicStaffThroughput(g.Key.StaffId, g.Key.StaffName, g.Count(), avg);
                })
                .OrderByDescending(x => x.CompletedCount)
                .ThenBy(x => x.StaffId)
                .ToList();

            var overallAvg = AvgHours(completedRows.Select(x => (x.CreatedAt, x.CompletedAt)));

            return new OutboundKpiBasicReportData(
                from,
                to,
                warehouseId,
                completedRows.Count,
                overallAvg,
                byDay,
                byStaff);
        }

        public async Task<InventoryTrackingReportData> GetInventoryTrackingAsync(int companyId, int? warehouseId, DateTime from, DateTime to)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (to < from) throw new ArgumentException("TimeTo must be >= TimeFrom.");

            // Transactions scoped to company via Warehouse → Company
            var txQuery = _context.InventoryTransactions
                .AsNoTracking()
                .Where(t =>
                    t.Warehouse != null &&
                    t.Warehouse.CompanyId == companyId &&
                    t.CreatedAt.HasValue &&
                    t.CreatedAt.Value >= from &&
                    t.CreatedAt.Value <= to);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                txQuery = txQuery.Where(t => t.WarehouseId == warehouseId.Value);

            var txRows = await txQuery
                .Select(t => new
                {
                    t.ProductId,
                    ProductName = t.Product != null ? t.Product.Name : null,
                    Sku = t.Product != null ? t.Product.Sku : null,
                    t.TransactionType,
                    Qty = t.QuantityChange ?? 0,
                    Day = t.CreatedAt!.Value.Date
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var inboundRows = txRows.Where(t => t.TransactionType == "Inbound").ToList();
            var outboundRows = txRows.Where(t => t.TransactionType == "Outbound").ToList();

            var totalInboundTx = inboundRows.Count;
            var totalOutboundTx = outboundRows.Count;
            var totalInboundQty = inboundRows.Sum(t => t.Qty);
            var totalOutboundQty = Math.Abs(outboundRows.Sum(t => t.Qty));

            var allDays = txRows.Select(t => t.Day).Distinct().OrderBy(d => d).ToList();
            var byDay = allDays.Select(day =>
            {
                var dayIn = txRows.Where(t => t.Day == day && t.TransactionType == "Inbound").ToList();
                var dayOut = txRows.Where(t => t.Day == day && t.TransactionType == "Outbound").ToList();
                return new InventoryTrackingDayPoint(
                    day,
                    dayIn.Count,
                    dayOut.Count,
                    dayIn.Sum(t => t.Qty),
                    Math.Abs(dayOut.Sum(t => t.Qty)));
            }).ToList();

            // Current stock snapshot for products that appeared in transactions
            var productIds = txRows.Where(t => t.ProductId.HasValue).Select(t => t.ProductId!.Value).Distinct().ToList();

            var stockQuery = _context.Inventories
                .AsNoTracking()
                .Where(i => i.ProductId.HasValue && productIds.Contains(i.ProductId.Value));

            if (warehouseId.HasValue && warehouseId.Value > 0)
                stockQuery = stockQuery.Where(i => i.WarehouseId == warehouseId.Value);
            else
                stockQuery = stockQuery.Where(i => i.Warehouse != null && i.Warehouse.CompanyId == companyId);

            var stockByProduct = await stockQuery
                .GroupBy(i => i.ProductId!.Value)
                .Select(g => new { ProductId = g.Key, CurrentStock = g.Sum(i => i.Quantity ?? 0) })
                .ToListAsync()
                .ConfigureAwait(false);

            var stockLookup = stockByProduct.ToDictionary(s => s.ProductId, s => s.CurrentStock);

            var topProducts = txRows
                .Where(t => t.ProductId.HasValue)
                .GroupBy(t => new { t.ProductId, t.ProductName, t.Sku })
                .Select(g =>
                {
                    var inQty = g.Where(t => t.TransactionType == "Inbound").Sum(t => t.Qty);
                    var outQty = Math.Abs(g.Where(t => t.TransactionType == "Outbound").Sum(t => t.Qty));
                    stockLookup.TryGetValue(g.Key.ProductId!.Value, out var currentStock);
                    return new InventoryTrackingProductRow(
                        g.Key.ProductId!.Value,
                        g.Key.ProductName,
                        g.Key.Sku,
                        inQty,
                        outQty,
                        inQty - outQty,
                        currentStock);
                })
                .OrderByDescending(r => r.InboundQty + r.OutboundQty)
                .Take(20)
                .ToList();

            return new InventoryTrackingReportData(
                from, to, warehouseId,
                totalInboundTx, totalOutboundTx,
                totalInboundQty, totalOutboundQty,
                byDay, topProducts);
        }

        public async Task<InboundKpiBasicReportData> GetInboundKpiBasicAsync(int companyId, int? warehouseId, DateTime from, DateTime to)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (to < from) throw new ArgumentException("TimeTo must be >= TimeFrom.");

            var baseQuery = _context.InboundOrders
                .AsNoTracking()
                .Where(o =>
                    o.Warehouse != null &&
                    o.Warehouse.CompanyId == companyId &&
                    o.Status == "Completed" &&
                    o.CreatedAt.HasValue &&
                    o.CreatedAt.Value >= from &&
                    o.CreatedAt.Value <= to);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                baseQuery = baseQuery.Where(o => o.WarehouseId == warehouseId.Value);

            var rows = await baseQuery
                .Select(o => new
                {
                    o.Id,
                    o.SupplierId,
                    SupplierName = o.Supplier != null ? o.Supplier.Name : null,
                    CompletedAt = o.CreatedAt!.Value,
                    ReceivedQty = o.InboundOrderItems
                        .Sum(i => i.ReceivedQuantity ?? 0)
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var totalCompleted = rows.Count;
            var totalReceivedQty = rows.Sum(r => r.ReceivedQty);

            var byDay = rows
                .GroupBy(r => r.CompletedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new InboundKpiBasicDayPoint(g.Key, g.Count(), g.Sum(r => r.ReceivedQty)))
                .ToList();

            var bySupplier = rows
                .Where(r => r.SupplierId.HasValue)
                .GroupBy(r => new { SupplierId = r.SupplierId!.Value, r.SupplierName })
                .Select(g => new InboundKpiBasicSupplierRow(g.Key.SupplierId, g.Key.SupplierName, g.Count(), g.Sum(r => r.ReceivedQty)))
                .OrderByDescending(s => s.CompletedCount)
                .ThenBy(s => s.SupplierId)
                .ToList();

            return new InboundKpiBasicReportData(
                from, to, warehouseId,
                totalCompleted, totalReceivedQty,
                byDay, bySupplier);
        }
    }
}

