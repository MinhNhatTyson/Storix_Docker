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

        public Task<bool> WarehouseBelongsToCompanyAsync(int companyId, int warehouseId)
        {
            return _context.Warehouses
                .AsNoTracking()
                .AnyAsync(w => w.Id == warehouseId && w.CompanyId == companyId);
        }

        public Task<bool> ProductBelongsToCompanyAsync(int companyId, int productId)
        {
            return _context.Products
                .AsNoTracking()
                .AnyAsync(p => p.Id == productId && p.CompanyId == companyId);
        }

        public Task<bool> InventoryCountTicketBelongsToCompanyAsync(int companyId, int inventoryCountTicketId)
        {
            return _context.InventoryCountsTickets
                .AsNoTracking()
                .AnyAsync(t =>
                    t.Id == inventoryCountTicketId &&
                    t.Warehouse != null &&
                    t.Warehouse.CompanyId == companyId);
        }

        public Task<bool> InventoryCountTicketBelongsToWarehouseAsync(int inventoryCountTicketId, int warehouseId)
        {
            return _context.InventoryCountsTickets
                .AsNoTracking()
                .AnyAsync(t => t.Id == inventoryCountTicketId && t.WarehouseId == warehouseId);
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

            var orders = await baseQuery
                .Select(o => new
                {
                    o.Id,
                    o.WarehouseId,
                    o.StaffId,
                    StaffName = o.Staff != null ? o.Staff.FullName : null,
                    o.CreatedAt
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var orderIds = orders.Select(o => o.Id).ToList();

            var statusMap = await statusCompletedAt
                .Where(x => orderIds.Contains(x.OutboundOrderId))
                .ToDictionaryAsync(x => x.OutboundOrderId, x => x.CompletedAt)
                .ConfigureAwait(false);

            var txMap = await txCompletedAt
                .Where(x => x.ReferenceId.HasValue && orderIds.Contains(x.ReferenceId.Value))
                .ToDictionaryAsync(x => x.ReferenceId!.Value, x => x.CompletedAt)
                .ConfigureAwait(false);

            var rows = orders
                .Select(o =>
                {
                    statusMap.TryGetValue(o.Id, out var statusCompleted);
                    txMap.TryGetValue(o.Id, out var txCompleted);
                    var completedAt = statusCompleted ?? txCompleted;

                    return new
                    {
                        o.Id,
                        o.WarehouseId,
                        o.StaffId,
                        o.StaffName,
                        o.CreatedAt,
                        CompletedAt = completedAt
                    };
                })
                .Where(x =>
                    x.CompletedAt.HasValue &&
                    x.CompletedAt.Value >= from &&
                    x.CompletedAt.Value <= to)
                .ToList();

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

            var batchSnapshot = await GetRemainingBatchSnapshotAsync(companyId, warehouseId, to, productIds)
                .ConfigureAwait(false);

            var stockLookup = DistinctBatchEntries(batchSnapshot)
                .GroupBy(b => b.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.RemainingQuantity));

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

            var batchRows = await _context.InventoryBatches
                .AsNoTracking()
                .Where(b =>
                    b.InboundOrder.Warehouse != null &&
                    b.InboundOrder.Warehouse.CompanyId == companyId &&
                    b.InboundOrder.Status == "Completed" &&
                    b.InboundOrder.CreatedAt.HasValue &&
                    b.InboundOrder.CreatedAt.Value >= from &&
                    b.InboundOrder.CreatedAt.Value <= to)
                .Where(b => !warehouseId.HasValue || warehouseId.Value <= 0 || b.WarehouseId == warehouseId.Value)
                .Select(b => new
                {
                    b.InboundOrderId,
                    b.ProductId,
                    b.ReceivedQuantity
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var receivedByOrderId = batchRows
                .GroupBy(x => x.InboundOrderId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.ReceivedQuantity));

            var rows = await baseQuery
                .Select(o => new
                {
                    o.Id,
                    o.SupplierId,
                    SupplierName = o.Supplier != null ? o.Supplier.Name : null,
                    CompletedAt = o.CreatedAt!.Value
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var materializedRows = rows
                .Select(o => new
                {
                    o.Id,
                    o.SupplierId,
                    o.SupplierName,
                    o.CompletedAt,
                    ReceivedQty = receivedByOrderId.TryGetValue(o.Id, out var qty) ? qty : 0
                })
                .ToList();

            var totalCompleted = materializedRows.Count;
            var totalReceivedQty = materializedRows.Sum(r => r.ReceivedQty);

            var byDay = materializedRows
                .GroupBy(r => r.CompletedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new InboundKpiBasicDayPoint(g.Key, g.Count(), g.Sum(r => r.ReceivedQty)))
                .ToList();

            var bySupplier = materializedRows
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

        public async Task<InventorySnapshotReportData> GetInventorySnapshotAsync(int companyId, int? warehouseId, DateTime from, DateTime to)
        {
            var batchSnapshot = await GetRemainingBatchSnapshotAsync(companyId, warehouseId, to)
                .ConfigureAwait(false);

            if (batchSnapshot.Count > 0)
            {
                var unitCostByProduct = BuildWeightedUnitCostLookup(batchSnapshot);

                var items = DistinctBatchEntries(batchSnapshot)
                    .GroupBy(r => new { r.ProductId, r.ProductName, r.Sku })
                    .Select(g =>
                    {
                        var qty = g.Sum(x => x.RemainingQuantity);
                        unitCostByProduct.TryGetValue(g.Key.ProductId, out var unitCost);
                        return new InventorySnapshotRow(g.Key.ProductId, g.Key.ProductName, g.Key.Sku, qty, unitCost, unitCost * qty);
                    })
                    .OrderByDescending(x => x.InventoryValue)
                    .ToList();

                return new InventorySnapshotReportData(
                    from,
                    to,
                    warehouseId,
                    items.Count,
                    items.Sum(x => x.Quantity),
                    items.Sum(x => x.InventoryValue),
                    items,
                    BuildBatchBreakdown(batchSnapshot));
            }

            var inventoryFallbackQuery = _context.Inventories
                .AsNoTracking()
                .Where(i =>
                    i.Warehouse != null &&
                    i.Warehouse.CompanyId == companyId);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                inventoryFallbackQuery = inventoryFallbackQuery.Where(i => i.WarehouseId == warehouseId.Value);

            var inventoryFallbackRows = await inventoryFallbackQuery
                .Select(i => new
                {
                    i.ProductId,
                    ProductName = i.Product != null ? i.Product.Name : null,
                    Sku = i.Product != null ? i.Product.Sku : null,
                    Qty = (i.Quantity ?? 0) - (i.ReservedQuantity ?? 0),
                    UnitCost = i.Product != null ? (decimal?)i.Product.ProductPrices.OrderByDescending(p => p.Date).Select(p => p.Price).FirstOrDefault() : null
                })
                .Where(x => x.ProductId.HasValue && x.ProductId.Value > 0 && x.Qty > 0)
                .ToListAsync()
                .ConfigureAwait(false);

            var fallbackItems = inventoryFallbackRows
                .GroupBy(x => new { ProductId = x.ProductId!.Value, x.ProductName, x.Sku })
                .Select(g =>
                {
                    var qty = g.Sum(x => x.Qty);
                    var unitCost = g.Select(x => x.UnitCost ?? 0m).FirstOrDefault();
                    return new InventorySnapshotRow(g.Key.ProductId, g.Key.ProductName, g.Key.Sku, qty, unitCost, unitCost * qty);
                })
                .OrderByDescending(x => x.InventoryValue)
                .ToList();

            return new InventorySnapshotReportData(
                from,
                to,
                warehouseId,
                fallbackItems.Count,
                fallbackItems.Sum(x => x.Quantity),
                fallbackItems.Sum(x => x.InventoryValue),
                fallbackItems,
                Array.Empty<InventoryBatchBreakdownRow>());
        }

        public async Task<InventoryLedgerReportData> GetInventoryLedgerAsync(int companyId, int? warehouseId, int? productId, DateTime from, DateTime to)
        {
            var txQuery = _context.InventoryTransactions.AsNoTracking().Where(t => t.Warehouse != null && t.Warehouse.CompanyId == companyId && t.CreatedAt.HasValue);
            if (warehouseId.HasValue && warehouseId.Value > 0) txQuery = txQuery.Where(t => t.WarehouseId == warehouseId.Value);
            if (productId.HasValue && productId.Value > 0) txQuery = txQuery.Where(t => t.ProductId == productId.Value);

            var opening = await txQuery.Where(t => t.CreatedAt!.Value < from).SumAsync(t => t.QuantityChange ?? 0).ConfigureAwait(false);

            var rows = await txQuery.Where(t => t.CreatedAt!.Value >= from && t.CreatedAt!.Value <= to)
                .OrderBy(t => t.CreatedAt)
                .Select(t => new
                {
                    t.ReferenceId,
                    Day = t.CreatedAt!.Value,
                    ProductId = t.ProductId ?? 0,
                    ProductName = t.Product != null ? t.Product.Name : null,
                    Sku = t.Product != null ? t.Product.Sku : null,
                    t.TransactionType,
                    Qty = t.QuantityChange ?? 0
                }).ToListAsync().ConfigureAwait(false);

            var inboundReferenceIds = rows
                .Where(r => r.TransactionType == "Inbound" && r.ReferenceId.HasValue)
                .Select(r => r.ReferenceId!.Value)
                .Distinct()
                .ToList();

            var inboundBatchLookup = await _context.InventoryBatches
                .AsNoTracking()
                .Where(b => inboundReferenceIds.Contains(b.InboundOrderId))
                .GroupBy(b => new { b.InboundOrderId, b.ProductId })
                .Select(g => g.OrderBy(x => x.InboundDate).ThenBy(x => x.Id).First())
                .ToListAsync()
                .ConfigureAwait(false);

            var batchByInboundOrderAndProduct = inboundBatchLookup.ToDictionary(
                x => (x.InboundOrderId, x.ProductId),
                x => x);

            var running = opening;
            var ledger = new List<InventoryLedgerRow>();
            foreach (var r in rows)
            {
                running += r.Qty;
                InventoryBatch? batch = null;
                if (r.TransactionType == "Inbound" && r.ReferenceId.HasValue && r.ProductId > 0)
                {
                    batchByInboundOrderAndProduct.TryGetValue((r.ReferenceId.Value, r.ProductId), out batch);
                }

                ledger.Add(new InventoryLedgerRow(
                    r.Day,
                    r.ProductId,
                    r.ProductName,
                    r.Sku,
                    r.TransactionType,
                    r.Qty > 0 ? r.Qty : 0,
                    r.Qty < 0 ? Math.Abs(r.Qty) : 0,
                    running,
                    batch?.Id,
                    batch?.InboundDate,
                    batch?.EffectiveUnitCost));
            }

            return new InventoryLedgerReportData(from, to, warehouseId, productId, opening, running, ledger);
        }

        public async Task<InventoryInOutBalanceReportData> GetInventoryInOutBalanceAsync(int companyId, int? warehouseId, DateTime from, DateTime to)
        {
            var closingSnapshot = await GetRemainingBatchSnapshotAsync(companyId, warehouseId, to)
                .ConfigureAwait(false);

            var closingByProduct = DistinctBatchEntries(closingSnapshot)
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.RemainingQuantity));

            var unitCostByProduct = BuildWeightedUnitCostLookup(closingSnapshot);

            var inboundRows = await _context.InventoryBatches
                .AsNoTracking()
                .Where(b =>
                    b.InboundOrder.Warehouse != null &&
                    b.InboundOrder.Warehouse.CompanyId == companyId &&
                    b.InboundOrder.Status == "Completed" &&
                    b.InboundDate >= from &&
                    b.InboundDate <= to)
                .Where(b => !warehouseId.HasValue || warehouseId.Value <= 0 || b.WarehouseId == warehouseId.Value)
                .Select(b => new
                {
                    b.ProductId,
                    ProductName = b.Product != null ? b.Product.Name : null,
                    Sku = b.Product != null ? b.Product.Sku : null,
                    Qty = b.ReceivedQuantity,
                    Day = b.InboundDate.Date
                })
                .Where(x => x.ProductId > 0 && x.Qty > 0)
                .ToListAsync()
                .ConfigureAwait(false);

            // Outbound: use completedAt from status history, fallback to transaction createdAt.
            var outboundBase = _context.OutboundOrders.AsNoTracking()
                .Where(o => o.Warehouse != null && o.Warehouse.CompanyId == companyId);
            if (warehouseId.HasValue && warehouseId.Value > 0) outboundBase = outboundBase.Where(o => o.WarehouseId == warehouseId.Value);

            var statusCompletedAt = _context.OutboundOrderStatusHistories
                .Where(h => h.NewStatus == "Completed")
                .GroupBy(h => h.OutboundOrderId)
                .Select(g => new { OutboundOrderId = g.Key, CompletedAt = g.Max(h => (DateTime?)h.ChangedAt) });

            var outboundTxQuery = _context.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.Warehouse != null && t.Warehouse.CompanyId == companyId && t.CreatedAt.HasValue);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                outboundTxQuery = outboundTxQuery.Where(t => t.WarehouseId == warehouseId.Value);

            var txCompletedAt = outboundTxQuery
                .Where(t => t.TransactionType == "Outbound")
                .GroupBy(t => t.ReferenceId)
                .Select(g => new { ReferenceId = g.Key, CompletedAt = g.Max(t => (DateTime?)t.CreatedAt) });

            var outboundOrders = await outboundBase
                .Select(o => new { o.Id })
                .ToListAsync()
                .ConfigureAwait(false);

            var outboundOrderIdsForScope = outboundOrders.Select(x => x.Id).ToList();

            var statusCompletedMap = await statusCompletedAt
                .Where(x => outboundOrderIdsForScope.Contains(x.OutboundOrderId))
                .ToDictionaryAsync(x => x.OutboundOrderId, x => x.CompletedAt)
                .ConfigureAwait(false);

            var txCompletedMap = await txCompletedAt
                .Where(x => x.ReferenceId.HasValue && outboundOrderIdsForScope.Contains(x.ReferenceId.Value))
                .ToDictionaryAsync(x => x.ReferenceId!.Value, x => x.CompletedAt)
                .ConfigureAwait(false);

            var outboundCompletedInRange = outboundOrders
                .Select(o =>
                {
                    statusCompletedMap.TryGetValue(o.Id, out var statusCompleted);
                    txCompletedMap.TryGetValue(o.Id, out var txCompleted);
                    var completedAt = statusCompleted ?? txCompleted;

                    return new
                    {
                        o.Id,
                        CompletedAt = completedAt
                    };
                })
                .Where(x => x.CompletedAt.HasValue && x.CompletedAt.Value >= from && x.CompletedAt.Value <= to)
                .Select(x => new
                {
                    x.Id,
                    Day = x.CompletedAt!.Value.Date
                })
                .ToList();

            var outboundOrderIds = outboundCompletedInRange.Select(x => x.Id).Distinct().ToList();
            var outboundDayByOrder = outboundCompletedInRange.ToDictionary(x => x.Id, x => x.Day);

            var outboundRows = await _context.OutboundOrderItems.AsNoTracking()
                .Where(i => i.OutboundOrderId.HasValue && outboundOrderIds.Contains(i.OutboundOrderId.Value) && i.ProductId.HasValue)
                .Select(i => new
                {
                    ProductId = i.ProductId!.Value,
                    ProductName = i.Product != null ? i.Product.Name : null,
                    Sku = i.Product != null ? i.Product.Sku : null,
                    Qty = i.ReceivedQuantity ?? i.ExpectedQuantity ?? i.Quantity ?? 0,
                    OutboundOrderId = i.OutboundOrderId!.Value
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var outboundMaterialized = outboundRows
                .Where(x => x.Qty > 0 && outboundDayByOrder.ContainsKey(x.OutboundOrderId))
                .Select(x => new
                {
                    x.ProductId,
                    x.ProductName,
                    x.Sku,
                    Qty = x.Qty,
                    Day = outboundDayByOrder[x.OutboundOrderId]
                })
                .ToList();

            var byDay = inboundRows.Select(x => new { x.Day, In = x.Qty, Out = 0 })
                .Concat(outboundMaterialized.Select(x => new { x.Day, In = 0, Out = x.Qty }))
                .GroupBy(x => x.Day)
                .OrderBy(g => g.Key)
                .Select(g => new InventoryInOutBalanceDayPoint(g.Key, g.Sum(x => x.In), g.Sum(x => x.Out)))
                .ToList();

            var allProducts = inboundRows.Select(x => new { x.ProductId, x.ProductName, x.Sku })
                .Concat(outboundMaterialized.Select(x => new { x.ProductId, x.ProductName, x.Sku }))
                .GroupBy(x => x.ProductId)
                .Select(g => g.First())
                .ToList();

            var productIds = allProducts.Select(p => p.ProductId)
                .Concat(closingByProduct.Keys)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var byProduct = allProducts.Select(p =>
            {
                closingByProduct.TryGetValue(p.ProductId, out var closing);
                var inQty = inboundRows.Where(x => x.ProductId == p.ProductId).Sum(x => x.Qty);
                var outQty = outboundMaterialized.Where(x => x.ProductId == p.ProductId).Sum(x => x.Qty);
                var opening = closing - inQty + outQty;
                unitCostByProduct.TryGetValue(p.ProductId, out var unitCost);
                var closingValue = closing > 0 ? closing * unitCost : 0m;
                return new InventoryInOutBalanceProductRow(p.ProductId, p.ProductName, p.Sku, opening, inQty, outQty, closing, unitCost, closingValue);
            })
            .OrderByDescending(x => x.ClosingValue)
            .ToList();

            return new InventoryInOutBalanceReportData(
                from,
                to,
                warehouseId,
                byProduct.Sum(x => x.OpeningQty),
                byProduct.Sum(x => x.InboundQty),
                byProduct.Sum(x => x.OutboundQty),
                byProduct.Sum(x => x.ClosingQty),
                byProduct.Sum(x => x.ClosingValue),
                byDay,
                byProduct,
                BuildBatchBreakdown(closingSnapshot.Where(x => productIds.Contains(x.ProductId))));
        }

        public async Task<StocktakeVarianceReportData> GetStocktakeVarianceAsync(int companyId, int? warehouseId, int? inventoryCountTicketId, DateTime from, DateTime to)
        {
            var ticketQuery = _context.InventoryCountsTickets.AsNoTracking().Where(t => t.Warehouse != null && t.Warehouse.CompanyId == companyId);
            if (warehouseId.HasValue && warehouseId.Value > 0) ticketQuery = ticketQuery.Where(t => t.WarehouseId == warehouseId.Value);
            if (inventoryCountTicketId.HasValue && inventoryCountTicketId.Value > 0) ticketQuery = ticketQuery.Where(t => t.Id == inventoryCountTicketId.Value);
            ticketQuery = ticketQuery.Where(t => t.CreatedAt.HasValue && t.CreatedAt.Value >= from && t.CreatedAt.Value <= to);

            var ticketIds = await ticketQuery.Select(t => t.Id).ToListAsync().ConfigureAwait(false);
            var rawItemRows = await _context.InventoryCountItems.AsNoTracking().Where(i => i.InventoryCountId.HasValue && ticketIds.Contains(i.InventoryCountId.Value))
                .Select(i => new
                {
                    ProductId = i.ProductId ?? 0,
                    ProductName = i.Product != null ? i.Product.Name : null,
                    Sku = i.Product != null ? i.Product.Sku : null,
                    i.LocationId,
                    SystemQty = i.SystemQuantity ?? 0,
                    CountedQty = i.FinalQuantity ?? i.CountedQuantity ?? 0,
                    Variance = i.Discrepancy ?? ((i.FinalQuantity ?? i.CountedQuantity ?? 0) - (i.SystemQuantity ?? 0))
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var itemRows = rawItemRows
                .Where(x => x.ProductId > 0)
                .Select(x => new StocktakeItemContext(
                    x.ProductId,
                    x.ProductName,
                    x.Sku,
                    x.LocationId,
                    x.SystemQty,
                    x.CountedQty,
                    x.Variance))
                .ToList();

            var stocktakeCostContext = await ResolveStocktakeCostContextAsync(
                companyId,
                warehouseId,
                itemRows.Select(x => x.ProductId).Distinct().ToList(),
                itemRows.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().ToList(),
                to).ConfigureAwait(false);

            var locationIds = itemRows.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().ToList();
            var locationShelfMap = await _context.InventoryLocations
                .AsNoTracking()
                .Where(l => locationIds.Contains(l.Id) && l.ShelfId.HasValue)
                .Select(l => new { l.Id, ShelfId = l.ShelfId!.Value })
                .ToDictionaryAsync(x => x.Id, x => x.ShelfId)
                .ConfigureAwait(false);

            var items = itemRows.Select(x =>
            {
                var cost = stocktakeCostContext.ResolveUnitCost(x.ProductId, x.LocationId);
                return new StocktakeVarianceRow(x.ProductId, x.ProductName, x.Sku, x.SystemQty, x.CountedQty, x.Variance, cost, cost * x.Variance);
            }).ToList();

            return new StocktakeVarianceReportData(
                from,
                to,
                warehouseId,
                inventoryCountTicketId,
                items.Count,
                items.Sum(x => x.VarianceQty),
                items.Sum(x => x.VarianceValue),
                items,
                BuildStocktakeBatchBreakdown(itemRows, stocktakeCostContext.BatchEntries, locationShelfMap));
        }

        public async Task<ReplenishmentRecommendationReportData> GetReplenishmentRecommendationDataAsync(
            int companyId,
            int? warehouseId,
            DateTime from,
            DateTime to,
            int forecastHorizonDays,
            int defaultLeadTimeDays,
            double serviceLevel,
            bool useAiExplanation)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (to < from) throw new ArgumentException("TimeTo must be >= TimeFrom.");
            if (forecastHorizonDays <= 0) throw new ArgumentException("ForecastHorizonDays must be greater than 0.");
            if (defaultLeadTimeDays <= 0) throw new ArgumentException("DefaultLeadTimeDays must be greater than 0.");
            if (serviceLevel <= 0 || serviceLevel >= 1) throw new ArgumentException("ServiceLevel must be between 0 and 1.");

            var onHandRowsQuery = _context.InventoryBatches
                .AsNoTracking()
                .Where(b =>
                    b.Warehouse != null &&
                    b.Warehouse.CompanyId == companyId &&
                    b.RemainingQuantity > 0);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                onHandRowsQuery = onHandRowsQuery.Where(b => b.WarehouseId == warehouseId.Value);

            var onHandRows = await onHandRowsQuery
                .Select(b => new
                {
                    b.ProductId,
                    ProductName = b.Product != null ? b.Product.Name : null,
                    Sku = b.Product != null ? b.Product.Sku : null,
                    Qty = b.RemainingQuantity
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var onHandByProduct = onHandRows
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            var inboundPlannedQuery = _context.InboundOrderItems
                .AsNoTracking()
                .Where(i =>
                    i.InboundOrder != null &&
                    i.InboundOrder.Warehouse != null &&
                    i.InboundOrder.Warehouse.CompanyId == companyId &&
                    i.ProductId.HasValue &&
                    i.InboundOrder.Status != "Completed");
            if (warehouseId.HasValue && warehouseId.Value > 0)
                inboundPlannedQuery = inboundPlannedQuery.Where(i => i.InboundOrder.WarehouseId == warehouseId.Value);

            var inboundPlannedRows = await inboundPlannedQuery
                .Select(i => new
                {
                    ProductId = i.ProductId!.Value,
                    Qty = i.ExpectedQuantity ?? i.ReceivedQuantity ?? 0
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var inboundPlannedByProduct = inboundPlannedRows
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0, x.Qty)));

            var outboundTxQuery = _context.InventoryTransactions
                .AsNoTracking()
                .Where(t =>
                    t.Warehouse != null &&
                    t.Warehouse.CompanyId == companyId &&
                    t.ProductId.HasValue &&
                    t.TransactionType == "Outbound" &&
                    t.CreatedAt.HasValue &&
                    t.CreatedAt.Value >= from &&
                    t.CreatedAt.Value <= to);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                outboundTxQuery = outboundTxQuery.Where(t => t.WarehouseId == warehouseId.Value);

            var outboundTxRows = await outboundTxQuery
                .Select(t => new
                {
                    ProductId = t.ProductId!.Value,
                    Day = t.CreatedAt!.Value.Date,
                    Qty = Math.Abs(t.QuantityChange ?? 0),
                    ProductName = t.Product != null ? t.Product.Name : null,
                    Sku = t.Product != null ? t.Product.Sku : null
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var dailyOutboundByProduct = outboundTxRows
                .GroupBy(x => new { x.ProductId, x.Day })
                .Select(g => new { g.Key.ProductId, g.Key.Day, Qty = g.Sum(x => x.Qty) })
                .ToList();

            var forecastQuery = _context.StorageForecasts
                .AsNoTracking()
                .Where(f =>
                    f.Warehouse != null &&
                    f.Warehouse.CompanyId == companyId &&
                    f.ProductId.HasValue);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                forecastQuery = forecastQuery.Where(f => f.WarehouseId == warehouseId.Value);

            var forecastRows = await forecastQuery
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new
                {
                    ProductId = f.ProductId!.Value,
                    f.PredictedStock,
                    f.DaysToStockout,
                    f.RiskLevel,
                    f.Confidence
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var latestForecastByProduct = forecastRows
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.First());

            var productNameSku = onHandRows
                .Select(x => new { x.ProductId, x.ProductName, x.Sku })
                .Concat(outboundTxRows.Select(x => new { x.ProductId, x.ProductName, x.Sku }))
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.First());

            var productIds = onHandByProduct.Keys
                .Concat(inboundPlannedByProduct.Keys)
                .Concat(dailyOutboundByProduct.Select(x => x.ProductId))
                .Concat(latestForecastByProduct.Keys)
                .Distinct()
                .Where(x => x > 0)
                .ToList();

            var zScore = ResolveZScore(serviceLevel);
            var items = new List<ReplenishmentRecommendationItem>();
            foreach (var productId in productIds)
            {
                productNameSku.TryGetValue(productId, out var meta);
                var onHand = onHandByProduct.TryGetValue(productId, out var onHandQty) ? onHandQty : 0;
                var inboundPlanned = inboundPlannedByProduct.TryGetValue(productId, out var inboundQty) ? inboundQty : 0;
                var dailySeries = dailyOutboundByProduct.Where(x => x.ProductId == productId).Select(x => (double)x.Qty).ToList();
                var avgDailyDemand = dailySeries.Any() ? dailySeries.Average() : 0.0;
                var demandStdDev = ComputeStdDev(dailySeries);

                int? forecastDaysToStockout = null;
                double forecastDailyDemand = avgDailyDemand;
                double forecastConfidence = 0.5;
                string? forecastRiskLevel = null;
                if (latestForecastByProduct.TryGetValue(productId, out var forecast))
                {
                    forecastDaysToStockout = forecast.DaysToStockout;
                    forecastRiskLevel = forecast.RiskLevel;
                    if (forecast.Confidence.HasValue)
                        forecastConfidence = Math.Clamp(forecast.Confidence.Value, 0.0, 1.0);
                    if (forecast.PredictedStock.HasValue && forecast.DaysToStockout.HasValue && forecast.DaysToStockout.Value > 0)
                    {
                        forecastDailyDemand = Math.Max(avgDailyDemand, forecast.PredictedStock.Value / (double)forecast.DaysToStockout.Value);
                    }
                }

                if (forecastDailyDemand <= 0)
                    forecastDailyDemand = avgDailyDemand;

                var leadTimeDays = defaultLeadTimeDays;
                var forecastDemandQty = (int)Math.Ceiling(Math.Max(0, forecastDailyDemand * forecastHorizonDays));
                var safetyStock = (int)Math.Ceiling(Math.Max(0, zScore * demandStdDev * Math.Sqrt(Math.Max(1, leadTimeDays))));
                var reorderPoint = (int)Math.Ceiling(Math.Max(0, (forecastDailyDemand * leadTimeDays) + safetyStock));
                var netAvailable = onHand + inboundPlanned;
                var recommendedQty = Math.Max(0, forecastDemandQty + safetyStock - netAvailable);

                var daysToStockout = forecastDaysToStockout;
                if (!daysToStockout.HasValue && forecastDailyDemand > 0)
                    daysToStockout = (int)Math.Floor(onHand / forecastDailyDemand);

                var riskLevel = NormalizeRiskLevel(forecastRiskLevel, daysToStockout, forecastHorizonDays, recommendedQty, onHand);
                var reasonCodes = BuildReasonCodes(onHand, reorderPoint, recommendedQty, daysToStockout, forecastDailyDemand, avgDailyDemand);
                var aiReasoning = useAiExplanation
                    ? BuildAiReasoning(meta?.ProductName, onHand, forecastDemandQty, safetyStock, recommendedQty, daysToStockout, reasonCodes)
                    : null;

                items.Add(new ReplenishmentRecommendationItem(
                    productId,
                    meta?.ProductName,
                    meta?.Sku,
                    onHand,
                    inboundPlanned,
                    forecastDemandQty,
                    Math.Round(avgDailyDemand, 2),
                    Math.Round(demandStdDev, 2),
                    leadTimeDays,
                    safetyStock,
                    reorderPoint,
                    recommendedQty,
                    Math.Round(forecastConfidence, 2),
                    riskLevel,
                    daysToStockout,
                    reasonCodes,
                    aiReasoning));
            }

            var orderedItems = items
                .OrderByDescending(x => x.RecommendedQty)
                .ThenByDescending(x => x.ForecastDemandQty)
                .ThenBy(x => x.ProductId)
                .ToList();

            var summary = new ReplenishmentRecommendationSummary(
                orderedItems.Count,
                orderedItems.Count(x => x.RecommendedQty > 0),
                orderedItems.Sum(x => x.RecommendedQty),
                orderedItems.Count(x => string.Equals(x.RiskLevel, "High", StringComparison.OrdinalIgnoreCase)));

            return new ReplenishmentRecommendationReportData(
                new ReplenishmentRecommendationMeta(
                    warehouseId,
                    from,
                    to,
                    forecastHorizonDays,
                    defaultLeadTimeDays,
                    serviceLevel,
                    "hybrid-rule-v1",
                    "storage_forecasts+consumption_fallback"),
                summary,
                orderedItems);
        }

        private static double ResolveZScore(double serviceLevel)
        {
            if (serviceLevel >= 0.995) return 2.58;
            if (serviceLevel >= 0.99) return 2.33;
            if (serviceLevel >= 0.975) return 1.96;
            if (serviceLevel >= 0.95) return 1.65;
            if (serviceLevel >= 0.90) return 1.28;
            if (serviceLevel >= 0.85) return 1.04;
            return 0.84;
        }

        private static double ComputeStdDev(IReadOnlyList<double> series)
        {
            if (series.Count <= 1) return 0;
            var avg = series.Average();
            var variance = series.Sum(x => (x - avg) * (x - avg)) / (series.Count - 1);
            return Math.Sqrt(Math.Max(0, variance));
        }

        private static string NormalizeRiskLevel(string? forecastRiskLevel, int? daysToStockout, int forecastHorizonDays, int recommendedQty, int onHand)
        {
            if (!string.IsNullOrWhiteSpace(forecastRiskLevel))
            {
                var risk = forecastRiskLevel.Trim();
                if (risk.Equals("high", StringComparison.OrdinalIgnoreCase)) return "High";
                if (risk.Equals("medium", StringComparison.OrdinalIgnoreCase)) return "Medium";
                if (risk.Equals("low", StringComparison.OrdinalIgnoreCase)) return "Low";
            }

            if (daysToStockout.HasValue && daysToStockout.Value <= Math.Max(1, forecastHorizonDays / 2)) return "High";
            if (recommendedQty > 0 && onHand <= 0) return "High";
            if (recommendedQty > 0) return "Medium";
            return "Low";
        }

        private static IReadOnlyList<string> BuildReasonCodes(
            int onHand,
            int reorderPoint,
            int recommendedQty,
            int? daysToStockout,
            double forecastDailyDemand,
            double avgDailyDemand)
        {
            var reasons = new List<string>();
            if (onHand < reorderPoint) reasons.Add("LOW_STOCK");
            if (recommendedQty > 0) reasons.Add("REPLENISHMENT_NEEDED");
            if (daysToStockout.HasValue && daysToStockout.Value <= 7) reasons.Add("STOCKOUT_RISK");
            if (forecastDailyDemand > avgDailyDemand * 1.2 && avgDailyDemand > 0) reasons.Add("DEMAND_SPIKE");
            if (!reasons.Any()) reasons.Add("STABLE_STOCK");
            return reasons;
        }

        private static string BuildAiReasoning(
            string? productName,
            int onHand,
            int forecastDemandQty,
            int safetyStock,
            int recommendedQty,
            int? daysToStockout,
            IReadOnlyList<string> reasonCodes)
        {
            var display = string.IsNullOrWhiteSpace(productName) ? "SKU" : productName;
            var stockoutText = daysToStockout.HasValue ? $"{daysToStockout.Value} days" : "unknown";
            if (recommendedQty <= 0)
                return $"{display}: Current stock is sufficient for projected demand. No replenishment is required now.";

            return $"{display}: On-hand stock ({onHand}) is below projected demand ({forecastDemandQty}) plus safety stock ({safetyStock}). Recommended replenishment is {recommendedQty} units. Estimated stockout window: {stockoutText}. Reasons: {string.Join(", ", reasonCodes)}.";
        }

        private sealed class FifoLayer
        {
            public int RemainingQty { get; set; }
            public decimal UnitCost { get; set; }
        }

        private sealed record BatchSnapshotEntry(
            int BatchId,
            int ProductId,
            string? ProductName,
            string? Sku,
            int RemainingQuantity,
            decimal EffectiveUnitCost,
            DateTime InboundDate,
            int? BinId,
            string? BinCode,
            string? BinIdCode,
            int? ShelfId,
            string? ShelfCode,
            int? ZoneId,
            string? ZoneCode,
            int LocationQuantity);

        private sealed record StocktakeItemContext(
            int ProductId,
            string? ProductName,
            string? Sku,
            int? LocationId,
            int SystemQty,
            int CountedQty,
            int Variance);

        private sealed record StocktakeCostContext(
            Dictionary<int, decimal> ProductUnitCosts,
            Dictionary<(int ProductId, int LocationId), decimal> LocationUnitCosts,
            IReadOnlyList<BatchSnapshotEntry> BatchEntries)
        {
            public decimal ResolveUnitCost(int productId, int? locationId)
            {
                if (locationId.HasValue && LocationUnitCosts.TryGetValue((productId, locationId.Value), out var locationCost))
                    return locationCost;

                return ProductUnitCosts.TryGetValue(productId, out var productCost) ? productCost : 0m;
            }
        }

        private async Task<List<BatchSnapshotEntry>> GetRemainingBatchSnapshotAsync(
            int companyId,
            int? warehouseId,
            DateTime upTo,
            IReadOnlyCollection<int>? productIds = null)
        {
            var batchQuery = _context.InventoryBatches
                .AsNoTracking()
                .Where(b =>
                    b.Warehouse != null &&
                    b.Warehouse.CompanyId == companyId &&
                    b.InboundDate <= upTo &&
                    b.RemainingQuantity > 0);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                batchQuery = batchQuery.Where(b => b.WarehouseId == warehouseId.Value);

            if (productIds != null && productIds.Count > 0)
                batchQuery = batchQuery.Where(b => productIds.Contains(b.ProductId));

            var batches = await batchQuery
                .Include(b => b.Product)
                .Include(b => b.BatchLocations)
                    .ThenInclude(bl => bl.Bin)
                        .ThenInclude(bin => bin.Level)
                            .ThenInclude(level => level!.Shelf)
                                .ThenInclude(shelf => shelf!.Zone)
                .ToListAsync()
                .ConfigureAwait(false);

            return batches
                .SelectMany(
                    b => b.BatchLocations.Where(bl => bl.Quantity > 0).DefaultIfEmpty(),
                    (b, bl) => new BatchSnapshotEntry(
                        b.Id,
                        b.ProductId,
                        b.Product?.Name,
                        b.Product?.Sku,
                        b.RemainingQuantity,
                        b.EffectiveUnitCost,
                        b.InboundDate,
                        bl?.BinId,
                        bl?.Bin?.Code,
                        bl?.Bin?.IdCode,
                        bl?.Bin?.Level?.ShelfId,
                        bl?.Bin?.Level?.Shelf?.Code,
                        bl?.Bin?.Level?.Shelf?.ZoneId,
                        bl?.Bin?.Level?.Shelf?.Zone?.Code,
                        bl?.Quantity ?? 0))
                .ToList();
        }

        private static Dictionary<int, decimal> BuildWeightedUnitCostLookup(IEnumerable<BatchSnapshotEntry> entries)
        {
            return DistinctBatchEntries(entries)
                .GroupBy(x => x.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var totalQty = g.Sum(x => x.RemainingQuantity);
                        if (totalQty <= 0) return 0m;

                        var totalValue = g.Sum(x => x.RemainingQuantity * x.EffectiveUnitCost);
                        return totalValue / totalQty;
                    });
        }

        private static IReadOnlyList<BatchSnapshotEntry> DistinctBatchEntries(IEnumerable<BatchSnapshotEntry> entries)
        {
            return entries
                .GroupBy(x => x.BatchId)
                .Select(g => g.First())
                .ToList();
        }

        private static IReadOnlyList<InventoryBatchBreakdownRow> BuildBatchBreakdown(IEnumerable<BatchSnapshotEntry> entries)
        {
            return entries
                .GroupBy(x => new { x.BatchId, x.ProductId, x.ProductName, x.Sku, x.RemainingQuantity, x.EffectiveUnitCost, x.InboundDate })
                .Select(g => new InventoryBatchBreakdownRow(
                    g.Key.BatchId,
                    g.Key.ProductId,
                    g.Key.ProductName,
                    g.Key.Sku,
                    g.Key.RemainingQuantity,
                    g.Key.EffectiveUnitCost,
                    g.Key.RemainingQuantity * g.Key.EffectiveUnitCost,
                    g.Key.InboundDate,
                    g.Where(x => x.BinId.HasValue)
                        .Select(x => new InventoryBatchLocationBreakdownRow(
                            x.BinId!.Value,
                            x.BinCode,
                            x.BinIdCode,
                            x.ShelfId,
                            x.ShelfCode,
                            x.ZoneId,
                            x.ZoneCode,
                            x.LocationQuantity))
                        .OrderByDescending(x => x.Quantity)
                        .ThenBy(x => x.BinId)
                        .ToList()))
                .OrderBy(x => x.ProductId)
                .ThenBy(x => x.InboundDate)
                .ThenBy(x => x.BatchId)
                .ToList();
        }

        private async Task<StocktakeCostContext> ResolveStocktakeCostContextAsync(
            int companyId,
            int? warehouseId,
            List<int> productIds,
            List<int> locationIds,
            DateTime upTo)
        {
            var batchEntries = await GetRemainingBatchSnapshotAsync(companyId, warehouseId, upTo, productIds)
                .ConfigureAwait(false);

            var productUnitCosts = BuildWeightedUnitCostLookup(batchEntries);
            var locationUnitCosts = new Dictionary<(int ProductId, int LocationId), decimal>();

            if (locationIds.Count > 0)
            {
                var locationShelfMap = await _context.InventoryLocations
                    .AsNoTracking()
                    .Where(l => locationIds.Contains(l.Id) && l.ShelfId.HasValue)
                    .Select(l => new { LocationId = l.Id, ShelfId = l.ShelfId!.Value })
                    .ToListAsync()
                    .ConfigureAwait(false);

                foreach (var location in locationShelfMap)
                {
                    var shelfEntries = batchEntries
                        .Where(x => x.ShelfId == location.ShelfId)
                        .GroupBy(x => x.ProductId);

                    foreach (var group in shelfEntries)
                    {
                        var totalQty = group.Sum(x => x.LocationQuantity > 0 ? x.LocationQuantity : x.RemainingQuantity);
                        if (totalQty <= 0) continue;

                        var totalValue = group.Sum(x => (x.LocationQuantity > 0 ? x.LocationQuantity : x.RemainingQuantity) * x.EffectiveUnitCost);
                        locationUnitCosts[(group.Key, location.LocationId)] = totalValue / totalQty;
                    }
                }
            }

            return new StocktakeCostContext(productUnitCosts, locationUnitCosts, batchEntries);
        }

        private static IReadOnlyList<StocktakeBatchBreakdownRow> BuildStocktakeBatchBreakdown(
            IReadOnlyList<StocktakeItemContext> itemRows,
            IReadOnlyList<BatchSnapshotEntry> batchEntries,
            IReadOnlyDictionary<int, int> locationShelfMap)
        {
            var rows = itemRows.ToList();
            if (!rows.Any() || batchEntries.Count == 0)
                return Array.Empty<StocktakeBatchBreakdownRow>();

            var breakdown = new List<StocktakeBatchBreakdownRow>();

            foreach (var item in rows)
            {
                IEnumerable<BatchSnapshotEntry> relevantEntries = batchEntries.Where(x => x.ProductId == item.ProductId);

                if (item.LocationId != null && locationShelfMap.TryGetValue(item.LocationId.Value, out var shelfId))
                {
                    var locationEntries = relevantEntries.Where(x => x.ShelfId == shelfId);
                    if (locationEntries.Any())
                        relevantEntries = locationEntries;
                }

                foreach (var entry in relevantEntries
                    .OrderBy(x => x.InboundDate)
                    .ThenBy(x => x.BatchId))
                {
                    var denominator = relevantEntries.Sum(x => x.LocationQuantity > 0 ? x.LocationQuantity : x.RemainingQuantity);
                    var effectiveQty = entry.LocationQuantity > 0 ? entry.LocationQuantity : entry.RemainingQuantity;
                    var varianceShare = denominator > 0
                        ? (int)Math.Round(item.Variance * (effectiveQty / (double)denominator), MidpointRounding.AwayFromZero)
                        : item.Variance;

                    breakdown.Add(new StocktakeBatchBreakdownRow(
                        item.ProductId,
                        item.LocationId,
                        entry.BatchId,
                        entry.InboundDate,
                        entry.EffectiveUnitCost,
                        effectiveQty,
                        varianceShare,
                        varianceShare * entry.EffectiveUnitCost,
                        entry.BinCode,
                        entry.ShelfCode,
                        entry.ZoneCode));
                }
            }

            return breakdown;
        }

        private async Task<Dictionary<int, decimal>> ResolveFifoUnitCostByProductAsync(
            int companyId,
            int? warehouseId,
            List<int> productIds,
            DateTime upTo)
        {
            if (productIds.Count == 0) return new Dictionary<int, decimal>();

            var snapshot = await GetRemainingBatchSnapshotAsync(companyId, warehouseId, upTo, productIds)
                .ConfigureAwait(false);

            return BuildWeightedUnitCostLookup(snapshot);
        }
    }
}
