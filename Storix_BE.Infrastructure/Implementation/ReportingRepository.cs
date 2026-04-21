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

        public async Task<InventorySnapshotReportData> GetInventorySnapshotAsync(int companyId, int? warehouseId, DateTime from, DateTime to)
        {
            var invQuery = _context.Inventories.AsNoTracking().Where(i => i.Warehouse != null && i.Warehouse.CompanyId == companyId);
            if (warehouseId.HasValue && warehouseId.Value > 0) invQuery = invQuery.Where(i => i.WarehouseId == warehouseId.Value);

            var rows = await invQuery.Select(i => new
            {
                ProductId = i.ProductId ?? 0,
                ProductName = i.Product != null ? i.Product.Name : null,
                Sku = i.Product != null ? i.Product.Sku : null,
                Qty = i.Quantity ?? 0,
            }).Where(x => x.ProductId > 0).ToListAsync().ConfigureAwait(false);

            var productIds = rows.Select(x => x.ProductId).Distinct().ToList();
            var fifoPrices = await ResolveFifoUnitCostByProductAsync(companyId, warehouseId, productIds, to).ConfigureAwait(false);

            var items = rows.GroupBy(r => new { r.ProductId, r.ProductName, r.Sku })
                .Select(g =>
                {
                    var qty = g.Sum(x => x.Qty);
                    fifoPrices.TryGetValue(g.Key.ProductId, out var unitCost);
                    return new InventorySnapshotRow(g.Key.ProductId, g.Key.ProductName, g.Key.Sku, qty, unitCost, unitCost * qty);
                })
                .OrderByDescending(x => x.InventoryValue)
                .ToList();

            return new InventorySnapshotReportData(from, to, warehouseId, items.Count, items.Sum(x => x.Quantity), items.Sum(x => x.InventoryValue), items);
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
                    Day = t.CreatedAt!.Value,
                    ProductId = t.ProductId ?? 0,
                    ProductName = t.Product != null ? t.Product.Name : null,
                    Sku = t.Product != null ? t.Product.Sku : null,
                    t.TransactionType,
                    Qty = t.QuantityChange ?? 0
                }).ToListAsync().ConfigureAwait(false);

            var running = opening;
            var ledger = new List<InventoryLedgerRow>();
            foreach (var r in rows)
            {
                running += r.Qty;
                ledger.Add(new InventoryLedgerRow(r.Day, r.ProductId, r.ProductName, r.Sku, r.TransactionType, r.Qty > 0 ? r.Qty : 0, r.Qty < 0 ? Math.Abs(r.Qty) : 0, running));
            }

            return new InventoryLedgerReportData(from, to, warehouseId, productId, opening, running, ledger);
        }

        public async Task<InventoryInOutBalanceReportData> GetInventoryInOutBalanceAsync(int companyId, int? warehouseId, DateTime from, DateTime to)
        {
            // Opening/closing base from inventory transactions (createdAt), movement in range by completedAt.
            var txQuery = _context.InventoryTransactions.AsNoTracking().Where(t => t.Warehouse != null && t.Warehouse.CompanyId == companyId && t.CreatedAt.HasValue);
            if (warehouseId.HasValue && warehouseId.Value > 0) txQuery = txQuery.Where(t => t.WarehouseId == warehouseId.Value);

            var openingRows = await txQuery
                .Where(t => t.CreatedAt!.Value < from)
                .Select(t => new { ProductId = t.ProductId ?? 0, Qty = t.QuantityChange ?? 0 })
                .ToListAsync()
                .ConfigureAwait(false);

            var openingByProduct = openingRows
                .Where(x => x.ProductId > 0)
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            // Inbound: completed inbound orders in range (fallback createdAt due current schema).
            var inboundOrders = _context.InboundOrders.AsNoTracking()
                .Where(o => o.Warehouse != null && o.Warehouse.CompanyId == companyId && o.Status == "Completed" && o.CreatedAt.HasValue);
            if (warehouseId.HasValue && warehouseId.Value > 0) inboundOrders = inboundOrders.Where(o => o.WarehouseId == warehouseId.Value);

            var inboundRows = await inboundOrders
                .Where(o => o.CreatedAt!.Value >= from && o.CreatedAt!.Value <= to)
                .SelectMany(o => o.InboundOrderItems.Select(i => new
                {
                    ProductId = i.ProductId ?? 0,
                    ProductName = i.Product != null ? i.Product.Name : null,
                    Sku = i.Product != null ? i.Product.Sku : null,
                    Qty = i.ReceivedQuantity ?? 0,
                    Day = o.CreatedAt!.Value.Date
                }))
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

            var txCompletedAt = txQuery
                .Where(t => t.TransactionType == "Outbound")
                .GroupBy(t => t.ReferenceId)
                .Select(g => new { ReferenceId = g.Key, CompletedAt = g.Max(t => (DateTime?)t.CreatedAt) });

            var outboundCompletedInRange = await outboundBase
                .GroupJoin(statusCompletedAt, o => o.Id, s => s.OutboundOrderId, (o, statuses) => new { Order = o, StatusCompleted = statuses.FirstOrDefault() })
                .GroupJoin(txCompletedAt, x => x.Order.Id, t => t.ReferenceId, (x, txs) => new
                {
                    x.Order,
                    CompletedAt = x.StatusCompleted != null ? x.StatusCompleted.CompletedAt : txs.Select(z => z.CompletedAt).FirstOrDefault()
                })
                .Where(x => x.CompletedAt.HasValue && x.CompletedAt.Value >= from && x.CompletedAt.Value <= to)
                .Select(x => new
                {
                    x.Order.Id,
                    Day = x.CompletedAt!.Value.Date
                })
                .ToListAsync()
                .ConfigureAwait(false);

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
                .Concat(openingByProduct.Keys)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var fifoUnitCostByProduct = await ResolveFifoUnitCostByProductAsync(companyId, warehouseId, productIds, to)
                .ConfigureAwait(false);

            var byProduct = allProducts.Select(p =>
            {
                openingByProduct.TryGetValue(p.ProductId, out var opening);
                var inQty = inboundRows.Where(x => x.ProductId == p.ProductId).Sum(x => x.Qty);
                var outQty = outboundMaterialized.Where(x => x.ProductId == p.ProductId).Sum(x => x.Qty);
                var closing = opening + inQty - outQty;
                fifoUnitCostByProduct.TryGetValue(p.ProductId, out var unitCost);
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
                byProduct);
        }

        public async Task<StocktakeVarianceReportData> GetStocktakeVarianceAsync(int companyId, int? warehouseId, int? inventoryCountTicketId, DateTime from, DateTime to)
        {
            var ticketQuery = _context.InventoryCountsTickets.AsNoTracking().Where(t => t.Warehouse != null && t.Warehouse.CompanyId == companyId);
            if (warehouseId.HasValue && warehouseId.Value > 0) ticketQuery = ticketQuery.Where(t => t.WarehouseId == warehouseId.Value);
            if (inventoryCountTicketId.HasValue && inventoryCountTicketId.Value > 0) ticketQuery = ticketQuery.Where(t => t.Id == inventoryCountTicketId.Value);
            ticketQuery = ticketQuery.Where(t => t.CreatedAt.HasValue && t.CreatedAt.Value >= from && t.CreatedAt.Value <= to);

            var ticketIds = await ticketQuery.Select(t => t.Id).ToListAsync().ConfigureAwait(false);
            var itemRows = await _context.InventoryCountItems.AsNoTracking().Where(i => i.InventoryCountId.HasValue && ticketIds.Contains(i.InventoryCountId.Value))
                .Select(i => new
                {
                    ProductId = i.ProductId ?? 0,
                    ProductName = i.Product != null ? i.Product.Name : null,
                    Sku = i.Product != null ? i.Product.Sku : null,
                    SystemQty = i.SystemQuantity ?? 0,
                    CountedQty = i.FinalQuantity ?? i.CountedQuantity ?? 0,
                    Variance = i.Discrepancy ?? ((i.FinalQuantity ?? i.CountedQuantity ?? 0) - (i.SystemQuantity ?? 0))
                }).Where(x => x.ProductId > 0).ToListAsync().ConfigureAwait(false);

            var fifoPrices = await ResolveFifoUnitCostByProductAsync(
                companyId,
                warehouseId,
                itemRows.Select(x => x.ProductId).Distinct().ToList(),
                to).ConfigureAwait(false);

            var items = itemRows.Select(x =>
            {
                fifoPrices.TryGetValue(x.ProductId, out var cost);
                return new StocktakeVarianceRow(x.ProductId, x.ProductName, x.Sku, x.SystemQty, x.CountedQty, x.Variance, cost, cost * x.Variance);
            }).ToList();

            return new StocktakeVarianceReportData(from, to, warehouseId, inventoryCountTicketId, items.Count, items.Sum(x => x.VarianceQty), items.Sum(x => x.VarianceValue), items);
        }



        private async Task<Dictionary<int, decimal>> ResolveUnitCostByProductAsync(List<int> productIds, DateTime upTo)
        {
            if (productIds.Count == 0) return new Dictionary<int, decimal>();

            var priceRows = await _context.ProductPrices.AsNoTracking()
                .Where(p => p.ProductId.HasValue && productIds.Contains(p.ProductId.Value) && p.Date.HasValue && p.Date.Value <= DateOnly.FromDateTime(upTo))
                .GroupBy(p => p.ProductId!.Value)
                .Select(g => g.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).FirstOrDefault())
                .ToListAsync().ConfigureAwait(false);

            return priceRows
                .Where(x => x != null && x.ProductId.HasValue)
                .ToDictionary(x => x!.ProductId!.Value, x => Convert.ToDecimal(x!.Price ?? 0d));
        }

        private sealed class FifoLayer
        {
            public int RemainingQty { get; set; }
            public decimal UnitCost { get; set; }
        }

        private async Task<Dictionary<int, decimal>> ResolveFifoUnitCostByProductAsync(
            int companyId,
            int? warehouseId,
            List<int> productIds,
            DateTime upTo)
        {
            if (productIds.Count == 0) return new Dictionary<int, decimal>();

            var inboundOrders = _context.InboundOrders.AsNoTracking()
                .Where(o => o.Warehouse != null && o.Warehouse.CompanyId == companyId && o.Status == "Completed" && o.CreatedAt.HasValue && o.CreatedAt.Value <= upTo);
            if (warehouseId.HasValue && warehouseId.Value > 0) inboundOrders = inboundOrders.Where(o => o.WarehouseId == warehouseId.Value);

            var inboundLots = await inboundOrders
                .SelectMany(o => o.InboundOrderItems.Select(i => new
                {
                    ProductId = i.ProductId ?? 0,
                    Qty = i.ReceivedQuantity ?? 0,
                    UnitCost = Convert.ToDecimal(i.Price ?? 0d),
                    Time = o.CreatedAt!.Value,
                    OrderItemId = i.Id
                }))
                .Where(x => x.ProductId > 0 && productIds.Contains(x.ProductId) && x.Qty > 0)
                .OrderBy(x => x.Time)
                .ThenBy(x => x.OrderItemId)
                .ToListAsync()
                .ConfigureAwait(false);

            var statusCompletedAt = _context.OutboundOrderStatusHistories
                .Where(h => h.NewStatus == "Completed")
                .GroupBy(h => h.OutboundOrderId)
                .Select(g => new { OutboundOrderId = g.Key, CompletedAt = g.Max(h => (DateTime?)h.ChangedAt) });

            var outboundOrders = _context.OutboundOrders.AsNoTracking()
                .Where(o => o.Warehouse != null && o.Warehouse.CompanyId == companyId);
            if (warehouseId.HasValue && warehouseId.Value > 0) outboundOrders = outboundOrders.Where(o => o.WarehouseId == warehouseId.Value);

            var outboundCompleted = await outboundOrders
                .GroupJoin(statusCompletedAt, o => o.Id, s => s.OutboundOrderId, (o, statuses) => new
                {
                    o.Id,
                    CompletedAt = statuses.Select(x => x.CompletedAt).FirstOrDefault()
                })
                .Where(x => x.CompletedAt.HasValue && x.CompletedAt.Value <= upTo)
                .Select(x => new { x.Id, CompletedAt = x.CompletedAt!.Value })
                .ToListAsync()
                .ConfigureAwait(false);

            var outboundIds = outboundCompleted.Select(x => x.Id).ToList();
            var outboundTimeMap = outboundCompleted.ToDictionary(x => x.Id, x => x.CompletedAt);

            var outboundItems = await _context.OutboundOrderItems.AsNoTracking()
                .Where(i => i.OutboundOrderId.HasValue && outboundIds.Contains(i.OutboundOrderId.Value) && i.ProductId.HasValue)
                .Select(i => new
                {
                    ProductId = i.ProductId!.Value,
                    Qty = i.ReceivedQuantity ?? i.ExpectedQuantity ?? i.Quantity ?? 0,
                    OutboundOrderId = i.OutboundOrderId!.Value,
                    OrderItemId = i.Id
                })
                .Where(x => x.Qty > 0 && productIds.Contains(x.ProductId))
                .ToListAsync()
                .ConfigureAwait(false);

            var outboundEvents = outboundItems
                .Where(x => outboundTimeMap.ContainsKey(x.OutboundOrderId))
                .Select(x => new
                {
                    x.ProductId,
                    x.Qty,
                    Time = outboundTimeMap[x.OutboundOrderId],
                    x.OrderItemId
                })
                .OrderBy(x => x.Time)
                .ThenBy(x => x.OrderItemId)
                .ToList();

            var events = inboundLots
                .Select(x => new { x.ProductId, Qty = x.Qty, x.UnitCost, x.Time, IsInbound = true, x.OrderItemId })
                .Concat(outboundEvents.Select(x => new { x.ProductId, Qty = x.Qty, UnitCost = 0m, x.Time, IsInbound = false, x.OrderItemId }))
                .OrderBy(x => x.Time)
                .ThenBy(x => x.OrderItemId)
                .ToList();

            var layersByProduct = new Dictionary<int, Queue<FifoLayer>>();
            foreach (var pid in productIds) layersByProduct[pid] = new Queue<FifoLayer>();

            foreach (var e in events)
            {
                if (!layersByProduct.TryGetValue(e.ProductId, out var queue))
                {
                    queue = new Queue<FifoLayer>();
                    layersByProduct[e.ProductId] = queue;
                }

                if (e.IsInbound)
                {
                    queue.Enqueue(new FifoLayer { RemainingQty = e.Qty, UnitCost = e.UnitCost });
                    continue;
                }

                var remainingOut = e.Qty;
                while (remainingOut > 0 && queue.Count > 0)
                {
                    var layer = queue.Peek();
                    var consume = Math.Min(remainingOut, layer.RemainingQty);
                    layer.RemainingQty -= consume;
                    remainingOut -= consume;
                    if (layer.RemainingQty <= 0) queue.Dequeue();
                }
            }

            var result = new Dictionary<int, decimal>();
            foreach (var pid in productIds)
            {
                if (!layersByProduct.TryGetValue(pid, out var queue) || queue.Count == 0)
                {
                    result[pid] = 0m;
                    continue;
                }

                var totalQty = queue.Sum(x => x.RemainingQty);
                if (totalQty <= 0)
                {
                    result[pid] = 0m;
                    continue;
                }

                var totalValue = queue.Sum(x => x.RemainingQty * x.UnitCost);
                result[pid] = totalValue / totalQty;
            }

            return result;
        }
    }
}

