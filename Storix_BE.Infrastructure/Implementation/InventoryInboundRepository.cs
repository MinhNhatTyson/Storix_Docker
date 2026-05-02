using ClosedXML.Excel;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static Storix_BE.Repository.Interfaces.IInventoryInboundRepository;

namespace Storix_BE.Repository.Implementation
{
    public class InventoryInboundRepository : IInventoryInboundRepository
    {
        private readonly StorixDbContext _context;

        public InventoryInboundRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<InboundRequest> CreateInventoryInboundTicketRequest(InboundRequest request, IEnumerable<ProductPrice>? productPrices = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Request must contain at least one product item
            if (request.InboundOrderItems == null || !request.InboundOrderItems.Any())
            {
                throw new InvalidOperationException("InboundRequest must contain at least one InboundOrderItem describing product and expected quantity.");
            }

            // Basic per-item validation
            var invalidItem = request.InboundOrderItems.FirstOrDefault(i => i.ProductId == null || i.ExpectedQuantity == null || i.ExpectedQuantity <= 0);
            if (invalidItem != null)
            {
                throw new InvalidOperationException("All InboundOrderItems must specify a ProductId and ExpectedQuantity > 0.");
            }

            // Verify products exist
            var productIds = request.InboundOrderItems.Select(i => i.ProductId!.Value).Distinct().ToList();
            var existingProductIds = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var missing = productIds.Except(existingProductIds).ToList();
            if (missing.Any())
            {
                throw new InvalidOperationException($"Products not found: {string.Join(',', missing)}");
            }

            // Optional: validate referenced warehouse/supplier/requestedBy exist when provided
            if (request.WarehouseId.HasValue)
            {
                var wh = await _context.Warehouses.FindAsync(request.WarehouseId.Value).ConfigureAwait(false);
                if (wh == null) throw new InvalidOperationException($"Warehouse with id {request.WarehouseId.Value} not found.");
            }
            if (request.SupplierId.HasValue)
            {
                var sup = await _context.Suppliers.FindAsync(request.SupplierId.Value).ConfigureAwait(false);
                if (sup == null) throw new InvalidOperationException($"Supplier with id {request.SupplierId.Value} not found.");
            }

            // Set defaults
            request.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            if (string.IsNullOrWhiteSpace(request.Status))
            {
                request.Status = "Pending";
            }

            // Ensure child items are correctly linked to the request
            foreach (var item in request.InboundOrderItems)
            {
                item.InboundRequest = request;
            }

            // Persist within a transaction — also persist productPrices if provided
            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.InboundRequests.Add(request);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                if (productPrices != null)
                {
                    var pricesList = productPrices.ToList();
                    if (pricesList.Any())
                    {
                        // Ensure Date is set
                        var nowDate = DateOnly.FromDateTime(DateTime.UtcNow);
                        foreach (var p in pricesList)
                        {
                            if (p.Date == null)
                                p.Date = nowDate;
                        }

                        _context.ProductPrices.AddRange(pricesList);
                        await _context.SaveChangesAsync().ConfigureAwait(false);
                    }
                }

                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
            return request;
        }

