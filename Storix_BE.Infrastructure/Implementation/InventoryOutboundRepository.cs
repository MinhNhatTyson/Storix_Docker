using ClosedXML.Excel;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class InventoryOutboundRepository : IInventoryOutboundRepository
    {
        private readonly StorixDbContext _context;
        private readonly ILogger<InventoryOutboundRepository> _logger;

        public InventoryOutboundRepository(StorixDbContext context, ILogger<InventoryOutboundRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        private async Task UpdateProductPopularityAsync()
        {
            // Example using Raw SQL in Entity Framework
            var sql = @"UPDATE products
                    SET popularity_score = sub.new_score
                    FROM (
                        SELECT 
                            p.id,
                            (COALESCE(SUM(ooi.quantity), 0) * 0.6) + 
                            (CASE 
                                WHEN MAX(it.created_at) IS NULL THEN 0 
                                ELSE (30 - EXTRACT(DAY FROM (NOW() - MAX(it.created_at)))) 
                             END * 0.4) as new_score
                        FROM products p
                        LEFT JOIN outbound_order_items ooi ON p.id = ooi.product_id
                        LEFT JOIN inventory_transactions it ON p.id = it.product_id 
                            AND it.transaction_type = 'OUT'
                        WHERE it.created_at > NOW() - INTERVAL '30 days' OR it.created_at IS NULL
                        GROUP BY p.id
                    ) AS sub
                    WHERE products.id = sub.id;";
            await _context.Database.ExecuteSqlRawAsync(sql);
        }
        public async Task<OutboundRequest> CreateOutboundRequestAsync(OutboundRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!request.RequestedBy.HasValue || request.RequestedBy <= 0)
                throw new InvalidOperationException("RequestedBy is required for outbound requests.");

            await EnsureStaffRequesterAsync(request.RequestedBy.Value).ConfigureAwait(false);

            if (request.OutboundOrderItems == null || !request.OutboundOrderItems.Any())
                throw new InvalidOperationException("OutboundRequest must contain at least one OutboundOrderItem.");

            var invalidItem = request.OutboundOrderItems.FirstOrDefault(i => i.ProductId == null || i.Quantity == null || i.Quantity <= 0);
            if (invalidItem != null)
                throw new InvalidOperationException("All OutboundOrderItems must specify a ProductId and Quantity > 0.");

            var productIds = request.OutboundOrderItems.Select(i => i.ProductId!.Value).Distinct().ToList();
            var existingProductIds = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var missing = productIds.Except(existingProductIds).ToList();
            if (missing.Any())
                throw new InvalidOperationException($"Products not found: {string.Join(',', missing)}");

            if (request.WarehouseId.HasValue)
            {
                var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId.Value).ConfigureAwait(false);
                if (warehouse == null)
                    throw new InvalidOperationException($"Warehouse with id {request.WarehouseId.Value} not found.");
                if (IsInactiveStatus(warehouse.Status))
                    throw new InvalidOperationException($"Warehouse with id {request.WarehouseId.Value} is inactive.");
            }
            else
            {
                throw new InvalidOperationException("WarehouseId is required for outbound requests.");
            }

            await EnsureStockAvailabilityAsync(request.WarehouseId.Value, request.OutboundOrderItems)
                .ConfigureAwait(false);

            request.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            if (string.IsNullOrWhiteSpace(request.Status))
                request.Status = "Pending";

            foreach (var item in request.OutboundOrderItems)
            {
                item.OutboundRequest = request;
            }

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.OutboundRequests.Add(request);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return request;
        }

        public async Task<IReadOnlyList<(int ProductId, int AvailableQuantity)>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds)
        {
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouseId.", nameof(warehouseId));
            if (productIds == null) throw new ArgumentNullException(nameof(productIds));

            var distinctIds = productIds.Where(id => id > 0).Distinct().ToList();
            if (!distinctIds.Any()) throw new ArgumentException("ProductIds is required.", nameof(productIds));

            var inventory = await _context.Inventories
                .Where(i => i.WarehouseId == warehouseId && i.ProductId.HasValue && distinctIds.Contains(i.ProductId.Value))
                .Select(i => new { ProductId = i.ProductId!.Value, Quantity = i.Quantity ?? 0 })
                .ToListAsync()
                .ConfigureAwait(false);

            var quantityByProduct = inventory.ToDictionary(i => i.ProductId, i => i.Quantity);

            var result = distinctIds
                .Select(id => (ProductId: id, AvailableQuantity: quantityByProduct.TryGetValue(id, out var qty) ? qty : 0))
                .ToList();

            return result;
        }

        public async Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status)
        {
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            await EnsureManagerApproverAsync(approverId).ConfigureAwait(false);

            var outbound = await _context.OutboundRequests
                .FirstOrDefaultAsync(r => r.Id == requestId)
                .ConfigureAwait(false);

            if (outbound == null)
                throw new InvalidOperationException($"OutboundRequest with id {requestId} not found.");

            outbound.Status = status;
            outbound.ApprovedBy = approverId;
            outbound.ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return outbound;
        }

        public async Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note, string? pricingMethod = "LastPurchasePrice")
        {
            var outboundRequest = await _context.OutboundRequests
                .Include(r => r.OutboundOrderItems)
                .FirstOrDefaultAsync(r => r.Id == outboundRequestId)
                .ConfigureAwait(false);

            if (outboundRequest == null)
                throw new InvalidOperationException($"OutboundRequest with id {outboundRequestId} not found.");

            if (!string.Equals(outboundRequest.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("OutboundRequest must be in 'Approved' status to create an OutboundOrder.");

            if (!outboundRequest.WarehouseId.HasValue)
                throw new InvalidOperationException("OutboundRequest must specify WarehouseId to check inventory availability.");

            await EnsureWarehouseActiveAsync(outboundRequest.WarehouseId.Value).ConfigureAwait(false);

            var productIds = outboundRequest.OutboundOrderItems
                .Where(i => i.ProductId.HasValue)
                .Select(i => i.ProductId!.Value)
                .Distinct()
                .ToList();

            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == outboundRequest.WarehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var reqItem in outboundRequest.OutboundOrderItems)
            {
                if (!reqItem.ProductId.HasValue || !reqItem.Quantity.HasValue)
                    throw new InvalidOperationException("OutboundRequest items must specify ProductId and Quantity.");

                var inventory = inventories.FirstOrDefault(i => i.ProductId == reqItem.ProductId);
                if (inventory == null || (inventory.Quantity ?? 0) < reqItem.Quantity)
                {
                    var available = inventory?.Quantity ?? 0;
                    throw new InvalidOperationException($"Insufficient stock for ProductId {reqItem.ProductId}. Available: {available}, Requested: {reqItem.Quantity}");
                }
            }

            if (staffId.HasValue)
            {
                await EnsureStaffAssignedToWarehouseAsync(outboundRequest.WarehouseId.Value, staffId.Value)
                    .ConfigureAwait(false);
            }

            var outboundOrder = new OutboundOrder
            {
                WarehouseId = outboundRequest.WarehouseId,
                Destination = outboundRequest.Destination,
                CreatedBy = createdBy,
                StaffId = staffId,
                Note = note,
                Status = "Created",
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            var method = string.IsNullOrWhiteSpace(pricingMethod) ? "LastPurchasePrice" : pricingMethod.Trim();

            foreach (var reqItem in outboundRequest.OutboundOrderItems)
            {
                var costPrice = await ResolveCostPriceAsync(reqItem.ProductId, method, outboundRequest.CreatedAt)
                    .ConfigureAwait(false);

                var orderItem = new OutboundOrderItem
                {
                    ProductId = reqItem.ProductId,
                    Quantity = reqItem.Quantity,
                    OutboundRequestId = outboundRequest.Id,
                    PricingMethod = method,
                    CostPrice = costPrice,
                    Price = reqItem.Price
                };
                outboundOrder.OutboundOrderItems.Add(orderItem);
            }

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.OutboundOrders.Add(outboundOrder);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
            await UpdateProductPopularityAsync().ConfigureAwait(false);
            return outboundOrder;
        }

        public async Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<OutboundOrderItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var order = await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.StaffId.HasValue)
                throw new InvalidOperationException("OutboundOrder must assign StaffId before staff can update items.");

            await EnsureStaffAssignedToWarehouseAsync(order.WarehouseId ?? 0, order.StaffId.Value)
                .ConfigureAwait(false);

            var currentStatus = NormalizeStatus(order.Status ?? "Created");
            if (!IsItemUpdateAllowedStatus(currentStatus))
                throw new InvalidOperationException($"Items can only be updated during QualityCheck/LoadTemporary/IssueReported. Current status: '{currentStatus}'.");

            var incomingList = items.ToList();
            foreach (var incoming in incomingList)
            {
                if (incoming.ProductId == null || incoming.ProductId <= 0)
                    throw new InvalidOperationException("Each item must have a valid ProductId.");
                if (incoming.Quantity == null || incoming.Quantity <= 0)
                    throw new InvalidOperationException("Each item must have Quantity > 0.");
            }

            var outboundRequestIds = order.OutboundOrderItems
                .Where(x => x.OutboundRequestId.HasValue)
                .Select(x => x.OutboundRequestId!.Value)
                .Distinct()
                .ToList();

            if (outboundRequestIds.Count != 1)
                throw new InvalidOperationException("OutboundOrder must map to exactly one OutboundRequest for item verification.");

            var outboundRequestId = outboundRequestIds[0];

            var requestedItems = await _context.OutboundOrderItems
                .AsNoTracking()
                .Where(x => x.OutboundRequestId == outboundRequestId && x.OutboundOrderId == null)
                .ToListAsync()
                .ConfigureAwait(false);

            if (!requestedItems.Any())
                throw new InvalidOperationException($"No source request items found for OutboundRequestId {outboundRequestId}.");

            var requestByProduct = requestedItems
                .Where(x => x.ProductId.HasValue)
                .GroupBy(x => x.ProductId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity ?? 0));

            foreach (var incoming in incomingList)
            {
                OutboundOrderItem? existing = null;

                if (incoming.Id > 0)
                {
                    existing = order.OutboundOrderItems.FirstOrDefault(x => x.Id == incoming.Id);
                    if (existing == null)
                        throw new InvalidOperationException($"OutboundOrderItem with id {incoming.Id} not found in order {outboundOrderId}.");
                }
                else
                {
                    existing = order.OutboundOrderItems.FirstOrDefault(x => x.ProductId == incoming.ProductId);
                }

                if (existing == null)
                    throw new InvalidOperationException("Cannot add new items that are not in the original outbound ticket.");

                if (existing.OutboundRequestId != outboundRequestId)
                    throw new InvalidOperationException("Item is not linked to the original outbound request.");

                if (existing.ProductId != incoming.ProductId)
                    throw new InvalidOperationException("Changing ProductId is not allowed when updating outbound ticket items.");

                existing.Quantity = incoming.Quantity;
            }

            var currentByProduct = order.OutboundOrderItems
                .Where(x => x.ProductId.HasValue)
                .GroupBy(x => x.ProductId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity ?? 0));

            var requestProductIds = requestByProduct.Keys.OrderBy(x => x).ToList();
            var currentProductIds = currentByProduct.Keys.OrderBy(x => x).ToList();
            if (!requestProductIds.SequenceEqual(currentProductIds))
                throw new InvalidOperationException("Outbound ticket items must keep the same product set as the original outbound request.");

            foreach (var kv in requestByProduct)
            {
                var productId = kv.Key;
                var requestedQty = kv.Value;
                var actualQty = currentByProduct.TryGetValue(productId, out var qty) ? qty : 0;

                if (actualQty != requestedQty)
                    throw new InvalidOperationException($"ProductId {productId} must keep total quantity {requestedQty} as requested. Current: {actualQty}.");
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
            return order;
        }

        public async Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status)
        {
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var order = await _context.OutboundOrders
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.StaffId.HasValue)
                throw new InvalidOperationException("OutboundOrder must assign StaffId before staff can update status.");

            if (order.StaffId.Value != performedBy)
                throw new InvalidOperationException("Only assigned staff can update outbound order status.");

            await EnsureStaffAssignedToWarehouseAsync(order.WarehouseId ?? 0, performedBy)
                .ConfigureAwait(false);

            if (string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Completed outbound order cannot be updated.");

            var normalized = NormalizeStatus(status);
            if (!IsStaffStatusAllowed(normalized))
                throw new InvalidOperationException("Invalid staff status. Allowed: Picking, QualityCheck, IssueReported, Packing, LoadHandover.");

            var current = NormalizeStatus(order.Status ?? "Created");
            if (!IsStaffTransitionAllowed(current, normalized))
                throw new InvalidOperationException($"Invalid status transition from '{current}' to '{normalized}'.");

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var oldStatus = order.Status;

            order.Status = normalized;
            await _context.SaveChangesAsync().ConfigureAwait(false);

            // Best-effort audit logging: outbound status update should not fail
            // just because the history table is missing/misconfigured.
            try
            {
                _context.OutboundOrderStatusHistories.Add(new OutboundOrderStatusHistory
                {
                    OutboundOrderId = order.Id,
                    OldStatus = oldStatus,
                    NewStatus = normalized,
                    ChangedByUserId = performedBy,
                    ChangedAt = now
                });

                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write outbound status history. OutboundOrderId={OutboundOrderId}", order.Id);
            }

            return order;
        }

        public async Task<OutboundOrder> ConfirmOutboundOrderAsync(
            int outboundOrderId,
            int performedBy,
            IEnumerable<(int ProductId, int BatchId, int Quantity)> allocations,
            IEnumerable<(int ProductId, int ShelfId, int Quantity)>? locationAllocations = null,
            string? note = null)
        {
            if (allocations == null) throw new ArgumentNullException(nameof(allocations));

            var allocationList = allocations.ToList();
            if (!allocationList.Any())
                throw new InvalidOperationException("Allocations are required for specific identification costing.");

            var order = await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.WarehouseId.HasValue)
                throw new InvalidOperationException("OutboundOrder must specify WarehouseId to update inventory.");

            await EnsureWarehouseActiveAsync(order.WarehouseId.Value).ConfigureAwait(false);
            await EnsureManagerPerformerAsync(performedBy).ConfigureAwait(false);

            if (string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Outbound order is already completed.");

            var orderItems = order.OutboundOrderItems.Where(i => i.ProductId.HasValue && i.Quantity.HasValue).ToList();
            if (!orderItems.Any())
                throw new InvalidOperationException("Outbound order has no valid items.");

            var orderProductIds = orderItems.Select(i => i.ProductId!.Value).Distinct().ToHashSet();
            var allocProductIds = allocationList.Select(a => a.ProductId).Distinct().ToHashSet();
            if (!orderProductIds.SetEquals(allocProductIds))
                throw new InvalidOperationException("Allocations must cover exactly all products in outbound order.");

            foreach (var productId in orderProductIds)
            {
                var required = orderItems.Where(i => i.ProductId == productId).Sum(i => i.Quantity ?? 0);
                var allocated = allocationList.Where(a => a.ProductId == productId).Sum(a => a.Quantity);
                if (required != allocated)
                    throw new InvalidOperationException($"Allocated quantity mismatch for ProductId {productId}. Required: {required}, Allocated: {allocated}.");
            }

            var productIds = orderProductIds.ToList();
            var batchIds = allocationList.Select(a => a.BatchId).Distinct().ToList();

            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == order.WarehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync()
                .ConfigureAwait(false);

            var inboundBatchRows = await _context.InboundOrderItems
                .Include(i => i.InboundOrder)
                .Where(i => i.ProductId.HasValue && productIds.Contains(i.ProductId.Value) &&
                            i.InboundOrderId.HasValue && batchIds.Contains(i.InboundOrderId.Value) &&
                            i.InboundOrder != null && i.InboundOrder.WarehouseId == order.WarehouseId)
                .ToListAsync()
                .ConfigureAwait(false);

            var outboundBatchTx = await _context.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.WarehouseId == order.WarehouseId && t.ProductId.HasValue && productIds.Contains(t.ProductId.Value) &&
                            t.TransactionType != null && t.TransactionType.StartsWith("OutboundSpecific:"))
                .ToListAsync()
                .ConfigureAwait(false);

            var locationList = (locationAllocations ?? Enumerable.Empty<(int ProductId, int ShelfId, int Quantity)>()).ToList();
            if (locationList.Any())
            {
                var locationProductIds = locationList.Select(x => x.ProductId).Distinct().ToHashSet();
                if (!locationProductIds.SetEquals(orderProductIds))
                    throw new InvalidOperationException("Location allocations must cover exactly all products in outbound order.");

                foreach (var productId in orderProductIds)
                {
                    var required = orderItems.Where(i => i.ProductId == productId).Sum(i => i.Quantity ?? 0);
                    var allocated = locationList.Where(a => a.ProductId == productId).Sum(a => a.Quantity);
                    if (required != allocated)
                        throw new InvalidOperationException($"Location quantity mismatch for ProductId {productId}. Required: {required}, Allocated: {allocated}.");
                }
            }

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            var oldStatusForHistory = order.Status;
            try
            {
                foreach (var alloc in allocationList)
                {
                    if (alloc.ProductId <= 0 || alloc.BatchId <= 0 || alloc.Quantity <= 0)
                        throw new InvalidOperationException("Each allocation must have ProductId > 0, BatchId > 0 and Quantity > 0.");

                    var batchRow = inboundBatchRows.FirstOrDefault(r => r.ProductId == alloc.ProductId && r.InboundOrderId == alloc.BatchId);
                    if (batchRow == null)
                        throw new InvalidOperationException($"Batch {alloc.BatchId} for ProductId {alloc.ProductId} not found in the same warehouse.");

                    var batchReceived = batchRow.ReceivedQuantity ?? 0;
                    var alreadyIssuedFromBatch = outboundBatchTx
                        .Where(t => t.ProductId == alloc.ProductId && string.Equals(t.TransactionType, $"OutboundSpecific:{alloc.BatchId}", StringComparison.OrdinalIgnoreCase))
                        .Sum(t => Math.Abs(t.QuantityChange ?? 0));

                    var batchRemaining = batchReceived - alreadyIssuedFromBatch;
                    if (alloc.Quantity > batchRemaining)
                        throw new InvalidOperationException($"Batch {alloc.BatchId} for ProductId {alloc.ProductId} has only {batchRemaining} remaining, cannot issue {alloc.Quantity}.");

                    var inventory = inventories.FirstOrDefault(i => i.ProductId == alloc.ProductId);
                    if (inventory == null || (inventory.Quantity ?? 0) < alloc.Quantity)
                    {
                        var available = inventory?.Quantity ?? 0;
                        throw new InvalidOperationException($"Insufficient stock for ProductId {alloc.ProductId}. Available: {available}, Requested: {alloc.Quantity}");
                    }

                    var lineCost = batchRow.Price ?? 0;
                    var discount = batchRow.Discount ?? 0;
                    if (discount < 0) discount = 0;
                    if (discount > 100) discount = 100;
                    var unitCost = lineCost - (lineCost * (discount / 100.0));
                    if (unitCost < 0) unitCost = 0;

                    inventory.Quantity = (inventory.Quantity ?? 0) - alloc.Quantity;
                    inventory.LastUpdated = now;

                    _context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        WarehouseId = order.WarehouseId,
                        ProductId = alloc.ProductId,
                        TransactionType = $"OutboundSpecific:{alloc.BatchId}",
                        QuantityChange = -alloc.Quantity,
                        ReferenceId = order.Id,
                        PerformedBy = performedBy,
                        CreatedAt = now
                    });

                    _context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        WarehouseId = order.WarehouseId,
                        ProductId = alloc.ProductId,
                        TransactionType = "Outbound",
                        QuantityChange = -alloc.Quantity,
                        ReferenceId = order.Id,
                        PerformedBy = performedBy,
                        CreatedAt = now
                    });

                    var matchingOrderItems = orderItems.Where(i => i.ProductId == alloc.ProductId).ToList();
                    foreach (var oi in matchingOrderItems)
                    {
                        oi.PricingMethod = "SpecificIdentification";
                        oi.CostPrice = unitCost;
                    }
                }

                if (locationList.Any())
                {
                    var shelfIds = locationList.Select(x => x.ShelfId).Distinct().ToList();
                    var shelfSet = await _context.Shelves
                        .AsNoTracking()
                        .Where(s => shelfIds.Contains(s.Id)
                                    && s.Zone != null
                                    && s.Zone.WarehouseId == order.WarehouseId)
                        .Select(s => s.Id)
                        .ToListAsync()
                        .ConfigureAwait(false);

                    var validShelfIds = shelfSet.ToHashSet();
                    var invalidShelfId = locationList.Select(x => x.ShelfId).FirstOrDefault(sid => !validShelfIds.Contains(sid));
                    if (invalidShelfId > 0)
                        throw new InvalidOperationException($"Shelf {invalidShelfId} is invalid or does not belong to outbound warehouse.");

                    foreach (var loc in locationList)
                    {
                        var inventory = inventories.First(i => i.ProductId == loc.ProductId);

                        var invLoc = await _context.InventoryLocations
                            .FirstOrDefaultAsync(x => x.InventoryId == inventory.Id && x.ShelfId == loc.ShelfId)
                            .ConfigureAwait(false);

                        if (invLoc == null)
                        {
                            throw new InvalidOperationException($"No stock placement found at ShelfId {loc.ShelfId} for ProductId {loc.ProductId}.");
                        }

                        var currentQty = invLoc.Quantity ?? 0;
                        if (currentQty < loc.Quantity)
                            throw new InvalidOperationException($"Insufficient shelf stock for ProductId {loc.ProductId} at ShelfId {loc.ShelfId}. Available: {currentQty}, Requested: {loc.Quantity}.");

                        invLoc.Quantity = currentQty - loc.Quantity;
                        invLoc.UpdatedAt = now;

                        _context.ActivityLogs.Add(new ActivityLog
                        {
                            UserId = performedBy,
                            Entity = "OutboundOrder",
                            EntityId = order.Id,
                            Action = $"OUTBOUND_LOCATION_TRANSITION:PRODUCT={loc.ProductId};SHELF={loc.ShelfId};QTY={loc.Quantity};NOTE={note}",
                            Timestamp = now
                        });
                    }
                }

                order.Status = "Completed";

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            try
            {
                _context.OutboundOrderStatusHistories.Add(new OutboundOrderStatusHistory
                {
                    OutboundOrderId = order.Id,
                    OldStatus = oldStatusForHistory,
                    NewStatus = "Completed",
                    ChangedByUserId = performedBy,
                    ChangedAt = now
                });

                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write outbound completion history. OutboundOrderId={OutboundOrderId}", order.Id);
            }

            return order;
        }

        private async Task<double?> ResolveCostPriceAsync(int? productId, string pricingMethod, DateTime? asOf)
        {
            if (!productId.HasValue || productId.Value <= 0) return null;

            var method = string.IsNullOrWhiteSpace(pricingMethod) ? "LastPurchasePrice" : pricingMethod.Trim();

            // SpecificIdentification: use the latest recorded inbound price snapshot as product-specific identified cost.
            // (If your domain later adds lot/serial mapping, replace this with lot-level cost lookup.)
            var latestPrice = await _context.ProductPrices
                .AsNoTracking()
                .Where(p => p.ProductId == productId.Value)
                .OrderByDescending(p => p.Date)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (latestPrice == null || !latestPrice.Price.HasValue)
                return null;

            var lineDiscount = latestPrice.LineDiscount ?? 0;
            if (lineDiscount < 0) lineDiscount = 0;
            if (lineDiscount > 100) lineDiscount = 100;

            var basePrice = latestPrice.Price.Value;
            var effective = basePrice - (basePrice * (lineDiscount / 100.0));
            if (effective < 0) effective = 0;

            if (string.Equals(method, "SpecificIdentification", StringComparison.OrdinalIgnoreCase))
                return effective;

            // LastPurchasePrice fallback (same source currently, explicit branch for readability/extensibility)
            return effective;
        }

        private static bool IsInactiveStatus(string? status)
        {
            return string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase);
        }

        private async Task EnsureWarehouseActiveAsync(int warehouseId)
        {
            var warehouse = await _context.Warehouses.FindAsync(warehouseId).ConfigureAwait(false);
            if (warehouse == null)
                throw new InvalidOperationException($"Warehouse with id {warehouseId} not found.");
            if (IsInactiveStatus(warehouse.Status))
                throw new InvalidOperationException($"Warehouse with id {warehouseId} is inactive.");
        }

        private async Task EnsureStaffRequesterAsync(int requesterId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == requesterId)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {requesterId} not found.");

            if (!user.RoleId.HasValue || user.RoleId.Value != 4)
                throw new InvalidOperationException("Only Staff can create outbound requests.");
        }

        private async Task EnsureManagerPerformerAsync(int performerId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == performerId)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {performerId} not found.");

            if (!user.RoleId.HasValue || user.RoleId.Value != 3)
                throw new InvalidOperationException("Only Manager can confirm outbound orders.");
        }

        private async Task EnsureManagerApproverAsync(int approverId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == approverId)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {approverId} not found.");

            if (!user.RoleId.HasValue)
                throw new InvalidOperationException($"User with id {approverId} has no role assigned.");

            if (user.RoleId.Value == 1)
                throw new InvalidOperationException("Super Admin cannot approve outbound requests.");

            if (user.RoleId.Value != 3)
                throw new InvalidOperationException("Only Manager can approve outbound requests.");
        }

        public async Task<List<OutboundRequest>> GetAllOutboundRequestsAsync(int companyId, int? warehouseId)
        {
            var query = _context.OutboundRequests
                .Include(r => r.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r => r.Warehouse != null && r.Warehouse.CompanyId == companyId);

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                query = query.Where(r => r.WarehouseId == warehouseId.Value);
            }

            return await query
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<OutboundRequest> GetOutboundRequestByIdAsync(int companyId, int id)
        {
            var request = await _context.OutboundRequests
                .Include(r => r.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.Warehouse != null &&
                    r.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (request == null)
                throw new InvalidOperationException($"OutboundRequest with id {id} not found.");

            return request;
        }

        public async Task<List<OutboundOrder>> GetAllOutboundOrdersAsync(int companyId, int? warehouseId)
        {
            var query = _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Where(o => o.Warehouse != null && o.Warehouse.CompanyId == companyId);

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                query = query.Where(o => o.WarehouseId == warehouseId.Value);
            }

            return await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<OutboundOrder> GetOutboundOrderByIdAsync(int companyId, int id)
        {
            var order = await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .FirstOrDefaultAsync(o =>
                    o.Id == id &&
                    o.Warehouse != null &&
                    o.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {id} not found.");

            return order;
        }

        public async Task<List<OutboundOrder>> GetOutboundOrdersByStaffAsync(int companyId, int staffId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (staffId <= 0) throw new ArgumentException("Invalid staff id.", nameof(staffId));

            var query = _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Include(o => o.Staff)
                .Where(o => o.StaffId == staffId && o.Warehouse != null && o.Warehouse.CompanyId == companyId)
                .OrderByDescending(o => o.CreatedAt);

            return await query.ToListAsync().ConfigureAwait(false);
        }

        private async Task EnsureStaffAssignedToWarehouseAsync(int warehouseId, int staffId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == staffId)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {staffId} not found.");

            if (!user.RoleId.HasValue || user.RoleId.Value != 4)
                throw new InvalidOperationException("Only Staff (roleId=4) can perform picking/packing.");

            var warehouse = await _context.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == warehouseId)
                .ConfigureAwait(false);

            if (warehouse == null)
                throw new InvalidOperationException($"Warehouse with id {warehouseId} not found.");

            if (!user.CompanyId.HasValue || !warehouse.CompanyId.HasValue || user.CompanyId.Value != warehouse.CompanyId.Value)
                throw new InvalidOperationException("Staff does not belong to the same company as the warehouse.");

            var assigned = await _context.WarehouseAssignments
                .AnyAsync(a => a.WarehouseId == warehouseId && a.UserId == staffId)
                .ConfigureAwait(false);

            if (!assigned)
                throw new InvalidOperationException($"Staff {staffId} is not assigned to warehouse {warehouseId}.");
        }

        private static bool IsStaffStatusAllowed(string status)
        {
            return status is "Picking" or "QualityCheck" or "IssueReported" or "Packing" or "LoadHandover";
        }

        private static bool IsItemUpdateAllowedStatus(string status)
        {
            return status is "QualityCheck" or "IssueReported";
        }

        private static string NormalizeStatus(string status)
        {
            var trimmed = status.Trim();
            return trimmed.Length == 0
                ? trimmed
                : char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
        }

        private static bool IsStaffTransitionAllowed(string current, string next)
        {
            if (string.IsNullOrWhiteSpace(next)) return false;

            if (string.IsNullOrWhiteSpace(current) || current == "Created")
                return next == "Picking";

            return current switch
            {
                "Picking" => next == "QualityCheck",
                "QualityCheck" => next is "IssueReported" or "Packing",
                "IssueReported" => next == "Packing",
                "Packing" => next == "LoadHandover",
                _ => false
            };
        }

        private async Task EnsureStockAvailabilityAsync(int warehouseId, IEnumerable<OutboundOrderItem> items)
        {
            var productIds = items
                .Where(i => i.ProductId.HasValue)
                .Select(i => i.ProductId!.Value)
                .Distinct()
                .ToList();

            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == warehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var item in items)
            {
                if (!item.ProductId.HasValue || !item.Quantity.HasValue)
                    throw new InvalidOperationException("OutboundRequest items must specify ProductId and Quantity.");

                var inventory = inventories.FirstOrDefault(i => i.ProductId == item.ProductId);
                if (inventory == null || (inventory.Quantity ?? 0) < item.Quantity)
                {
                    var available = inventory?.Quantity ?? 0;
                    throw new InvalidOperationException($"Insufficient stock for ProductId {item.ProductId}. Available: {available}, Requested: {item.Quantity}");
                }
            }
        }

        public async Task<OutboundRequestExportDto?> GetOutboundRequestForExportAsync(int outboundRequestId)
        {
            if (outboundRequestId <= 0) return null;

            var dto = await _context.OutboundRequests
                .Where(r => r.Id == outboundRequestId)
                .Select(r => new OutboundRequestExportDto
                {
                    Id = r.Id,
                    Warehouse = r.Warehouse != null ? r.Warehouse.Name : null,
                    RequestedBy = r.RequestedByNavigation != null ? r.RequestedByNavigation.FullName : null,
                    ApprovedBy = r.ApprovedByNavigation != null ? r.ApprovedByNavigation.FullName : null,
                    Status = r.Status,
                    TotalPrice = r.TotalPrice,
                    CreatedAt = r.CreatedAt,
                    ApprovedAt = r.ApprovedAt,
                    Items = r.OutboundOrderItems.Select(i => new OutboundOrderItemExportDto
                    {
                        ProductId = i.ProductId,
                        Sku = i.Product != null ? i.Product.Sku : null,
                        Name = i.Product != null ? i.Product.Name : null,
                        Price = i.Price,
                        TypeId = i.Product != null ? i.Product.TypeId : null,
                        Description = i.Product != null ? i.Product.Description : null
                    }).ToList()
                })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return dto;
        }

        public async Task<OutboundOrderExportDto?> GetOutboundOrderForExportAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) return null;

            var dto = await _context.OutboundOrders
                .Where(o => o.Id == outboundOrderId)
                .Select(o => new OutboundOrderExportDto
                {
                    Id = o.Id,
                    Warehouse = o.Warehouse != null ? o.Warehouse.Name : null,
                    CreatedBy = o.CreatedByNavigation != null ? o.CreatedByNavigation.FullName : null,
                    Staff = o.Staff != null ? o.Staff.FullName : null,
                    Status = o.Status,
                    /*TotalPrice = o.InboundRequest != null ? o.InboundRequest.FinalPrice : null,*/
                    CreatedAt = o.CreatedAt,
                    Items = o.OutboundOrderItems.Select(i => new OutboundOrderItemExportDto
                    {
                        ProductId = i.ProductId,
                        Sku = i.Product != null ? i.Product.Sku : null,
                        Name = i.Product != null ? i.Product.Name : null,
                        Price = i.Price,
                        TypeId = i.Product != null ? i.Product.TypeId : null,
                        Description = i.Product != null ? i.Product.Description : null
                    }).ToList()
                })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return dto;
        }

        public byte[] ExportOutboundRequestToCsv(OutboundRequestExportDto request)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream, new UTF8Encoding(true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            var rows = new List<object>();

            if (request == null)
            {
                writer.Flush();
                return memoryStream.ToArray();
            }

            if (request.Items != null && request.Items.Count > 0)
            {
                foreach (var it in request.Items)
                {
                    rows.Add(new
                    {
                        RequestId = request.Id,
                        request.Code,
                        request.Warehouse,
                        request.RequestedBy,
                        request.ApprovedBy,
                        request.Status,
                        request.TotalPrice,
                        request.CreatedAt,
                        request.ApprovedAt,
                        Item_ProductId = it.ProductId,
                        Item_Sku = it.Sku,
                        Item_Name = it.Name,
                        Item_Price = it.Price,
                        Item_TypeId = it.TypeId,
                        Item_Description = it.Description
                    });
                }
            }
            else
            {
                rows.Add(new
                {
                    RequestId = request.Id,
                    request.Code,
                    request.Warehouse,
                    request.RequestedBy,
                    request.ApprovedBy,
                    request.Status,
                    request.TotalPrice,
                    request.CreatedAt,
                    request.ApprovedAt,
                    Item_ProductId = (int?)null,
                    Item_Sku = (string?)null,
                    Item_Name = (string?)null,
                    Item_Price = (double?)null,
                    Item_TypeId = (int?)null,
                    Item_Description = (string?)null
                });
            }

            csv.WriteRecords(rows);
            writer.Flush();
            return memoryStream.ToArray();
        }

        public byte[] ExportOutboundRequestToExcel(OutboundRequestExportDto request)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("OutboundRequest");

            var headers = new[]
            {
                "Request ID","Code","Warehouse","Requested By","Approved By","Status",
                "Total Price","Created At","Approved At",
                "Item ProductId","Item SKU","Item Name","Item Price","Item TypeId","Item Description"
            };

            for (int c = 0; c < headers.Length; c++)
                worksheet.Cell(1, c + 1).Value = headers[c];

            var rowIndex = 2;

            if (request != null && request.Items != null && request.Items.Count > 0)
            {
                foreach (var it in request.Items)
                {
                    worksheet.Cell(rowIndex, 1).Value = request.Id;
                    worksheet.Cell(rowIndex, 2).Value = request.Code;
                    worksheet.Cell(rowIndex, 3).Value = request.Warehouse;
                    worksheet.Cell(rowIndex, 5).Value = request.RequestedBy;
                    worksheet.Cell(rowIndex, 6).Value = request.ApprovedBy;
                    worksheet.Cell(rowIndex, 7).Value = request.Status;
                    worksheet.Cell(rowIndex, 8).Value = request.TotalPrice;
                    worksheet.Cell(rowIndex, 13).Value = request.CreatedAt;
                    worksheet.Cell(rowIndex, 14).Value = request.ApprovedAt;

                    worksheet.Cell(rowIndex, 15).Value = it.ProductId;
                    worksheet.Cell(rowIndex, 16).Value = it.Sku;
                    worksheet.Cell(rowIndex, 17).Value = it.Name;
                    worksheet.Cell(rowIndex, 18).Value = it.Price;
                    worksheet.Cell(rowIndex, 22).Value = it.TypeId;
                    worksheet.Cell(rowIndex, 23).Value = it.Description;

                    rowIndex++;
                }
            }
            else if (request != null)
            {
                worksheet.Cell(rowIndex, 1).Value = request.Id;
                worksheet.Cell(rowIndex, 2).Value = request.Code;
                worksheet.Cell(rowIndex, 3).Value = request.Warehouse;
                worksheet.Cell(rowIndex, 5).Value = request.RequestedBy;
                worksheet.Cell(rowIndex, 6).Value = request.ApprovedBy;
                worksheet.Cell(rowIndex, 7).Value = request.Status;
                worksheet.Cell(rowIndex, 8).Value = request.TotalPrice;
                worksheet.Cell(rowIndex, 13).Value = request.CreatedAt;
                worksheet.Cell(rowIndex, 14).Value = request.ApprovedAt;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] ExportOutboundOrderToCsv(OutboundOrderExportDto order)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream, new UTF8Encoding(true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            var rows = new List<object>();

            if (order == null)
            {
                writer.Flush();
                return memoryStream.ToArray();
            }

            if (order.Items != null && order.Items.Count > 0)
            {
                foreach (var it in order.Items)
                {
                    rows.Add(new
                    {
                        OrderId = order.Id,
                        order.ReferenceCode,
                        order.Warehouse,
                        order.CreatedBy,
                        order.Staff,
                        order.Status,
                        order.TotalPrice,
                        order.CreatedAt,
                        Item_ProductId = it.ProductId,
                        Item_Sku = it.Sku,
                        Item_Name = it.Name,
                        Item_Price = it.Price,
                        Item_TypeId = it.TypeId,
                        Item_Description = it.Description
                    });
                }
            }
            else
            {
                rows.Add(new
                {
                    OrderId = order.Id,
                    order.ReferenceCode,
                    order.Warehouse,
                    order.CreatedBy,
                    order.Staff,
                    order.Status,
                    order.TotalPrice,
                    order.CreatedAt,
                    Item_ProductId = (int?)null,
                    Item_Sku = (string?)null,
                    Item_Name = (string?)null,
                    Item_Price = (double?)null,
                    Item_TypeId = (int?)null,
                    Item_Description = (string?)null
                });
            }

            csv.WriteRecords(rows);
            writer.Flush();
            return memoryStream.ToArray();
        }

        public byte[] ExportOutboundOrderToExcel(OutboundOrderExportDto order)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("OutboundOrder");

            var headers = new[]
            {
                "Order ID","Reference Code","Warehouse","Created By","Staff","Status","Total Price","Created At",
                "Item ProductId","Item SKU","Item Name","Item Price","Item TypeId","Item Description"
            };

            for (int c = 0; c < headers.Length; c++)
                worksheet.Cell(1, c + 1).Value = headers[c];

            var rowIndex = 2;

            if (order != null && order.Items != null && order.Items.Count > 0)
            {
                foreach (var it in order.Items)
                {
                    worksheet.Cell(rowIndex, 1).Value = order.Id;
                    worksheet.Cell(rowIndex, 2).Value = order.ReferenceCode;
                    worksheet.Cell(rowIndex, 3).Value = order.Warehouse;
                    worksheet.Cell(rowIndex, 5).Value = order.CreatedBy;
                    worksheet.Cell(rowIndex, 6).Value = order.Staff;
                    worksheet.Cell(rowIndex, 7).Value = order.Status;
                    worksheet.Cell(rowIndex, 8).Value = order.TotalPrice;
                    worksheet.Cell(rowIndex, 9).Value = order.CreatedAt;

                    worksheet.Cell(rowIndex, 10).Value = it.ProductId;
                    worksheet.Cell(rowIndex, 11).Value = it.Sku;
                    worksheet.Cell(rowIndex, 12).Value = it.Name;
                    worksheet.Cell(rowIndex, 13).Value = it.Price;
                    worksheet.Cell(rowIndex, 17).Value = it.TypeId;
                    worksheet.Cell(rowIndex, 18).Value = it.Description;

                    rowIndex++;
                }
            }
            else if (order != null)
            {
                worksheet.Cell(rowIndex, 1).Value = order.Id;
                worksheet.Cell(rowIndex, 2).Value = order.ReferenceCode;
                worksheet.Cell(rowIndex, 3).Value = order.Warehouse;
                worksheet.Cell(rowIndex, 5).Value = order.CreatedBy;
                worksheet.Cell(rowIndex, 6).Value = order.Staff;
                worksheet.Cell(rowIndex, 7).Value = order.Status;
                worksheet.Cell(rowIndex, 8).Value = order.TotalPrice;
                worksheet.Cell(rowIndex, 9).Value = order.CreatedAt;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
