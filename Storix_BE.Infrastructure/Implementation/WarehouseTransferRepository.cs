using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;

namespace Storix_BE.Repository.Implementation
{
    public class WarehouseTransferRepository : IWarehouseTransferRepository
    {
        private readonly StorixDbContext _context;

        public WarehouseTransferRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<TransferOrder> CreateTransferOrderAsync(TransferOrder order)
        {
            _context.TransferOrders.Add(order);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return order;
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();

        public Task<TransferOrder?> GetTransferOrderWithWarehousesAsync(int transferOrderId)
            => _context.TransferOrders
                .Include(t => t.SourceWarehouse)
                .Include(t => t.DestinationWarehouse)
                .FirstOrDefaultAsync(t => t.Id == transferOrderId);

        public Task<TransferOrder?> GetTransferOrderDetailAsync(int transferOrderId)
            => _context.TransferOrders
                .Include(t => t.SourceWarehouse)
                .Include(t => t.DestinationWarehouse)
                .Include(t => t.CreatedByNavigation)
                .Include(t => t.TransferOrderItems)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(t => t.Id == transferOrderId);

        public async Task<List<TransferOrder>> GetTransferOrdersByCompanyAsync(int companyId, int? sourceWarehouseId, int? destinationWarehouseId, string? status)
        {
            var query = _context.TransferOrders
                .Include(t => t.SourceWarehouse)
                .Include(t => t.DestinationWarehouse)
                .Include(t => t.CreatedByNavigation)
                .Include(t => t.TransferOrderItems)
                .Where(t => t.SourceWarehouse != null && t.DestinationWarehouse != null
                    && t.SourceWarehouse.CompanyId == companyId
                    && t.DestinationWarehouse.CompanyId == companyId);

            if (sourceWarehouseId.HasValue) query = query.Where(t => t.SourceWarehouseId == sourceWarehouseId.Value);
            if (destinationWarehouseId.HasValue) query = query.Where(t => t.DestinationWarehouseId == destinationWarehouseId.Value);
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status != null && t.Status.ToUpper() == status.Trim().ToUpper());

            return await query.OrderByDescending(x => x.CreatedAt).ToListAsync().ConfigureAwait(false);
        }

        public Task<TransferOrderItem?> GetTransferOrderItemAsync(int transferOrderId, int itemId)
            => _context.TransferOrderItems.FirstOrDefaultAsync(x => x.Id == itemId && x.TransferOrderId == transferOrderId);

        public Task<TransferOrderItem?> GetTransferOrderItemByProductAsync(int transferOrderId, int productId)
            => _context.TransferOrderItems.FirstOrDefaultAsync(i => i.TransferOrderId == transferOrderId && i.ProductId == productId);

        public void AddTransferOrderItem(TransferOrderItem item) => _context.TransferOrderItems.Add(item);

        public void RemoveTransferOrderItem(TransferOrderItem item) => _context.TransferOrderItems.Remove(item);

        public Task<bool> AnyTransferItemsAsync(int transferOrderId)
            => _context.TransferOrderItems.AnyAsync(x => x.TransferOrderId == transferOrderId);

        public Task<List<TransferOrderItem>> GetTransferItemsByOrderIdAsync(int transferOrderId)
            => _context.TransferOrderItems.Where(x => x.TransferOrderId == transferOrderId).ToListAsync();

        public Task<List<TransferOrderItem>> GetTransferItemsWithProductByOrderIdAsync(int transferOrderId)
            => _context.TransferOrderItems.Include(i => i.Product).Where(i => i.TransferOrderId == transferOrderId).ToListAsync();

        public Task<List<Inventory>> GetInventoriesByWarehouseAndProductsAsync(int? warehouseId, IReadOnlyCollection<int> productIds)
            => _context.Inventories
                .Where(i => i.WarehouseId == warehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync();

        public Task<Inventory?> GetInventoryAsync(int? warehouseId, int productId)
            => _context.Inventories.FirstOrDefaultAsync(i => i.WarehouseId == warehouseId && i.ProductId == productId);

        public void AddInventory(Inventory inventory) => _context.Inventories.Add(inventory);

        public Task<User?> GetUserByIdAsync(int userId)
            => _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        public Task<bool> IsStaffAssignedToWarehouseAsync(int userId, int warehouseId)
            => _context.WarehouseAssignments.AsNoTracking().AnyAsync(a => a.WarehouseId == warehouseId && a.UserId == userId);

        public Task<Warehouse?> GetWarehouseByIdAsync(int warehouseId)
            => _context.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId);

        public Task<bool> ProductInCompanyAsync(int productId, int companyId)
            => _context.Products.AsNoTracking().AnyAsync(p => p.Id == productId && p.CompanyId == companyId);

