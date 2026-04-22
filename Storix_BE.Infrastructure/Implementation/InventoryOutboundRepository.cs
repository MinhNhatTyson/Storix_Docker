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
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static Storix_BE.Repository.Interfaces.IInventoryOutboundRepository;

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
        //Code này của Minh Nhật tự ý thêm để gen AI Path optimization cho việc lấy suggested location theo FIFO, không liên quan đến bất kỳ task nào trong ticket, có thể refactor hoặc remove nếu thấy không cần thiết
        public async Task<List<FifoBinSuggestionDto>> GetFifoSuggestedLocationsAsync(
    int warehouseId,
    int productId,
    int requiredQuantity)
        {
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouseId.");
            if (productId <= 0) throw new ArgumentException("Invalid productId.");
            if (requiredQuantity <= 0) throw new ArgumentException("RequiredQuantity must be > 0.");

            // Load non-exhausted batches ordered oldest first (FIFO)
            var batches = await _context.InventoryBatches
                .Include(b => b.BatchLocations)
                    .ThenInclude(bl => bl.Bin)
                        .ThenInclude(b => b.Level)
                            .ThenInclude(l => l.Shelf)
                                .ThenInclude(s => s.Zone)
                .Where(b =>
                    b.WarehouseId == warehouseId &&
                    b.ProductId == productId &&
                    !b.IsExhausted &&
                    b.RemainingQuantity > 0)
                .OrderBy(b => b.InboundDate)
                .ThenBy(b => b.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var suggestions = new List<FifoBinSuggestionDto>();
            var remainingNeeded = requiredQuantity;

            foreach (var batch in batches)
            {
                if (remainingNeeded <= 0) break;

                // Within each batch, pick from bins ordered by quantity descending
                // so we empty bins before touching partially-filled ones
                var locations = batch.BatchLocations
                    .Where(bl => bl.Quantity > 0 &&
                                 bl.Bin?.Level?.Shelf?.Zone?.WarehouseId == warehouseId)
                    .OrderByDescending(bl => bl.Quantity)
                    .ToList();

                foreach (var location in locations)
                {
                    if (remainingNeeded <= 0) break;

                    var pickQuantity = Math.Min(location.Quantity, remainingNeeded);

                    suggestions.Add(new FifoBinSuggestionDto(
                        BatchId: batch.Id,
                        InboundDate: batch.InboundDate,
                        EffectiveUnitCost: batch.EffectiveUnitCost,
                        BinId: location.BinId,
                        BinIdCode: location.Bin?.IdCode,
                        BinCode: location.Bin?.Code,
                        ShelfId: location.Bin?.Level?.ShelfId,
                        ShelfCode: location.Bin?.Level?.Shelf?.Code,
                        ZoneId: location.Bin?.Level?.Shelf?.ZoneId,
                        AvailableInBin: location.Quantity,
                        SuggestedPickQty: pickQuantity
                    ));

                    remainingNeeded -= pickQuantity;
                }
            }

            return suggestions;
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
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var warehouseId = request.WarehouseId!.Value;

            var hasActiveInventoryCount = await _context.InventoryCountsTickets
                .AsNoTracking()
                .AnyAsync(t =>
                    t.WarehouseId == warehouseId
                    && t.ExecutedDay != null
                    // If FinishedDay is null -> count still in progress; if not null -> check current time is within the executed..finished window
                    && (t.FinishedDay == null || (t.ExecutedDay <= now && t.FinishedDay >= now))
                ).ConfigureAwait(false);

            if (hasActiveInventoryCount)
                throw new InvalidOperationException("Cannot create outbound requests during an active inventory count in this warehouse.");

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

        public async Task<IReadOnlyList<IInventoryOutboundRepository.WarehouseInventoryItemDto>> GetWarehouseInventoryAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouseId.", nameof(warehouseId));

            var warehouseExistsInCompany = await _context.Warehouses
                .AsNoTracking()
                .AnyAsync(w => w.Id == warehouseId && w.CompanyId == companyId)
                .ConfigureAwait(false);

            if (!warehouseExistsInCompany)
                throw new InvalidOperationException($"Warehouse {warehouseId} does not belong to company {companyId}.");

            var rawItems = await _context.Inventories
                .AsNoTracking()
                .Where(i => i.WarehouseId == warehouseId && i.ProductId.HasValue)
                .Include(i => i.Product)
                .OrderBy(i => i.Product!.Name)
                .ThenBy(i => i.ProductId)
                .Select(i => new
                {
                    InventoryId = i.Id,
                    WarehouseId = i.WarehouseId ?? warehouseId,
                    ProductId = i.ProductId!.Value,
                    ProductName = i.Product != null ? i.Product.Name : null,
                    ProductSku = i.Product != null ? i.Product.Sku : null,
                    ProductImage = i.Product != null ? i.Product.Image : null,
                    Quantity = i.Quantity ?? 0,
                    ReservedQuantity = i.ReservedQuantity ?? 0,
                    LastUpdated = i.LastUpdated,
                    LastCountedAt = i.LastCountedAt
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var inventoryIds = rawItems.Select(i => i.InventoryId).ToList();

            var locationRows = await _context.ShelfLevelBins
                .AsNoTracking()
                .Where(b => b.InventoryId.HasValue && inventoryIds.Contains(b.InventoryId.Value))
                .Include(b => b.Level)
                    .ThenInclude(l => l!.Shelf)
                        .ThenInclude(s => s!.Zone)
                .Select(b => new
                {
                    InventoryId = b.InventoryId!.Value,
                    ZoneId = b.Level != null && b.Level.Shelf != null ? b.Level.Shelf.ZoneId : null,
                    ZoneCode = b.Level != null && b.Level.Shelf != null && b.Level.Shelf.Zone != null ? b.Level.Shelf.Zone.Code : null,
                    ShelfId = b.Level != null ? b.Level.ShelfId : null,
                    ShelfCode = b.Level != null && b.Level.Shelf != null ? b.Level.Shelf.Code : null,
                    BinId = (int?)b.Id,
                    BinCode = b.Code,
                    BinIdCode = b.IdCode,
                    Quantity = (int?)b.Percentage
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var locationsByInventory = locationRows
                .GroupBy(x => x.InventoryId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<IInventoryOutboundRepository.WarehouseInventoryLocationDto>)g
                        .Select(x => new IInventoryOutboundRepository.WarehouseInventoryLocationDto(
                            x.ZoneId,
                            x.ZoneCode,
                            x.ShelfId,
                            x.ShelfCode,
                            x.BinId,
                            x.BinCode,
                            x.BinIdCode,
                            x.Quantity ?? 0))
                        .ToList());

            var items = rawItems
                .Select(i => new IInventoryOutboundRepository.WarehouseInventoryItemDto(
                    i.InventoryId,
                    i.WarehouseId,
                    i.ProductId,
                    i.ProductName,
                    i.ProductSku,
                    i.ProductImage,
                    i.Quantity,
                    i.ReservedQuantity,
                    i.LastUpdated,
                    i.LastCountedAt,
                    locationsByInventory.TryGetValue(i.InventoryId, out var locations)
                        ? locations
                        : Array.Empty<IInventoryOutboundRepository.WarehouseInventoryLocationDto>()))
                .ToList();

            return items;
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
                    ExpectedQuantity = reqItem.Quantity,
                    ReceivedQuantity = reqItem.Quantity,
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

        public async Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<OutboundOrderItem> items, IEnumerable<IInventoryOutboundRepository.InventoryPlacementDto>? placements = null)
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

                if (incoming.ExpectedQuantity.HasValue && incoming.ExpectedQuantity < 0)
                    throw new InvalidOperationException("ExpectedQuantity cannot be negative.");

                if (incoming.ReceivedQuantity.HasValue && incoming.ReceivedQuantity < 0)
                    throw new InvalidOperationException("ReceivedQuantity cannot be negative.");

                if (incoming.Quantity == null || incoming.Quantity <= 0)
                    throw new InvalidOperationException("Each item must have Quantity > 0.");
            }

            // Persist selected bin allocations (staff picking) into ActivityLogs.
            // This does not mutate inventory; actual stock deduction happens on manager confirm.
            var placementList = (placements ?? Enumerable.Empty<IInventoryOutboundRepository.InventoryPlacementDto>())
                .Where(p => p.OutboundOrderItemId > 0 && p.ProductId > 0 && p.Quantity > 0 && !string.IsNullOrWhiteSpace(p.BinIdCode))
                .ToList();

            if (placementList.Any())
            {
                if (!order.WarehouseId.HasValue || order.WarehouseId.Value <= 0)
                    throw new InvalidOperationException("OutboundOrder must have a valid WarehouseId to assign locations.");

                var binCodes = placementList.Select(p => p.BinIdCode).Distinct().ToList();
                var bins = await _context.ShelfLevelBins
                    .Include(b => b.Level)
                        .ThenInclude(l => l!.Shelf)
                            .ThenInclude(s => s!.Zone)
                    .Where(b => b.IdCode != null && binCodes.Contains(b.IdCode))
                    .ToListAsync()
                    .ConfigureAwait(false);

                var binByCode = bins
                    .Where(b => !string.IsNullOrWhiteSpace(b.IdCode))
                    .GroupBy(b => b.IdCode!)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var p in placementList)
                {
                    if (!binByCode.TryGetValue(p.BinIdCode, out var bin))
                        throw new InvalidOperationException($"ShelfLevelBin with IdCode {p.BinIdCode} not found.");

                    var whId = bin.Level?.Shelf?.Zone?.WarehouseId;
                    if (!whId.HasValue || whId.Value != order.WarehouseId.Value)
                        throw new InvalidOperationException($"Bin {p.BinIdCode} is invalid or does not belong to outbound warehouse.");
                }

                var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                var affectedItemIds = placementList.Select(p => p.OutboundOrderItemId).Distinct().ToHashSet();
                var existingAssignmentLogs = await _context.ActivityLogs
                    .Where(l => l.Entity == "OutboundOrder"
                                && l.EntityId == order.Id
                                && l.Action != null
                                && l.Action.StartsWith("OUTBOUND_ITEM_LOCATION_ASSIGN:"))
                    .ToListAsync()
                    .ConfigureAwait(false);

                var assignmentLogsToRemove = existingAssignmentLogs
                    .Where(l =>
                    {
                        var parsed = ParseOutboundBinAssignment(l);
                        return parsed != null && affectedItemIds.Contains(parsed.OutboundOrderItemId);
                    })
                    .ToList();

                if (assignmentLogsToRemove.Any())
                    _context.ActivityLogs.RemoveRange(assignmentLogsToRemove);

                foreach (var p in placementList)
                {
                    _context.ActivityLogs.Add(new ActivityLog
                    {
                        UserId = order.StaffId.Value,
                        Entity = "OutboundOrder",
                        EntityId = order.Id,
                        Action = $"OUTBOUND_ITEM_LOCATION_ASSIGN:ORDER={order.Id};ITEM={p.OutboundOrderItemId};PRODUCT={p.ProductId};BIN={p.BinIdCode};QTY={p.Quantity}",
                        Timestamp = now
                    });
                }
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

                existing.ExpectedQuantity = incoming.ExpectedQuantity;
                existing.ReceivedQuantity = incoming.ReceivedQuantity;
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
                .Include(o => o.OutboundOrderItems)
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

            if (string.Equals(normalized, "IssueReported", StringComparison.OrdinalIgnoreCase))
            {
                var existingIssues = await GetOutboundIssuesByTicketAsync(outboundOrderId).ConfigureAwait(false);
                if (!existingIssues.Any())
                    throw new InvalidOperationException("Cannot set status to IssueReported without at least one issue record.");
            }

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var oldStatus = order.Status;

            // Staff reaches final handover step => close outbound ticket and deduct inventory.
            var finalStatus = string.Equals(normalized, "LoadHandover", StringComparison.OrdinalIgnoreCase)
                ? "Completed"
                : normalized;

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                // Deduct inventory when outbound is completed by staff flow.
                if (string.Equals(finalStatus, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    if (!order.WarehouseId.HasValue || order.WarehouseId.Value <= 0)
                        throw new InvalidOperationException("OutboundOrder must specify WarehouseId to update inventory.");

                    var orderItems = order.OutboundOrderItems
                        .Where(i => i.Id > 0 && i.ProductId.HasValue)
                        .ToList();

                    var itemGroups = order.OutboundOrderItems
                        .Where(i => i.ProductId.HasValue)
                        .GroupBy(i => i.ProductId!.Value)
                        .Select(g => new
                        {
                            ProductId = g.Key,
                            Quantity = g.Sum(x => x.Quantity ?? x.ReceivedQuantity ?? x.ExpectedQuantity ?? 0)
                        })
                        .Where(x => x.Quantity > 0)
                        .ToList();

                    if (!itemGroups.Any())
                        throw new InvalidOperationException("Outbound order has no valid items to deduct inventory.");

                    var productIds = itemGroups.Select(x => x.ProductId).Distinct().ToList();
                    var inventories = await _context.Inventories
                        .Where(i => i.WarehouseId == order.WarehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                        .ToListAsync()
                        .ConfigureAwait(false);

                    var selectedBins = await LoadOutboundBinAssignmentsAsync(order.Id, orderItems.Select(i => i.Id))
                        .ConfigureAwait(false);

                    if (selectedBins.Any())
                    {
                        var itemMap = orderItems.ToDictionary(i => i.Id);
                        var assignedItemIds = selectedBins.Select(x => x.OutboundOrderItemId).Distinct().ToHashSet();
                        var missingAssignments = itemMap.Keys.Where(id => !assignedItemIds.Contains(id)).ToList();
                        if (missingAssignments.Any())
                            throw new InvalidOperationException($"Missing selected bin assignments for outbound items: {string.Join(',', missingAssignments)}.");

                        foreach (var item in orderItems)
                        {
                            var assigned = selectedBins
                                .Where(x => x.OutboundOrderItemId == item.Id)
                                .ToList();

                            if (assigned.Any(x => x.ProductId != item.ProductId))
                                throw new InvalidOperationException($"Selected bin assignments for item {item.Id} do not match its product.");

                            var required = item.Quantity ?? item.ReceivedQuantity ?? item.ExpectedQuantity ?? 0;
                            var allocated = assigned.Sum(x => x.Quantity);
                            if (required != allocated)
                                throw new InvalidOperationException($"Selected bin quantities for item {item.Id} must equal {required}, but got {allocated}.");
                        }
                    }

                    foreach (var item in itemGroups)
                    {
                        var inventory = inventories.FirstOrDefault(i => i.ProductId == item.ProductId);
                        var available = inventory?.Quantity ?? 0;
                        if (inventory == null || available < item.Quantity)
                            throw new InvalidOperationException($"Insufficient stock for ProductId {item.ProductId}. Available: {available}, Requested: {item.Quantity}");

                        inventory.Quantity = available - item.Quantity;
                        inventory.LastUpdated = now;

                        _context.InventoryTransactions.Add(new InventoryTransaction
                        {
                            WarehouseId = order.WarehouseId,
                            ProductId = item.ProductId,
                            TransactionType = "Outbound",
                            QuantityChange = -item.Quantity,
                            ReferenceId = order.Id,
                            PerformedBy = performedBy,
                            CreatedAt = now
                        });
                    }

                    if (selectedBins.Any())
                    {
                        var binCodes = selectedBins.Select(x => x.BinIdCode).Distinct().ToList();
                        var bins = await _context.ShelfLevelBins
                            .Include(b => b.Level)
                                .ThenInclude(l => l!.Shelf)
                                    .ThenInclude(s => s!.Zone)
                            .Where(b => b.IdCode != null && binCodes.Contains(b.IdCode))
                            .ToListAsync()
                            .ConfigureAwait(false);

                        var binByCode = bins
                            .Where(b => !string.IsNullOrWhiteSpace(b.IdCode))
                            .GroupBy(b => b.IdCode!)
                            .ToDictionary(g => g.Key, g => g.First());

                        var products = await _context.Products
                            .Where(p => productIds.Contains(p.Id))
                            .ToListAsync()
                            .ConfigureAwait(false);

                        var shelfAllocations = selectedBins
                            .Select(x =>
                            {
                                if (!binByCode.TryGetValue(x.BinIdCode, out var bin))
                                    throw new InvalidOperationException($"ShelfLevelBin with IdCode {x.BinIdCode} not found.");

                                var shelfId = bin.Level?.ShelfId;
                                var warehouseId = bin.Level?.Shelf?.Zone?.WarehouseId;
                                if (!shelfId.HasValue || !warehouseId.HasValue || warehouseId.Value != order.WarehouseId.Value)
                                    throw new InvalidOperationException($"Bin {x.BinIdCode} is invalid or does not belong to outbound warehouse.");

                                return new
                                {
                                    x.ProductId,
                                    ShelfId = shelfId.Value,
                                    x.Quantity,
                                    Bin = bin
                                };
                            })
                            .ToList();

                        var shelfDeductions = shelfAllocations
                            .GroupBy(x => new { x.ProductId, x.ShelfId })
                            .Select(g => new
                            {
                                g.Key.ProductId,
                                g.Key.ShelfId,
                                Quantity = g.Sum(x => x.Quantity)
                            })
                            .ToList();

                        foreach (var deduction in shelfDeductions)
                        {
                            var inventory = inventories.First(i => i.ProductId == deduction.ProductId);
                            var invLoc = await _context.InventoryLocations
                                .FirstOrDefaultAsync(x => x.InventoryId == inventory.Id && x.ShelfId == deduction.ShelfId)
                                .ConfigureAwait(false);

                            if (invLoc == null)
                                throw new InvalidOperationException($"No stock placement found at ShelfId {deduction.ShelfId} for ProductId {deduction.ProductId}.");

                            var currentQty = invLoc.Quantity ?? 0;
                            if (currentQty < deduction.Quantity)
                                throw new InvalidOperationException($"Insufficient shelf stock for ProductId {deduction.ProductId} at ShelfId {deduction.ShelfId}. Available: {currentQty}, Requested: {deduction.Quantity}.");

                            invLoc.Quantity = currentQty - deduction.Quantity;
                            invLoc.UpdatedAt = now;
                        }

                        foreach (var assignment in selectedBins)
                        {
                            var inventory = inventories.First(i => i.ProductId == assignment.ProductId);
                            if (!binByCode.TryGetValue(assignment.BinIdCode, out var bin))
                                throw new InvalidOperationException($"ShelfLevelBin with IdCode {assignment.BinIdCode} not found.");

                            if (bin.InventoryId.HasValue && bin.InventoryId.Value != inventory.Id)
                                throw new InvalidOperationException($"Bin {assignment.BinIdCode} does not currently map to inventory {inventory.Id} for product {assignment.ProductId}.");

                            var product = products.FirstOrDefault(p => p.Id == assignment.ProductId);
                            if (product == null)
                                throw new InvalidOperationException($"Product {assignment.ProductId} not found.");

                            var percentDeduction = CalculateBinPercentageDeduction(product, bin, assignment.Quantity);
                            var currentPercent = bin.Percentage ?? 0;
                            var nextPercent = percentDeduction > 0
                                ? Math.Max(0, currentPercent - percentDeduction)
                                : currentPercent;

                            if ((inventory.Quantity ?? 0) <= 0)
                                nextPercent = 0;

                            bin.Percentage = nextPercent;
                            bin.Status = nextPercent >= 100;

                            if (nextPercent <= 0 && bin.InventoryId == inventory.Id)
                                bin.InventoryId = null;
                            else if (nextPercent > 0 && !bin.InventoryId.HasValue)
                                bin.InventoryId = inventory.Id;

                            _context.ActivityLogs.Add(new ActivityLog
                            {
                                UserId = performedBy,
                                Entity = "OutboundOrder",
                                EntityId = order.Id,
                                Action = $"OUTBOUND_LOCATION_TRANSITION:PRODUCT={assignment.ProductId};SHELF={bin.Level!.ShelfId};BIN={assignment.BinIdCode};QTY={assignment.Quantity}",
                                Timestamp = now
                            });
                        }
                        // ── FIFO Batch deduction on outbound completion ───────────────────────────
                        foreach (var item in itemGroups)
                        {
                            var remainingToDeduct = item.Quantity;

                            // Load oldest non-exhausted batches for this product/warehouse
                            var batchesToDeduct = await _context.InventoryBatches
                                .Include(b => b.BatchLocations)
                                .Where(b =>
                                    b.WarehouseId == order.WarehouseId &&
                                    b.ProductId == item.ProductId &&
                                    !b.IsExhausted &&
                                    b.RemainingQuantity > 0)
                                .OrderBy(b => b.InboundDate)
                                .ThenBy(b => b.Id)
                                .ToListAsync()
                                .ConfigureAwait(false);

                            foreach (var batch in batchesToDeduct)
                            {
                                if (remainingToDeduct <= 0) break;

                                // Resolve which bins to deduct from using selected bin assignments
                                var assignedBinCodes = selectedBins
                                    .Where(x => x.ProductId == item.ProductId)
                                    .Select(x => x.BinIdCode)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                                // Prefer deducting from bins the staff actually picked from
                                var locationsToDeduct = batch.BatchLocations
                                    .Where(bl => bl.Quantity > 0)
                                    .OrderByDescending(bl =>
                                        assignedBinCodes.Contains(bl.Bin?.IdCode ?? string.Empty) ? 1 : 0)
                                    .ThenByDescending(bl => bl.Quantity)
                                    .ToList();

                                foreach (var location in locationsToDeduct)
                                {
                                    if (remainingToDeduct <= 0) break;

                                    var deductFromLocation = Math.Min(location.Quantity, remainingToDeduct);
                                    location.Quantity -= deductFromLocation;
                                    location.UpdatedAt = now;
                                    remainingToDeduct -= deductFromLocation;
                                    batch.RemainingQuantity -= deductFromLocation;
                                }

                                // Mark batch exhausted if fully consumed
                                if (batch.RemainingQuantity <= 0)
                                {
                                    batch.RemainingQuantity = 0;
                                    batch.IsExhausted = true;
                                }

                                batch.UpdatedAt = now;
                            }

                            if (remainingToDeduct > 0)
                                _logger.LogWarning(
                                    "FIFO deduction shortfall for ProductId={ProductId}, " +
                                    "OutboundOrderId={OrderId}. Unresolved qty={Remaining}",
                                    item.ProductId, order.Id, remainingToDeduct);
                        }
                        // ── End FIFO Batch deduction ──────────────────────────────────────────────
                    }
                }

                order.Status = finalStatus;
                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            // Best-effort audit logging: outbound status update should not fail
            // just because the history table is missing/misconfigured.
            try
            {
                _context.OutboundOrderStatusHistories.Add(new OutboundOrderStatusHistory
                {
                    OutboundOrderId = order.Id,
                    OldStatus = oldStatus,
                    NewStatus = finalStatus,
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

        public async Task<IReadOnlyList<IInventoryOutboundRepository.OutboundOrderItemAvailableLocationsDto>> GetOutboundOrderItemAvailableLocationsAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));

            var order = await _context.OutboundOrders
                .AsNoTracking()
                .Include(o => o.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.WarehouseId.HasValue || order.WarehouseId.Value <= 0)
                throw new InvalidOperationException("OutboundOrder must have a valid WarehouseId.");

            var warehouseId = order.WarehouseId.Value;

            var itemInfos = order.OutboundOrderItems
                .Where(i => i.Id > 0 && i.ProductId.HasValue && (i.Quantity ?? 0) > 0)
                .Select(i => new
                {
                    OutboundOrderItemId = i.Id,
                    ProductId = i.ProductId!.Value,
                    ProductName = i.Product?.Name,
                    RequiredQuantity = i.Quantity ?? 0
                })
                .ToList();

            if (!itemInfos.Any())
                return Array.Empty<IInventoryOutboundRepository.OutboundOrderItemAvailableLocationsDto>();

            var productIds = itemInfos.Select(x => x.ProductId).Distinct().ToList();

            var inventories = await _context.Inventories
                .AsNoTracking()
                .Where(i => i.WarehouseId == warehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .Select(i => new { i.Id, ProductId = i.ProductId!.Value })
                .ToListAsync()
                .ConfigureAwait(false);

            var inventoryIdByProductId = inventories
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.First().Id);

            var inventoryIds = inventories.Select(x => x.Id).Distinct().ToList();

            var shelfStocks = await _context.InventoryLocations
                .AsNoTracking()
                .Where(il => il.InventoryId.HasValue && inventoryIds.Contains(il.InventoryId.Value))
                .Include(il => il.Shelf)
                    .ThenInclude(s => s!.Zone)
                .ToListAsync()
                .ConfigureAwait(false);

            // Only bins explicitly mapped to these inventories; note: quantity per bin is not tracked in current schema.
            var bins = await _context.ShelfLevelBins
                .AsNoTracking()
                .Where(b => b.InventoryId.HasValue && inventoryIds.Contains(b.InventoryId.Value))
                .Include(b => b.Level)
                    .ThenInclude(l => l!.Shelf)
                        .ThenInclude(s => s!.Zone)
                .ToListAsync()
                .ConfigureAwait(false);

            var shelvesByProduct = shelfStocks
                .Where(il => il.InventoryId.HasValue && il.ShelfId.HasValue && (il.Quantity ?? 0) > 0
                             && il.Shelf != null
                             && il.Shelf.Zone != null
                             && il.Shelf.Zone.WarehouseId == warehouseId)
                .GroupBy(il =>
                {
                    var invId = il.InventoryId!.Value;
                    var productId = inventories.First(x => x.Id == invId).ProductId;
                    return productId;
                })
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<IInventoryOutboundRepository.OutboundAvailableShelfDto>)g
                        .OrderByDescending(x => x.Quantity ?? 0)
                        .Select(x => new IInventoryOutboundRepository.OutboundAvailableShelfDto(
                            x.ShelfId!.Value,
                            x.Shelf!.Code,
                            x.Shelf!.IdCode,
                            x.Shelf!.ZoneId,
                            x.Shelf!.Zone!.WarehouseId,
                            x.Quantity ?? 0))
                        .ToList());

            var binsByProduct = bins
                .Where(b => b.InventoryId.HasValue
                            && b.Level != null
                            && b.Level.Shelf != null
                            && b.Level.Shelf.Zone != null
                            && b.Level.Shelf.Zone.WarehouseId == warehouseId)
                .GroupBy(b =>
                {
                    var invId = b.InventoryId!.Value;
                    var productId = inventories.First(x => x.Id == invId).ProductId;
                    return productId;
                })
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<IInventoryOutboundRepository.OutboundAvailableBinDto>)g
                        .Select(b => new IInventoryOutboundRepository.OutboundAvailableBinDto(
                            b.Id,
                            b.Code,
                            b.IdCode,
                            b.LevelId,
                            b.Level!.ShelfId,
                            b.InventoryId,
                            b.Percentage,
                            b.Width,
                            b.Height,
                            b.Length))
                        .ToList());

            var results = itemInfos.Select(ii =>
            {
                var shelves = shelvesByProduct.TryGetValue(ii.ProductId, out var s) ? s : Array.Empty<IInventoryOutboundRepository.OutboundAvailableShelfDto>();
                var availableBins = binsByProduct.TryGetValue(ii.ProductId, out var b) ? b : Array.Empty<IInventoryOutboundRepository.OutboundAvailableBinDto>();

                return new IInventoryOutboundRepository.OutboundOrderItemAvailableLocationsDto(
                    ii.OutboundOrderItemId,
                    ii.ProductId,
                    ii.ProductName,
                    ii.RequiredQuantity,
                    shelves,
                    availableBins
                );
            }).ToList();

            return results;
        }

        public async Task<IReadOnlyList<IInventoryOutboundRepository.OutboundOrderItemSelectedLocationDto>> GetOutboundOrderItemSelectedLocationsAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));

            // Validate order exists early for consistent 404 behavior.
            var exists = await _context.OutboundOrders
                .AsNoTracking()
                .AnyAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (!exists)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            var assignments = await LoadOutboundBinAssignmentsAsync(outboundOrderId).ConfigureAwait(false);
            return assignments
                .Select(a => new IInventoryOutboundRepository.OutboundOrderItemSelectedLocationDto(
                    a.OutboundOrderItemId,
                    a.ProductId,
                    a.BinIdCode,
                    a.Quantity,
                    a.Timestamp))
                .ToList();
        }

        public async Task<IInventoryOutboundRepository.OutboundIssueDto> CreateOutboundIssueAsync(int outboundOrderId, int reportedBy, int outboundOrderItemId, int issueQuantity, string reason, string? note, string? imageUrl)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (reportedBy <= 0) throw new ArgumentException("Invalid reportedBy.", nameof(reportedBy));
            if (outboundOrderItemId <= 0) throw new ArgumentException("Invalid outboundOrderItemId.", nameof(outboundOrderItemId));
            if (issueQuantity <= 0) throw new ArgumentException("IssueQuantity must be > 0.", nameof(issueQuantity));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required.", nameof(reason));

            var order = await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.StaffId.HasValue || order.StaffId.Value != reportedBy)
                throw new InvalidOperationException("Only assigned staff can create outbound issues.");

            await EnsureStaffAssignedToWarehouseAsync(order.WarehouseId ?? 0, reportedBy).ConfigureAwait(false);

            var item = order.OutboundOrderItems.FirstOrDefault(i => i.Id == outboundOrderItemId);
            if (item == null)
                throw new InvalidOperationException($"OutboundOrderItem with id {outboundOrderItemId} not found in ticket {outboundOrderId}.");

            if (!item.ProductId.HasValue || item.ProductId.Value <= 0)
                throw new InvalidOperationException("OutboundOrderItem must have a valid ProductId.");

            var maxQty = item.Quantity ?? 0;
            if (maxQty <= 0)
                throw new InvalidOperationException("OutboundOrderItem quantity is invalid.");

            if (issueQuantity > maxQty)
                throw new InvalidOperationException($"Issue quantity cannot exceed item quantity ({maxQty}).");

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var issueId = Guid.NewGuid().ToString("N");
            var action = $"OUTBOUND_ISSUE_CREATE:ISSUE_ID={issueId};ORDER={outboundOrderId};ITEM={outboundOrderItemId};PRODUCT={item.ProductId.Value};QTY={issueQuantity};REASON={ToBase64(reason.Trim())};NOTE={ToBase64(note)};IMAGE={ToBase64(imageUrl)};REPORTED_BY={reportedBy}";

            _context.ActivityLogs.Add(new ActivityLog
            {
                UserId = reportedBy,
                Entity = "OutboundOrderIssue",
                EntityId = outboundOrderId,
                Action = action,
                Timestamp = now
            });

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return new IInventoryOutboundRepository.OutboundIssueDto(
                ParseIssueId(issueId.ToString()),
                outboundOrderId,
                outboundOrderItemId,
                item.ProductId.Value,
                issueQuantity,
                reason.Trim(),
                string.IsNullOrWhiteSpace(note) ? null : note,
                string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
                reportedBy,
                now,
                null,
                null);
        }

        public async Task<IInventoryOutboundRepository.OutboundIssueDto> UpdateOutboundIssueAsync(int outboundOrderId, int issueId, int updatedBy, int? outboundOrderItemId, int? issueQuantity, string? reason, string? note, string? imageUrl)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (issueId <= 0) throw new ArgumentException("Invalid issueId.", nameof(issueId));
            if (updatedBy <= 0) throw new ArgumentException("Invalid updatedBy.", nameof(updatedBy));

            var issueKey = issueId.ToString();
            var order = await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.StaffId.HasValue || order.StaffId.Value != updatedBy)
                throw new InvalidOperationException("Only assigned staff can update outbound issues.");

            await EnsureStaffAssignedToWarehouseAsync(order.WarehouseId ?? 0, updatedBy).ConfigureAwait(false);

            var existing = (await GetOutboundIssuesByTicketAsync(outboundOrderId).ConfigureAwait(false))
                .FirstOrDefault(x => x.IssueId == issueId);

            if (existing == null)
                throw new InvalidOperationException($"Issue {issueId} not found in ticket {outboundOrderId}.");

            var targetItemId = outboundOrderItemId ?? existing.OutboundOrderItemId;
            var item = order.OutboundOrderItems.FirstOrDefault(i => i.Id == targetItemId);
            if (item == null)
                throw new InvalidOperationException($"OutboundOrderItem with id {targetItemId} not found in ticket {outboundOrderId}.");

            if (!item.ProductId.HasValue || item.ProductId.Value <= 0)
                throw new InvalidOperationException("OutboundOrderItem must have a valid ProductId.");

            var targetQty = issueQuantity ?? existing.IssueQuantity;
            if (targetQty <= 0)
                throw new InvalidOperationException("IssueQuantity must be > 0.");

            var maxQty = item.Quantity ?? 0;
            if (targetQty > maxQty)
                throw new InvalidOperationException($"Issue quantity cannot exceed item quantity ({maxQty}).");

            var targetReason = string.IsNullOrWhiteSpace(reason) ? existing.Reason : reason.Trim();
            var targetNote = note ?? existing.Note;
            var targetImage = imageUrl ?? existing.ImageUrl;

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var action = $"OUTBOUND_ISSUE_UPDATE:ISSUE_ID={issueKey};ORDER={outboundOrderId};ITEM={targetItemId};PRODUCT={item.ProductId.Value};QTY={targetQty};REASON={ToBase64(targetReason)};NOTE={ToBase64(targetNote)};IMAGE={ToBase64(targetImage)};UPDATED_BY={updatedBy}";

            _context.ActivityLogs.Add(new ActivityLog
            {
                UserId = updatedBy,
                Entity = "OutboundOrderIssue",
                EntityId = outboundOrderId,
                Action = action,
                Timestamp = now
            });

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return new IInventoryOutboundRepository.OutboundIssueDto(
                issueId,
                outboundOrderId,
                targetItemId,
                item.ProductId.Value,
                targetQty,
                targetReason,
                targetNote,
                targetImage,
                existing.ReportedBy,
                existing.ReportedAt,
                updatedBy,
                now);
        }

        public async Task<List<IInventoryOutboundRepository.OutboundIssueDto>> GetOutboundIssuesByTicketAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));

            var exists = await _context.OutboundOrders
                .AsNoTracking()
                .AnyAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (!exists)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            var logs = await _context.ActivityLogs
                .AsNoTracking()
                .Where(l => l.Entity == "OutboundOrderIssue"
                            && l.EntityId == outboundOrderId
                            && l.Action != null
                            && (l.Action.StartsWith("OUTBOUND_ISSUE_CREATE:") || l.Action.StartsWith("OUTBOUND_ISSUE_UPDATE:")))
                .OrderBy(l => l.Timestamp)
                .ToListAsync()
                .ConfigureAwait(false);

            var map = new Dictionary<int, IInventoryOutboundRepository.OutboundIssueDto>();

            foreach (var log in logs)
            {
                var action = log.Action;
                if (string.IsNullOrWhiteSpace(action)) continue;

                var idx = action.IndexOf(':');
                if (idx < 0 || idx >= action.Length - 1) continue;

                var kind = action.Substring(0, idx);
                var payload = action.Substring(idx + 1);
                var parts = payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var kvMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (kv.Length == 2) kvMap[kv[0]] = kv[1];
                }

                if (!kvMap.TryGetValue("ISSUE_ID", out var issueIdRaw)) continue;
                var parsedIssueId = ParseIssueId(issueIdRaw);
                if (parsedIssueId <= 0) continue;

                if (!kvMap.TryGetValue("ITEM", out var itemRaw) || !int.TryParse(itemRaw, out var itemId) || itemId <= 0) continue;
                if (!kvMap.TryGetValue("PRODUCT", out var productRaw) || !int.TryParse(productRaw, out var productId) || productId <= 0) continue;
                if (!kvMap.TryGetValue("QTY", out var qtyRaw) || !int.TryParse(qtyRaw, out var qty) || qty <= 0) continue;
                if (!kvMap.TryGetValue("REASON", out var reasonEncoded)) continue;

                var reasonDecoded = FromBase64(reasonEncoded);
                if (string.IsNullOrWhiteSpace(reasonDecoded)) continue;

                var noteDecoded = kvMap.TryGetValue("NOTE", out var noteEncoded) ? FromBase64(noteEncoded) : null;
                var imageDecoded = kvMap.TryGetValue("IMAGE", out var imageEncoded) ? FromBase64(imageEncoded) : null;

                if (string.Equals(kind, "OUTBOUND_ISSUE_CREATE", StringComparison.OrdinalIgnoreCase))
                {
                    var reportedBy = (kvMap.TryGetValue("REPORTED_BY", out var rbRaw) && int.TryParse(rbRaw, out var rb) && rb > 0)
                        ? rb
                        : (log.UserId ?? 0);

                    if (reportedBy <= 0) continue;

                    map[parsedIssueId] = new IInventoryOutboundRepository.OutboundIssueDto(
                        parsedIssueId,
                        outboundOrderId,
                        itemId,
                        productId,
                        qty,
                        reasonDecoded,
                        noteDecoded,
                        imageDecoded,
                        reportedBy,
                        log.Timestamp,
                        null,
                        null);
                }
                else if (string.Equals(kind, "OUTBOUND_ISSUE_UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    if (!map.TryGetValue(parsedIssueId, out var old)) continue;

                    var updatedBy = (kvMap.TryGetValue("UPDATED_BY", out var ubRaw) && int.TryParse(ubRaw, out var ub) && ub > 0)
                        ? ub
                        : log.UserId;

                    map[parsedIssueId] = old with
                    {
                        OutboundOrderItemId = itemId,
                        ProductId = productId,
                        IssueQuantity = qty,
                        Reason = reasonDecoded,
                        Note = noteDecoded,
                        ImageUrl = imageDecoded,
                        UpdatedBy = updatedBy,
                        UpdatedAt = log.Timestamp
                    };
                }
            }

            return map.Values.OrderByDescending(x => x.UpdatedAt ?? x.ReportedAt).ToList();
        }

        public async Task<IInventoryOutboundRepository.OutboundPathOptimizationDto> SaveOutboundPathOptimizationAsync(int outboundOrderId, int savedBy, string payloadJson)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (savedBy <= 0) throw new ArgumentException("Invalid savedBy.", nameof(savedBy));
            if (string.IsNullOrWhiteSpace(payloadJson)) throw new ArgumentException("Payload is required.", nameof(payloadJson));

            var order = await _context.OutboundOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.WarehouseId.HasValue || order.WarehouseId.Value <= 0)
                throw new InvalidOperationException("OutboundOrder must have a valid WarehouseId.");

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == savedBy)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {savedBy} not found.");

            var payloadChecksum = ComputeSha256Hex(payloadJson);

            var latestLog = await _context.ActivityLogs
                .AsNoTracking()
                .Where(l => l.Entity == "OutboundPathOptimization"
                            && l.EntityId == outboundOrderId
                            && l.Action != null
                            && l.Action.StartsWith("OUTBOUND_PATH_OPTIMIZATION_SAVE:"))
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (latestLog != null && !string.IsNullOrWhiteSpace(latestLog.Action))
            {
                var idxLatest = latestLog.Action.IndexOf(':');
                if (idxLatest >= 0 && idxLatest < latestLog.Action.Length - 1)
                {
                    var payloadLatest = latestLog.Action.Substring(idxLatest + 1);
                    var partsLatest = payloadLatest.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var kvLatest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var part in partsLatest)
                    {
                        var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                        if (kv.Length == 2) kvLatest[kv[0]] = kv[1];
                    }

                    if (kvLatest.TryGetValue("CHECKSUM", out var latestChecksum)
                        && string.Equals(latestChecksum, payloadChecksum, StringComparison.OrdinalIgnoreCase))
                    {
                        var latestSavedBy = (kvLatest.TryGetValue("SAVED_BY", out var lsbRaw) && int.TryParse(lsbRaw, out var lsb) && lsb > 0)
                            ? lsb
                            : (latestLog.UserId ?? savedBy);

                        return new IInventoryOutboundRepository.OutboundPathOptimizationDto(
                            outboundOrderId,
                            payloadJson,
                            latestSavedBy,
                            latestLog.Timestamp);
                    }
                }
            }

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var action = $"OUTBOUND_PATH_OPTIMIZATION_SAVE:ORDER={outboundOrderId};CHECKSUM={payloadChecksum};PAYLOAD={ToBase64(payloadJson)};SAVED_BY={savedBy}";

            _context.ActivityLogs.Add(new ActivityLog
            {
                UserId = savedBy,
                Entity = "OutboundPathOptimization",
                EntityId = outboundOrderId,
                Action = action,
                Timestamp = now
            });

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return new IInventoryOutboundRepository.OutboundPathOptimizationDto(
                outboundOrderId,
                payloadJson,
                savedBy,
                now);
        }

        public async Task<IInventoryOutboundRepository.OutboundPathOptimizationDto?> GetOutboundPathOptimizationByTicketAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));

            var order = await _context.OutboundOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            var log = await _context.ActivityLogs
                .AsNoTracking()
                .Where(l => l.Entity == "OutboundPathOptimization"
                            && l.EntityId == outboundOrderId
                            && l.Action != null
                            && l.Action.StartsWith("OUTBOUND_PATH_OPTIMIZATION_SAVE:"))
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (log == null || string.IsNullOrWhiteSpace(log.Action))
                return null;

            var idx = log.Action.IndexOf(':');
            if (idx < 0 || idx >= log.Action.Length - 1)
                return null;

            var payload = log.Action.Substring(idx + 1);
            var parts = payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var kvMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2) kvMap[kv[0]] = kv[1];
            }

            if (!kvMap.TryGetValue("PAYLOAD", out var encodedPayload))
                return null;

            var json = FromBase64(encodedPayload);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var savedBy = (kvMap.TryGetValue("SAVED_BY", out var savedByRaw) && int.TryParse(savedByRaw, out var sb) && sb > 0)
                ? sb
                : (log.UserId ?? 0);

            if (savedBy <= 0)
                return null;

            return new IInventoryOutboundRepository.OutboundPathOptimizationDto(
                outboundOrderId,
                json,
                savedBy,
                log.Timestamp);
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

        private sealed record OutboundBinAssignmentState(
            int OutboundOrderItemId,
            int ProductId,
            string BinIdCode,
            int Quantity,
            DateTime? Timestamp);

        private static OutboundBinAssignmentState? ParseOutboundBinAssignment(ActivityLog log)
        {
            if (log == null || string.IsNullOrWhiteSpace(log.Action) || !log.Action.StartsWith("OUTBOUND_ITEM_LOCATION_ASSIGN:", StringComparison.OrdinalIgnoreCase))
                return null;

            var idx = log.Action.IndexOf(':');
            if (idx < 0 || idx >= log.Action.Length - 1)
                return null;

            var payload = log.Action.Substring(idx + 1);
            var parts = payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            int itemId = 0;
            int productId = 0;
            int qty = 0;
            string? binIdCode = null;

            foreach (var part in parts)
            {
                var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2) continue;

                if (kv[0].Equals("ITEM", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(kv[1], out itemId);
                else if (kv[0].Equals("PRODUCT", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(kv[1], out productId);
                else if (kv[0].Equals("BIN", StringComparison.OrdinalIgnoreCase))
                    binIdCode = kv[1];
                else if (kv[0].Equals("QTY", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(kv[1], out qty);
            }

            if (itemId <= 0 || productId <= 0 || qty <= 0 || string.IsNullOrWhiteSpace(binIdCode))
                return null;

            return new OutboundBinAssignmentState(itemId, productId, binIdCode, qty, log.Timestamp);
        }

        private async Task<List<OutboundBinAssignmentState>> LoadOutboundBinAssignmentsAsync(int outboundOrderId, IEnumerable<int>? itemIds = null)
        {
            var logs = await _context.ActivityLogs
                .AsNoTracking()
                .Where(l => l.Entity == "OutboundOrder"
                            && l.EntityId == outboundOrderId
                            && l.Action != null
                            && l.Action.StartsWith("OUTBOUND_ITEM_LOCATION_ASSIGN:"))
                .OrderBy(l => l.Timestamp)
                .ToListAsync()
                .ConfigureAwait(false);

            var itemIdSet = itemIds?
                .Where(id => id > 0)
                .ToHashSet();

            return logs
                .Select(ParseOutboundBinAssignment)
                .Where(x => x != null)
                .Select(x => x!)
                .Where(x => itemIdSet == null || itemIdSet.Contains(x.OutboundOrderItemId))
                .GroupBy(x => new { x.OutboundOrderItemId, x.ProductId, x.BinIdCode })
                .Select(g => new OutboundBinAssignmentState(
                    g.Key.OutboundOrderItemId,
                    g.Key.ProductId,
                    g.Key.BinIdCode,
                    g.Sum(x => x.Quantity),
                    g.Max(x => x.Timestamp)))
                .Where(x => x.Quantity > 0)
                .ToList();
        }

        private static double CalculateProductUnitVolume(Product product)
        {
            var width = product.Width ?? 0.0;
            var height = product.Height ?? 0.0;
            var length = product.Length ?? 0.0;

            if (width <= 0 && height <= 0 && length <= 0)
                return 0.0;

            if (width <= 0) return height * length;
            if (height <= 0) return width * length;
            if (length <= 0) return width * height;
            return width * height * length;
        }

        private static int CalculateBinPercentageDeduction(Product product, ShelfLevelBin bin, int quantity)
        {
            if (quantity <= 0) return 0;

            var productUnitVolume = CalculateProductUnitVolume(product);
            var binVolume = (bin.Width ?? 0.0) * (bin.Height ?? 0.0) * (bin.Length ?? 0.0);
            if (productUnitVolume <= 0 || binVolume <= 0)
                return 0;

            var percent = (productUnitVolume * quantity / binVolume) * 100.0;
            if (percent <= 0) return 0;

            return Math.Max(1, (int)Math.Ceiling(percent));
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

        public async Task<List<OutboundRequest>> GetOutboundRequestsByWarehouseIdAsync(int warehouseId)
        {
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            return await _context.OutboundRequests
                .Include(r => r.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r => r.WarehouseId == warehouseId)
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

        public async Task<List<OutboundOrder>> GetOutboundOrdersByWarehouseIdAsync(int warehouseId)
        {
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            return await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Where(o => o.WarehouseId == warehouseId)
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

        private static string ToBase64(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string ComputeSha256Hex(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        private static string? FromBase64(string? encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) return null;
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        private static int ParseIssueId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            if (int.TryParse(raw, out var parsed) && parsed > 0) return parsed;

            unchecked
            {
                var hash = 23;
                foreach (var ch in raw)
                {
                    hash = (hash * 31) + ch;
                }
                return Math.Abs(hash);
            }
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

            var allowedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Picking",
                "QualityCheck",
                "IssueReported",
                "Packing",
                "LoadHandover"
            };

            // Keep initial guard: from Created (or empty) staff must start with Picking.
            if (string.IsNullOrWhiteSpace(current) || current == "Created")
                return string.Equals(next, "Picking", StringComparison.OrdinalIgnoreCase);

            // Allow both forward and backward transitions among staff workflow statuses.
            // Example: QualityCheck -> Picking is valid.
            return allowedStatuses.Contains(current) && allowedStatuses.Contains(next);
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