        public async Task<InboundRequest> UpdateInventoryInboundTicketRequestStatus(int ticketRequestId, int approverId, string status)
        {
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var inbound = await _context.InboundRequests
                .Include(r => r.RequestedByNavigation) // <- ensure company info available via RequestedByNavigation.CompanyId
                .FirstOrDefaultAsync(r => r.Id == ticketRequestId)
                .ConfigureAwait(false);

            if (inbound == null)
            {
                throw new InvalidOperationException($"InboundRequest with id {ticketRequestId} not found.");
            }

            inbound.Status = status;
            inbound.ApprovedBy = approverId;
            inbound.ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return inbound;
        }
        public async Task<InboundOrder> CreateInboundOrderFromRequestAsync(int inboundRequestId, int createdBy, int? staffId)
        {
            var inboundRequest = await _context.InboundRequests
                .Include(r => r.InboundOrderItems)
                .FirstOrDefaultAsync(r => r.Id == inboundRequestId)
                .ConfigureAwait(false);

            if (inboundRequest == null)
                throw new InvalidOperationException($"InboundRequest with id {inboundRequestId} not found.");

            if (!string.Equals(inboundRequest.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("InboundRequest must be in 'Approved' status to create an InboundOrder (ticket).");

            // Build new InboundOrder inheriting fields except CreatedAt, Status, CreatedBy
            var inboundOrder = new InboundOrder
            {
                WarehouseId = inboundRequest.WarehouseId,
                SupplierId = inboundRequest.SupplierId,
                CreatedBy = createdBy,
                StaffId = staffId,
                Status = "Waiting for payment",
                InboundRequestId = inboundRequest.Id,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                ReferenceCode = $"INB-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}"
            };

            foreach (var reqItem in inboundRequest.InboundOrderItems)
            {
                reqItem.InboundOrder = inboundOrder;
                inboundOrder.InboundOrderItems.Add(reqItem);
            }

            var batchByProduct = new Dictionary<int, InventoryBatch>();
            // Create skeleton InventoryBatch per product, linked to its InboundOrderItem (reuse reqItem instances)
            foreach (var reqItem in inboundRequest.InboundOrderItems)
            {
                if (!reqItem.ProductId.HasValue) continue;

                // One batch per InboundOrderItem (not per product) since each item
                // gets its own FK reference - deduplication happens at the product
                // level during FIFO queries via InboundDate ordering
                var batch = new InventoryBatch
                {
                    // Link via navigation so EF resolves the FK after SaveChanges
                    InboundOrderItem = reqItem,
                    InboundOrder = inboundOrder,
                    ProductId = reqItem.ProductId.Value,
                    WarehouseId = inboundRequest.WarehouseId!.Value,
                    ReceivedQuantity = 0,
                    RemainingQuantity = 0,
                    UnitCost = (decimal)(reqItem.Price ?? 0),
                    LineDiscount = (decimal)(reqItem.Discount ?? 0),
                    InboundDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    IsExhausted = false,
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };

                inboundOrder.InventoryBatches.Add(batch);
            }

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.InboundOrders.Add(inboundOrder);
                inboundRequest.Status = "Transported";
                await _context.SaveChangesAsync().ConfigureAwait(false);

                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return inboundOrder;
        }

        public async Task<InboundOrder> UpdateInboundOrderItemsAsync(int inboundOrderId, IEnumerable<InboundOrderItem> items, IEnumerable<InventoryPlacementDto>? placements = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var order = await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == inboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"InboundOrder with id {inboundOrderId} not found.");

            if (!order.WarehouseId.HasValue)
                throw new InvalidOperationException("InboundOrder must specify WarehouseId to update inventory.");

            foreach (var i in items)
            {
                if (i.ProductId == null || i.ProductId <= 0)
                    throw new InvalidOperationException("Each item must have a valid ProductId.");
                if (i.ExpectedQuantity.HasValue && i.ExpectedQuantity < 0)
                    throw new InvalidOperationException("ExpectedQuantity cannot be negative.");
                if (i.ReceivedQuantity.HasValue && i.ReceivedQuantity < 0)
                    throw new InvalidOperationException("ReceivedQuantity cannot be negative.");
            }

            var incomingList = items.ToList();
            var productIds = incomingList.Select(i => i.ProductId!.Value).Distinct().ToList();

            // load existing inventories for warehouse/product combination
            var inventories = await _context.Inventories
                .Where(inv => inv.WarehouseId == order.WarehouseId && inv.ProductId.HasValue && productIds.Contains(inv.ProductId.Value))
                .ToListAsync()
                .ConfigureAwait(false);

            // load product metadata needed for bin occupancy calculation
            var products = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync()
                .ConfigureAwait(false);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            var placementList = (placements ?? Enumerable.Empty<InventoryPlacementDto>()).ToList();
            var binIdCodes = placementList.Select(p => p.BinIdCode).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

            // Preload bins (with level -> shelf -> zone to validate warehouse)
            var bins = new List<ShelfLevelBin>();
            if (binIdCodes.Any())
            {
                bins = await _context.ShelfLevelBins
                    .Include(b => b.Level!)
                        .ThenInclude(l => l.Shelf!)
                            .ThenInclude(s => s.Zone!)
                    .Where(b => binIdCodes.Contains(b.IdCode))
                    .ToListAsync()
                    .ConfigureAwait(false);

                var missingBins = binIdCodes.Except(bins.Select(b => b.IdCode)).ToList();
                if (missingBins.Any())
                    throw new InvalidOperationException($"ShelfLevelBins not found for IdCodes: {string.Join(',', missingBins)}");

                // Validate bins belong to the same warehouse as the order
                var invalidBins = bins.Where(b =>
                    (b.Level?.Shelf?.Zone?.WarehouseId ?? null) != order.WarehouseId).ToList();
                if (invalidBins.Any())
                    throw new InvalidOperationException($"One or more provided bins do not belong to the order warehouse.");
            }

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                var deltas = new List<(int InboundOrderItemId, int ProductId, int Delta)>();

                // compute deltas per inbound order item and ensure placements totals match deltas when provided
                foreach (var incoming in incomingList)
                {
                    InboundOrderItem? existing = null;

                    if (incoming.Id > 0)
                    {
                        existing = order.InboundOrderItems.FirstOrDefault(x => x.Id == incoming.Id);
                        if (existing == null)
                            throw new InvalidOperationException($"InboundOrderItem with id {incoming.Id} not found in order {inboundOrderId}.");
                    }
                    else
                    {
                        existing = order.InboundOrderItems.FirstOrDefault(x => x.ProductId == incoming.ProductId);
                    }

                    var previousReceived = existing?.ReceivedQuantity ?? 0;
                    var newReceived = incoming.ReceivedQuantity ?? 0;
                    var delta = newReceived - previousReceived;

                    if (existing != null)
                    {
                        existing.ExpectedQuantity = incoming.ExpectedQuantity;
                        existing.ReceivedQuantity = incoming.ReceivedQuantity;
                        existing.ProductId = incoming.ProductId;
                    }
                    else
                    {
                        var newItem = new InboundOrderItem
                        {
                            ProductId = incoming.ProductId,
                            ExpectedQuantity = incoming.ExpectedQuantity,
                            ReceivedQuantity = incoming.ReceivedQuantity,
                            InboundOrder = order
                        };
                        order.InboundOrderItems.Add(newItem);
                    }

                    if (delta != 0)
                        deltas.Add((incoming.Id, incoming.ProductId!.Value, delta));
                }

                // Validate placements sums match positive deltas (if placements provided)
                var placementsByInbound = placementList.GroupBy(p => p.InboundOrderItemId).ToDictionary(g => g.Key, g => g.ToList());
                foreach (var (inboundItemId, _, delta) in deltas)
                {
                    if (delta > 0 && placementsByInbound.TryGetValue(inboundItemId, out var plist))
                    {
                        var sumAssigned = plist.Sum(p => p.Quantity);
                        if (sumAssigned != delta)
                            throw new InvalidOperationException($"Sum of placement quantities ({sumAssigned}) does not match received delta ({delta}) for inbound item {inboundItemId}.");
                    }
                }

                // apply inventory quantity changes and create transactions; also process placements -> inventory locations and bins occupancy
                foreach (var (inboundItemId, productId, delta) in deltas)
                {
                    var inventory = inventories.FirstOrDefault(i => i.ProductId == productId);

                    if (inventory == null)
                    {
                        if (delta < 0)
                            throw new InvalidOperationException($"Insufficient stock for ProductId {productId}. Available: 0, Required reduction: {-delta}");

                        inventory = new Inventory
                        {
                            WarehouseId = order.WarehouseId,
                            ProductId = productId,
                            Quantity = delta,
                            LastUpdated = now
                        };
                        _context.Inventories.Add(inventory);
                        inventories.Add(inventory);
                    }
                    else
                    {
                        var newQty = (inventory.Quantity ?? 0) + delta;
                        if (newQty < 0)
                        {
                            var available = inventory.Quantity ?? 0;
                            throw new InvalidOperationException($"Insufficient stock for ProductId {productId}. Available: {available}, Required reduction: {-delta}");
                        }

                        inventory.Quantity = newQty;
                        inventory.LastUpdated = now;
                    }

                    // create inventory transaction
                    var transaction = new InventoryTransaction
                    {
                        WarehouseId = order.WarehouseId,
                        ProductId = productId,
                        TransactionType = "Inbound",
                        QuantityChange = delta,
                        ReferenceId = order.Id,
                        PerformedBy = order.StaffId ?? order.CreatedBy,
                        CreatedAt = now
                    };
                    _context.InventoryTransactions.Add(transaction);

                    // If placements provided for this inbound item, update InventoryLocation and ShelfLevelBin
                    if (placementList.Any())
                    {
                        var itemPlacements = placementList.Where(p => p.ProductId == productId && p.InboundOrderItemId == inboundItemId).ToList();
                        foreach (var p in itemPlacements)
                        {
                            var bin = bins.FirstOrDefault(b => b.IdCode == p.BinIdCode);
                            if (bin == null)
                                throw new InvalidOperationException($"ShelfLevelBin with IdCode {p.BinIdCode} not found.");

                            // Ensure inventory exists (it should after the update above)
                            var inv = inventories.First(i => i.ProductId == productId);

                            // Link bin to inventory (assign bin to this product inventory)
                            bin.InventoryId = inv.Id;

                            // Find shelf id (bin -> level -> shelf)
                            var shelf = bin.Level?.Shelf;
                            if (shelf == null)
                                throw new InvalidOperationException($"Shelf for bin {bin.IdCode} not found.");

                            // Update or create InventoryLocation for this inventory/shelf
                            var invLoc = await _context.InventoryLocations
                                .FirstOrDefaultAsync(il => il.InventoryId == inv.Id && il.ShelfId == shelf.Id)
                                .ConfigureAwait(false);

                            if (invLoc == null)
                            {
                                invLoc = new InventoryLocation
                                {
                                    Inventory = inv,
                                    ShelfId = shelf.Id,
                                    Quantity = p.Quantity,
                                    UpdatedAt = now
                                };
                                _context.InventoryLocations.Add(invLoc);
                            }
                            else
                            {
                                invLoc.Quantity = (invLoc.Quantity ?? 0) + p.Quantity;
                                invLoc.UpdatedAt = now;
                            }
                        }
                    }
                }

                // Recompute bin percentage occupancy for affected bins
                if (placementList.Any())
                {
                    var affectedBinCodes = placementList.Select(p => p.BinIdCode).Distinct().ToList();
                    foreach (var code in affectedBinCodes)
                    {
                        var bin = bins.First(b => b.IdCode == code);
                        // calculate total occupied volume in this bin across all placements (may include different products)
                        double totalOccupiedVolume = 0.0;
                        var assignmentsForBin = placementList.Where(pl => pl.BinIdCode == code).ToList();
                        foreach (var a in assignmentsForBin)
                        {
                            var prod = products.FirstOrDefault(p => p.Id == a.ProductId);
                            if (prod == null) continue;
                            var pw = prod.Width ?? 0.0;
                            var ph = prod.Height ?? 0.0;
                            var plength = prod.Length ?? 0.0;
                            double productUnitVolume = 0;
                            if (pw <= 0)
                            {
                                productUnitVolume = ph * plength;
                            }
                            else if (ph <= 0)
                            {
                                productUnitVolume = pw * plength;
                            }
                            else if (plength <= 0)
                            {
                                productUnitVolume = pw * ph;
                            }
                            else
                            {
                                productUnitVolume = pw * ph * plength;
                            }
                            totalOccupiedVolume += productUnitVolume * a.Quantity;
                        }

                        var binWidth = bin.Width ?? 0.0;
                        var binHeight = bin.Height ?? 0.0;
                        var binLength = bin.Length ?? 0.0;
                        var binVolume = binWidth * binHeight * binLength;

                        if (binVolume > 0.0 && totalOccupiedVolume > 0.0)
                        {
                            var percentDouble = (totalOccupiedVolume / binVolume) * 100.0;
                            var percentInt = (int)Math.Min(100, Math.Floor(percentDouble));
                            bin.Percentage = percentInt;
                        }
                        else
                        {
                            // If cannot compute (missing dims), set null or zero - choose null to indicate unknown
                            bin.Percentage = null;
                        }

                        if (bin.Percentage == 100)
                        {
                            bin.Status = true;
                        }
                    }

                    var allItems = order.InboundOrderItems;
                    var anyReceived = allItems.Any(i => (i.ReceivedQuantity ?? 0) > 0);
                    var allComplete = allItems.Any() && allItems.All(i => (i.ExpectedQuantity ?? 0) > 0 && (i.ReceivedQuantity ?? 0) == (i.ExpectedQuantity ?? 0));

                    if (allComplete)
                        order.Status = "Completed";
                    else if (anyReceived)
                        order.Status = "Partially Completed";

                    // ── FIFO Batch update ────────────────────────────────────────────────────
                    // Load existing batches for this inbound order
                    var existingBatches = await _context.InventoryBatches
                        .Include(b => b.BatchLocations)
                        .Where(b => b.InboundOrderId == inboundOrderId)
                        .ToListAsync()
                        .ConfigureAwait(false);

                    foreach (var (inboundItemId, productId, delta) in deltas)
                    {
                        if (delta <= 0) continue; // only process positive received quantities

                        var batch = existingBatches.FirstOrDefault(b => b.ProductId == productId);
                        if (batch == null)
                        {
                            // Fallback: create batch if skeleton was not created at order creation
                            var incomingItem = incomingList.First(i => i.ProductId == productId);
                            batch = new InventoryBatch
                            {
                                InboundOrderId = inboundOrderId,
                                ProductId = productId,
                                WarehouseId = order.WarehouseId!.Value,
                                ReceivedQuantity = 0,
                                RemainingQuantity = 0,
                                UnitCost = (decimal)(order.InboundOrderItems
                                    .FirstOrDefault(i => i.ProductId == productId)?.Price ?? 0),
                                LineDiscount = (decimal)(order.InboundOrderItems
                                    .FirstOrDefault(i => i.ProductId == productId)?.Discount ?? 0),
                                InboundDate = order.CreatedAt ??
                                    DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                                IsExhausted = false,
                                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                            };
                            _context.InventoryBatches.Add(batch);
                            existingBatches.Add(batch);
                        }

                        // Update received/remaining quantities
                        batch.ReceivedQuantity += delta;
                        batch.RemainingQuantity += delta;
                        batch.UpdatedAt = now;

                        // Update BatchLocations from placements
                        if (placementList.Any())
                        {
                            var itemPlacements = placementList
                                .Where(p => p.ProductId == productId && p.InboundOrderItemId == inboundItemId)
                                .ToList();

                            foreach (var placement in itemPlacements)
                            {
                                var bin = bins.FirstOrDefault(b => b.IdCode == placement.BinIdCode);
                                if (bin == null) continue;

                                var existingLocation = batch.BatchLocations
                                    .FirstOrDefault(bl => bl.BinId == bin.Id);

                                if (existingLocation == null)
                                {
                                    batch.BatchLocations.Add(new InventoryBatchLocation
                                    {
                                        BinId = bin.Id,
                                        Quantity = placement.Quantity,
                                        UpdatedAt = now
                                    });
                                }
                                else
                                {
                                    existingLocation.Quantity += placement.Quantity;
                                    existingLocation.UpdatedAt = now;
                                }
                            }
                        }
                    }
                    // ── End FIFO Batch update ─────────────────────────────────────────────────

                    await _context.SaveChangesAsync().ConfigureAwait(false);
                    await tx.CommitAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return order;
        }
        public async Task<List<InboundRequest>> GetAllInboundRequestsAsync(int companyId)
        {
            return await _context.InboundRequests
                .Include(r => r.InboundOrderItems)
                .Include(r => r.Supplier)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r => r.RequestedByNavigation.CompanyId == companyId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<InboundOrder>> GetAllInboundOrdersAsync(int companyId)
        {
            return await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.InboundRequest)
                .Include(o => o.Supplier)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Where(o => o.CreatedByNavigation.CompanyId == companyId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }
        public async Task<InboundRequest> GetInboundRequestByIdAsync(int companyId, int id)
        {
            var request = await _context.InboundRequests
                .Include(r => r.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.Supplier)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r => r.RequestedByNavigation.CompanyId == companyId)
                .FirstOrDefaultAsync(r => r.Id == id)
                .ConfigureAwait(false);

            if (request == null)
                throw new InvalidOperationException($"InboundRequest with id {id} not found.");

            return request;
        }

        public async Task<InboundOrder> GetInboundOrderByIdAsync(int companyId, int id)
        {
            var order = await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.InboundRequest)
                .Include(o => o.Supplier)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Where(o => o.CreatedByNavigation.CompanyId == companyId)
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"InboundOrder with id {id} not found.");

            if (string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                await _context.Entry(order)
                    .Collection(o => o.InventoryBatches)
                    .Query()
                        .Include(b => b.InboundOrderItem)
                        .Include(b => b.Product)
                        .Include(b => b.BatchLocations)
                            .ThenInclude(bl => bl.Bin)
                                .ThenInclude(bin => bin.Level)
                                    .ThenInclude(level => level!.Shelf)
                                        .ThenInclude(shelf => shelf!.Zone)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }

            return order;
        }
        public async Task<List<InboundRequest>> GetInboundRequestsByWarehouseAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            return await _context.InboundRequests
                .Include(r => r.InboundOrderItems)
                .Include(r => r.Supplier)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r => r.WarehouseId == warehouseId && r.RequestedByNavigation.CompanyId == companyId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<InboundOrder>> GetInboundOrdersByWarehouseAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            return await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.InboundRequest)
                .Include(o => o.Supplier)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Where(o => o.WarehouseId == warehouseId && o.Warehouse != null && o.Warehouse.CompanyId == companyId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }
        public async Task<bool> InboundRequestCodeExistsAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            return await _context.InboundRequests.AnyAsync(r => r.Code == code).ConfigureAwait(false);
        }
        public async Task<List<InboundOrder>> GetInboundOrdersByStaffAsync(int companyId, int staffId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (staffId <= 0) throw new ArgumentException("Invalid staff id.", nameof(staffId));

            var query = _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Supplier)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Include(o => o.Staff)
                .Include(o => o.InboundRequest)
                .Where(o => o.StaffId == staffId && o.Warehouse != null && o.Warehouse.CompanyId == companyId)
                .OrderByDescending(o => o.CreatedAt);

            return await query.ToListAsync().ConfigureAwait(false);
        }
        public async Task<InboundRequestExportDto?> GetInboundRequestForExportAsync(int inboundRequestId)
        {
            if (inboundRequestId <= 0) return null;

            var dto = await _context.InboundRequests
                .Where(r => r.Id == inboundRequestId)
                .Select(r => new InboundRequestExportDto
                {
                    Id = r.Id,
                    Code = r.Code,
                    Warehouse = r.Warehouse != null ? r.Warehouse.Name : null,
                    Supplier = r.Supplier != null ? r.Supplier.Name : null,
                    RequestedBy = r.RequestedByNavigation != null ? r.RequestedByNavigation.FullName : null,
                    ApprovedBy = r.ApprovedByNavigation != null ? r.ApprovedByNavigation.FullName : null,
                    Status = r.Status,
                    TotalPrice = r.TotalPrice,
                    OrderDiscount = r.OrderDiscount,
                    FinalPrice = r.FinalPrice,
                    ExpectedDate = r.ExpectedDate,
                    Note = r.Note,
                    CreatedAt = r.CreatedAt,
                    ApprovedAt = r.ApprovedAt,
                    Items = r.InboundOrderItems.Select(i => new InboundOrderItemExportDto
                    {
                        ProductId = i.ProductId,
                        Sku = i.Product != null ? i.Product.Sku : null,
                        Name = i.Product != null ? i.Product.Name : null,
                        Price = i.Price,
                        Discount = i.Discount,
                        ExpectedQuantity = i.ExpectedQuantity,
                        ReceivedQuantity = i.ReceivedQuantity,
                        Description = i.Product != null ? i.Product.Description : null
                    }).ToList()
                })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return dto;
        }

        public async Task<InboundOrderExportDto?> GetInboundOrderForExportAsync(int inboundOrderId)
        {
            if (inboundOrderId <= 0) return null;

            var dto = await _context.InboundOrders
                .Where(o => o.Id == inboundOrderId)
                .Select(o => new InboundOrderExportDto
                {
                    Id = o.Id,
                    ReferenceCode = o.ReferenceCode,
                    Warehouse = o.Warehouse != null ? o.Warehouse.Name : null,
                    Supplier = o.Supplier != null ? o.Supplier.Name : null,
                    CreatedBy = o.CreatedByNavigation != null ? o.CreatedByNavigation.FullName : null,
                    Staff = o.Staff != null ? o.Staff.FullName : null,
                    Status = o.Status,
                    TotalPrice = o.InboundRequest != null ? o.InboundRequest.FinalPrice : null,
                    CreatedAt = o.CreatedAt,
                    Items = o.InboundOrderItems.Select(i => new InboundOrderItemExportDto
                    {
                        ProductId = i.ProductId,
                        Sku = i.Product != null ? i.Product.Sku : null,
                        Name = i.Product != null ? i.Product.Name : null,
                        Price = i.Price,
                        Discount = i.Discount,
                        ExpectedQuantity = i.ExpectedQuantity,
                        ReceivedQuantity = i.ReceivedQuantity,
                        Description = i.Product != null ? i.Product.Description : null
                    }).ToList()
                })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return dto;
        }

        public byte[] ExportInboundRequestToCsv(InboundRequestExportDto request)
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
                        request.Supplier,
                        request.RequestedBy,
                        request.ApprovedBy,
                        request.Status,
                        request.TotalPrice,
                        request.OrderDiscount,
                        request.FinalPrice,
                        ExpectedDate = request.ExpectedDate?.ToString(),
                        request.Note,
                        request.CreatedAt,
                        request.ApprovedAt,
                        Item_ProductId = it.ProductId,
                        Item_Sku = it.Sku,
                        Item_Name = it.Name,
                        Item_Price = it.Price,
                        Item_Discount = it.Discount,
                        Item_ExpectedQuantity = it.ExpectedQuantity,
                        Item_ReceivedQuantity = it.ReceivedQuantity,
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
                    request.Supplier,
                    request.RequestedBy,
                    request.ApprovedBy,
                    request.Status,
                    request.TotalPrice,
                    request.OrderDiscount,
                    request.FinalPrice,
                    ExpectedDate = request.ExpectedDate?.ToString(),
                    request.Note,
                    request.CreatedAt,
                    request.ApprovedAt,
                    Item_ProductId = (int?)null,
                    Item_Sku = (string?)null,
                    Item_Name = (string?)null,
                    Item_Price = (double?)null,
                    Item_Discount = (double?)null,
                    Item_ExpectedQuantity = (int?)null,
                    Item_ReceivedQuantity = (int?)null,
                    Item_TypeId = (int?)null,
                    Item_Description = (string?)null
                });
            }

            csv.WriteRecords(rows);
            writer.Flush();
            return memoryStream.ToArray();
        }

        public byte[] ExportInboundRequestToExcel(InboundRequestExportDto request)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("InboundRequest");

            var headers = new[]
            {
                "Request ID","Code","Warehouse","Supplier","Requested By","Approved By","Status",
                "Total Price","Order Discount","Final Price","Expected Date","Note","Created At","Approved At",
                "Item ProductId","Item SKU","Item Name","Item Price","Item Discount","Item ExpectedQty","Item ReceivedQty","Item TypeId","Item Description"
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
                    worksheet.Cell(rowIndex, 4).Value = request.Supplier;
                    worksheet.Cell(rowIndex, 5).Value = request.RequestedBy;
                    worksheet.Cell(rowIndex, 6).Value = request.ApprovedBy;
                    worksheet.Cell(rowIndex, 7).Value = request.Status;
                    worksheet.Cell(rowIndex, 8).Value = request.TotalPrice;
                    worksheet.Cell(rowIndex, 9).Value = request.OrderDiscount;
                    worksheet.Cell(rowIndex, 10).Value = request.FinalPrice;
                    worksheet.Cell(rowIndex, 11).Value = request.ExpectedDate?.ToString();
                    worksheet.Cell(rowIndex, 12).Value = request.Note;
                    worksheet.Cell(rowIndex, 13).Value = request.CreatedAt;
                    worksheet.Cell(rowIndex, 14).Value = request.ApprovedAt;

                    worksheet.Cell(rowIndex, 15).Value = it.ProductId;
                    worksheet.Cell(rowIndex, 16).Value = it.Sku;
                    worksheet.Cell(rowIndex, 17).Value = it.Name;
                    worksheet.Cell(rowIndex, 18).Value = it.Price;
                    worksheet.Cell(rowIndex, 19).Value = it.Discount;
                    worksheet.Cell(rowIndex, 20).Value = it.ExpectedQuantity;
                    worksheet.Cell(rowIndex, 21).Value = it.ReceivedQuantity;
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
                worksheet.Cell(rowIndex, 4).Value = request.Supplier;
                worksheet.Cell(rowIndex, 5).Value = request.RequestedBy;
                worksheet.Cell(rowIndex, 6).Value = request.ApprovedBy;
                worksheet.Cell(rowIndex, 7).Value = request.Status;
                worksheet.Cell(rowIndex, 8).Value = request.TotalPrice;
                worksheet.Cell(rowIndex, 9).Value = request.OrderDiscount;
                worksheet.Cell(rowIndex, 10).Value = request.FinalPrice;
                worksheet.Cell(rowIndex, 11).Value = request.ExpectedDate?.ToString();
                worksheet.Cell(rowIndex, 12).Value = request.Note;
                worksheet.Cell(rowIndex, 13).Value = request.CreatedAt;
                worksheet.Cell(rowIndex, 14).Value = request.ApprovedAt;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] ExportInboundOrderToCsv(InboundOrderExportDto order)
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
                        order.Supplier,
                        order.CreatedBy,
                        order.Staff,
                        order.Status,
                        order.TotalPrice,
                        order.CreatedAt,
                        Item_ProductId = it.ProductId,
                        Item_Sku = it.Sku,
                        Item_Name = it.Name,
                        Item_Price = it.Price,
                        Item_Discount = it.Discount,
                        Item_ExpectedQuantity = it.ExpectedQuantity,
                        Item_ReceivedQuantity = it.ReceivedQuantity,
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
                    order.Supplier,
                    order.CreatedBy,
                    order.Staff,
                    order.Status,
                    order.TotalPrice,
                    order.CreatedAt,
                    Item_ProductId = (int?)null,
                    Item_Sku = (string?)null,
                    Item_Name = (string?)null,
                    Item_Price = (double?)null,
                    Item_Discount = (double?)null,
                    Item_ExpectedQuantity = (int?)null,
                    Item_ReceivedQuantity = (int?)null,
                    Item_TypeId = (int?)null,
                    Item_Description = (string?)null
                });
            }

            csv.WriteRecords(rows);
            writer.Flush();
            return memoryStream.ToArray();
        }

        public byte[] ExportInboundOrderToExcel(InboundOrderExportDto order)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("InboundOrder");

            var headers = new[]
            {
                "Order ID","Reference Code","Warehouse","Supplier","Created By","Staff","Status","Total Price","Created At",
                "Item ProductId","Item SKU","Item Name","Item Price","Item Discount","Item ExpectedQty","Item ReceivedQty","Item TypeId","Item Description"
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
                    worksheet.Cell(rowIndex, 4).Value = order.Supplier;
                    worksheet.Cell(rowIndex, 5).Value = order.CreatedBy;
                    worksheet.Cell(rowIndex, 6).Value = order.Staff;
                    worksheet.Cell(rowIndex, 7).Value = order.Status;
                    worksheet.Cell(rowIndex, 8).Value = order.TotalPrice;
                    worksheet.Cell(rowIndex, 9).Value = order.CreatedAt;

                    worksheet.Cell(rowIndex, 10).Value = it.ProductId;
                    worksheet.Cell(rowIndex, 11).Value = it.Sku;
                    worksheet.Cell(rowIndex, 12).Value = it.Name;
                    worksheet.Cell(rowIndex, 13).Value = it.Price;
                    worksheet.Cell(rowIndex, 14).Value = it.Discount;
                    worksheet.Cell(rowIndex, 15).Value = it.ExpectedQuantity;
                    worksheet.Cell(rowIndex, 16).Value = it.ReceivedQuantity;
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
                worksheet.Cell(rowIndex, 4).Value = order.Supplier;
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


        public async Task AddStorageRecommendationsAsync(IEnumerable<IInventoryInboundRepository.StorageRecommendationCreateDto> requests)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            var list = requests.ToList();
            if (!list.Any()) return;

            var inboundProductIds = list.Select(r => r.InboundProductId).Distinct().ToList();
            // Validate inbound products exist
            var existingInboundProductIds = await _context.InboundOrderItems
                .Where(i => inboundProductIds.Contains(i.Id))
                .Select(i => i.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var missingInbound = inboundProductIds.Except(existingInboundProductIds).ToList();
            if (missingInbound.Any())
                throw new InvalidOperationException($"InboundOrderItems not found: {string.Join(',', missingInbound)}");

            // Resolve shelf level bins by IdCode (BinIdCode)
            var binIdCodes = list.Select(r => r.Recommendation.BinIdCode).Distinct().ToList();
            var shelfBins = await _context.ShelfLevelBins
                .Where(b => binIdCodes.Contains(b.IdCode))
                .ToListAsync()
                .ConfigureAwait(false);

            var missingBins = binIdCodes.Except(shelfBins.Select(b => b.IdCode)).ToList();
            if (missingBins.Any())
                throw new InvalidOperationException($"ShelfLevelBins not found for IdCodes: {string.Join(',', missingBins)}");

            // Use transaction to persist Recommendation and StorageRecommendation atomically
            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                foreach (var req in list)
                {
                    var bin = shelfBins.First(b => b.IdCode == req.Recommendation.BinIdCode);

                    var recommendation = new Recommendation
                    {
                        BinId = bin.Id,
                        Path = req.Recommendation.Path,
                        DistanceInfo = req.Recommendation.DistanceInfo,
                        Quantity = req.Recommendation.Quantity
                    };

                    // Create StorageRecommendation with navigation to Recommendation so EF will insert both and set FK.
                    var storageRecommendation = new StorageRecommendation
                    {
                        InboundProductId = req.InboundProductId,
                        Recommendation = recommendation,
                        Reason = req.Reason,
                        CreatedAt = now
                    };

                    _context.StorageRecommendations.Add(storageRecommendation);
                }

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }
        public async Task<List<InboundOrderItem>> GetInboundOrderItemsWithRecommendationsAsync(int inboundOrderId)
        {
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inbound order id.", nameof(inboundOrderId));

            var orderExists = await _context.InboundOrders
                .AnyAsync(o => o.Id == inboundOrderId)
                .ConfigureAwait(false);

            if (!orderExists)
                throw new InvalidOperationException($"InboundOrder with id {inboundOrderId} not found.");

            var items = await _context.InboundOrderItems
                .Where(i => i.InboundOrderId == inboundOrderId)
                .Include(i => i.Product)
                .Include(i => i.StorageRecommendations)
                    .ThenInclude(sr => sr.Recommendation)
                        .ThenInclude(r => r.Bin)
                .ToListAsync()
                .ConfigureAwait(false);

            return items;
        }
        public async Task<InboundOrder> AssignStaffToInboundOrderAsync(int companyId, int inboundOrderId, int managerUserId, int staffUserId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inboundOrderId.", nameof(inboundOrderId));
            if (managerUserId <= 0) throw new ArgumentException("Invalid managerUserId.", nameof(managerUserId));
            if (staffUserId <= 0) throw new ArgumentException("Invalid staffUserId.", nameof(staffUserId));

            var order = await _context.InboundOrders
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Include(o => o.Staff)
                .FirstOrDefaultAsync(o => o.Id == inboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"InboundOrder with id {inboundOrderId} not found.");

            if (order.Warehouse == null || order.Warehouse.CompanyId != companyId)
                throw new InvalidOperationException("InboundOrder does not belong to the specified company.");

            // Ensure manager is assigned to the same warehouse
            var managerAssignment = await _context.WarehouseAssignments
                .AnyAsync(a => a.UserId == managerUserId && a.WarehouseId == order.WarehouseId)
                .ConfigureAwait(false);
            if (!managerAssignment)
                throw new InvalidOperationException("Manager is not assigned to the inbound order warehouse.");

            // Ensure staff is assigned to the same warehouse
            var staffAssignment = await _context.WarehouseAssignments
                .AnyAsync(a => a.UserId == staffUserId && a.WarehouseId == order.WarehouseId)
                .ConfigureAwait(false);
            if (!staffAssignment)
                throw new InvalidOperationException("Staff is not assigned to the inbound order warehouse.");

            order.StaffId = staffUserId;
            await _context.SaveChangesAsync().ConfigureAwait(false);

            // refresh navigation property
            await _context.Entry(order).Reference(o => o.Staff).LoadAsync().ConfigureAwait(false);

            return order;
        }
    }
}