        public async Task AddActivityAsync(int userId, string action, int transferOrderId, DateTime? timestamp = null)
        {
            _context.ActivityLogs.Add(new ActivityLog
            {
                UserId = userId,
                Action = action,
                Entity = "TransferOrder",
                EntityId = transferOrderId,
                Timestamp = timestamp ?? DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public Task<List<ActivityLog>> GetActivitiesAsync(int transferOrderId)
            => _context.ActivityLogs
                .Where(a => a.Entity == "TransferOrder" && a.EntityId == transferOrderId)
                .Include(a => a.User)
                .OrderBy(a => a.Timestamp)
                .ToListAsync();

        public Task<string?> GetLatestActivityActionAsync(int transferOrderId, string prefix)
            => _context.ActivityLogs
                .Where(a => a.Entity == "TransferOrder" && a.EntityId == transferOrderId && a.Action != null && a.Action.StartsWith(prefix))
                .OrderByDescending(a => a.Timestamp)
                .Select(a => a.Action)
                .FirstOrDefaultAsync();

        public Task<List<User>> GetAssignedStaffInWarehouseAsync(int warehouseId, int companyId)
            => _context.WarehouseAssignments
                .Where(a => a.WarehouseId == warehouseId)
                .Select(a => a.User)
                .Where(u => u != null && (u.RoleId ?? 0) == 4 && (u.CompanyId ?? 0) == companyId)
                .Distinct()
                .ToListAsync()!;

        public Task<int> CountWarehouseAssignmentsByUserAsync(int userId)
            => _context.WarehouseAssignments.AsNoTracking().CountAsync(a => a.UserId == userId);

        public Task<List<int>> GetTransferOrderIdsByCarrierAsync(int staffUserId)
            => _context.ActivityLogs
                .Where(a => a.Entity == "TransferOrder" && a.Action == $"CARRIER:{staffUserId}")
                .Select(a => a.EntityId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToListAsync();

        public Task<int> CountActiveTransfersByOrderIdsAsync(int companyId, IReadOnlyCollection<int> orderIds, IReadOnlyCollection<string> activeStatuses)
            => _context.TransferOrders
                .Include(t => t.SourceWarehouse)
                .Include(t => t.DestinationWarehouse)
                .Where(t => orderIds.Contains(t.Id)
                    && activeStatuses.Contains((t.Status ?? string.Empty).ToUpper())
                    && (t.SourceWarehouse!.CompanyId ?? 0) == companyId
                    && (t.DestinationWarehouse!.CompanyId ?? 0) == companyId)
                .CountAsync();

        public Task<OutboundOrder?> GetOutboundOrderWithItemsAsync(int outboundOrderId)
            => _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId);

        public async Task ApproveTransferAsync(
            TransferOrder order,
            IReadOnlyCollection<TransferOrderItem> items,
            IReadOnlyCollection<Inventory> sourceInventories,
            int actorUserId,
            int? receiverStaffId,
            int? carrierId)
        {
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                foreach (var line in items)
                {
                    var inv = sourceInventories.First(i => i.ProductId == line.ProductId);
                    var qty = line.Quantity ?? 0;
                    inv.Quantity = (inv.Quantity ?? 0) - qty;
                    inv.LastUpdated = now;

                    _context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        WarehouseId = order.SourceWarehouseId,
                        ProductId = line.ProductId,
                        QuantityChange = -qty,
                        TransactionType = "TransferApproveOut",
                        ReferenceId = order.Id,
                        PerformedBy = actorUserId,
                        CreatedAt = now
                    });
                }

                var outbound = new OutboundOrder
                {
                    WarehouseId = order.SourceWarehouseId,
                    Destination = order.DestinationWarehouse?.Name,
                    CreatedBy = actorUserId,
                    StaffId = carrierId,
                    Status = "READY",
                    Note = $"AUTO_FROM_TRANSFER#{order.Id}",
                    CreatedAt = now
                };

                foreach (var line in items)
                {
                    outbound.OutboundOrderItems.Add(new OutboundOrderItem
                    {
                        ProductId = line.ProductId,
                        Quantity = line.Quantity,
                        PricingMethod = "LastPurchasePrice"
                    });
                }

                _context.OutboundOrders.Add(outbound);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                var inbound = new InboundOrder
                {
                    WarehouseId = order.DestinationWarehouseId,
                    CreatedBy = actorUserId,
                    StaffId = receiverStaffId,
                    Status = "WAITING_RECEIPT",
                    ReferenceCode = $"AUTO_FROM_TRANSFER#{order.Id}",
                    CreatedAt = now
                };

                foreach (var line in items)
                {
                    inbound.InboundOrderItems.Add(new InboundOrderItem
                    {
                        ProductId = line.ProductId,
                        ExpectedQuantity = line.Quantity,
                        ReceivedQuantity = 0
                    });
                }

                _context.InboundOrders.Add(inbound);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                var outboundByProduct = outbound.OutboundOrderItems
                    .Where(x => x.ProductId.HasValue)
                    .ToDictionary(x => x.ProductId!.Value, x => x.Id);
                var inboundByProduct = inbound.InboundOrderItems
                    .Where(x => x.ProductId.HasValue)
                    .ToDictionary(x => x.ProductId!.Value, x => x.Id);

                foreach (var line in items.Where(i => i.ProductId.HasValue))
                {
                    var productId = line.ProductId!.Value;
                    if (!outboundByProduct.TryGetValue(productId, out var outboundItemId))
                        throw new InvalidOperationException($"Cannot map outbound item for product {productId}.");
                    if (!inboundByProduct.TryGetValue(productId, out var inboundItemId))
                        throw new InvalidOperationException($"Cannot map inbound item for product {productId}.");

                    line.OutboundOrderItemId = outboundItemId;
                    line.InboundOrderItemId = inboundItemId;
                }

                order.OutboundTicketId = outbound.Id;
                order.InboundTicketId = inbound.Id;
                order.Status = "APPROVED";

                _context.ActivityLogs.AddRange(
                    new ActivityLog
                    {
                        UserId = actorUserId,
                        Action = $"LINK_OUTBOUND:{outbound.Id}",
                        Entity = "TransferOrder",
                        EntityId = order.Id,
                        Timestamp = now
                    },
                    new ActivityLog
                    {
                        UserId = actorUserId,
                        Action = $"LINK_INBOUND:{inbound.Id}",
                        Entity = "TransferOrder",
                        EntityId = order.Id,
                        Timestamp = now
                    },
                    new ActivityLog
                    {
                        UserId = actorUserId,
                        Action = "TRANSFER_APPROVED",
                        Entity = "TransferOrder",
                        EntityId = order.Id,
                        Timestamp = now
                    });

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task ShipTransferAsync(TransferOrder order)
        {
            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                order.Status = "IN_TRANSIT";
                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task ReceiveTransferAsync(
            TransferOrder order,
            IReadOnlyCollection<(int ProductId, int ReceivedQuantity)> receiveLines,
            IReadOnlyDictionary<int, int> requiredByProduct)
        {
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                foreach (var line in receiveLines)
                {
                    if (!requiredByProduct.ContainsKey(line.ProductId))
                        throw new InvalidOperationException($"INVALID_PRODUCT {line.ProductId}");
                    if (line.ReceivedQuantity < 0)
                        throw new InvalidOperationException("INVALID_RECEIVED_QUANTITY");

                    var inv = await GetInventoryAsync(order.DestinationWarehouseId, line.ProductId).ConfigureAwait(false);
                    if (inv == null)
                    {
                        inv = new Inventory
                        {
                            WarehouseId = order.DestinationWarehouseId,
                            ProductId = line.ProductId,
                            Quantity = 0,
                            ReservedQuantity = 0,
                            LastUpdated = now
                        };
                        _context.Inventories.Add(inv);
                    }

                    inv.Quantity = (inv.Quantity ?? 0) + line.ReceivedQuantity;
                    inv.LastUpdated = now;
                }

                order.Status = "COMPLETED";
                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<int> BackfillTransferItemLinksAsync(int transferOrderId, int? outboundTicketId, int? inboundTicketId)
        {
            var transferItems = await _context.TransferOrderItems
                .Where(i => i.TransferOrderId == transferOrderId)
                .ToListAsync()
                .ConfigureAwait(false);

            if (!transferItems.Any())
                return 0;

            Dictionary<int, int> outboundByProduct = new();
            if (outboundTicketId.HasValue)
            {
                outboundByProduct = await _context.OutboundOrderItems
                    .Where(i => i.OutboundOrderId == outboundTicketId.Value && i.ProductId.HasValue)
                    .GroupBy(i => i.ProductId!.Value)
                    .Select(g => g.OrderBy(x => x.Id).First())
                    .ToDictionaryAsync(i => i.ProductId!.Value, i => i.Id)
                    .ConfigureAwait(false);
            }

            Dictionary<int, int> inboundByProduct = new();
            if (inboundTicketId.HasValue)
            {
                inboundByProduct = await _context.InboundOrderItems
                    .Where(i => i.InboundOrderId == inboundTicketId.Value && i.ProductId.HasValue)
                    .GroupBy(i => i.ProductId!.Value)
                    .Select(g => g.OrderBy(x => x.Id).First())
                    .ToDictionaryAsync(i => i.ProductId!.Value, i => i.Id)
                    .ConfigureAwait(false);
            }

            var changed = 0;
            foreach (var transferItem in transferItems.Where(i => i.ProductId.HasValue))
            {
                var productId = transferItem.ProductId!.Value;

                if (!transferItem.OutboundOrderItemId.HasValue && outboundByProduct.TryGetValue(productId, out var outboundItemId))
                {
                    transferItem.OutboundOrderItemId = outboundItemId;
                    changed++;
                }

                if (!transferItem.InboundOrderItemId.HasValue && inboundByProduct.TryGetValue(productId, out var inboundItemId))
                {
                    transferItem.InboundOrderItemId = inboundItemId;
                    changed++;
                }
            }

            if (changed > 0)
            {
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }

            return changed;
        }
    }
}
