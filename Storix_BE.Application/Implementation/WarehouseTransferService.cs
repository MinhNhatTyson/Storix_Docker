using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.Service.Implementation
{
    public class WarehouseTransferService : IWarehouseTransferService
    {
        private readonly StorixDbContext _context;

        public WarehouseTransferService(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<TransferOrderDetailDto> CreateDraftAsync(int companyId, int createdBy, CreateTransferOrderRequest request)
        {
            ValidateCompanyAndUser(companyId, createdBy);
            ValidateWarehouses(request.SourceWarehouseId, request.DestinationWarehouseId);

            await EnsureManagerAsync(createdBy, companyId);
            await GetWarehouseInCompanyAsync(request.SourceWarehouseId, companyId);
            await GetWarehouseInCompanyAsync(request.DestinationWarehouseId, companyId);

            var entity = new TransferOrder
            {
                SourceWarehouseId = request.SourceWarehouseId,
                DestinationWarehouseId = request.DestinationWarehouseId,
                CreatedBy = createdBy,
                Status = TransferStatuses.Draft,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            _context.TransferOrders.Add(entity);
            await _context.SaveChangesAsync();
            await AddActivityAsync(createdBy, "TRANSFER_CREATED_DRAFT", entity.Id);

            if (request.SubmitAfterCreate)
                return await SubmitAsync(companyId, createdBy, entity.Id);

            return await GetByIdAsync(companyId, entity.Id);
        }

        public async Task<TransferOrderDetailDto> UpdateDraftAsync(int companyId, int actorUserId, int transferOrderId, UpdateTransferOrderRequest request)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            EnsureCanEdit(order.Status);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);

            ValidateWarehouses(request.SourceWarehouseId, request.DestinationWarehouseId);
            await GetWarehouseInCompanyAsync(request.SourceWarehouseId, companyId);
            await GetWarehouseInCompanyAsync(request.DestinationWarehouseId, companyId);

            order.SourceWarehouseId = request.SourceWarehouseId;
            order.DestinationWarehouseId = request.DestinationWarehouseId;
            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_UPDATED_DRAFT", order.Id);

            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> AddItemAsync(int companyId, int actorUserId, int transferOrderId, AddTransferOrderItemRequest request)
        {
            ValidatePositive(request.ProductId, nameof(request.ProductId));
            ValidatePositive(request.Quantity, nameof(request.Quantity));

            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            EnsureCanEdit(order.Status);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);
            await EnsureProductInCompanyAsync(request.ProductId, companyId);

            var existing = await _context.TransferOrderItems
                .FirstOrDefaultAsync(i => i.TransferOrderId == order.Id && i.ProductId == request.ProductId);

            if (existing == null)
            {
                _context.TransferOrderItems.Add(new TransferOrderItem
                {
                    TransferOrderId = order.Id,
                    ProductId = request.ProductId,
                    Quantity = request.Quantity
                });
            }
            else
            {
                existing.Quantity = (existing.Quantity ?? 0) + request.Quantity;
            }

            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_ITEM_ADDED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> UpdateItemAsync(int companyId, int actorUserId, int transferOrderId, int itemId, UpdateTransferOrderItemRequest request)
        {
            ValidatePositive(itemId, nameof(itemId));
            ValidatePositive(request.ProductId, nameof(request.ProductId));
            ValidatePositive(request.Quantity, nameof(request.Quantity));

            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            EnsureCanEdit(order.Status);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);
            await EnsureProductInCompanyAsync(request.ProductId, companyId);

            var item = await _context.TransferOrderItems.FirstOrDefaultAsync(x => x.Id == itemId && x.TransferOrderId == order.Id);
            if (item == null) throw new InvalidOperationException("Transfer item not found.");

            item.ProductId = request.ProductId;
            item.Quantity = request.Quantity;
            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_ITEM_UPDATED", order.Id);

            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> RemoveItemAsync(int companyId, int actorUserId, int transferOrderId, int itemId)
        {
            ValidatePositive(itemId, nameof(itemId));

            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            EnsureCanEdit(order.Status);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);

            var item = await _context.TransferOrderItems.FirstOrDefaultAsync(x => x.Id == itemId && x.TransferOrderId == order.Id);
            if (item == null) throw new InvalidOperationException("Transfer item not found.");

            _context.TransferOrderItems.Remove(item);
            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_ITEM_REMOVED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> SubmitAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);

            if (!new[] { TransferStatuses.Draft, TransferStatuses.Rejected }.Contains((order.Status ?? "").ToUpperInvariant()))
                throw new InvalidOperationException("Only DRAFT/REJECTED can be submitted.");

            var hasItems = await _context.TransferOrderItems.AnyAsync(x => x.TransferOrderId == order.Id);
            if (!hasItems) throw new InvalidOperationException("Transfer must contain at least one item.");

            order.Status = TransferStatuses.PendingApproval;
            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_SUBMITTED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> ApproveAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAsync(actorUserId, companyId);
            if (!string.Equals(order.Status, TransferStatuses.PendingApproval, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only PENDING_APPROVAL can be approved.");

            var items = await _context.TransferOrderItems.Where(x => x.TransferOrderId == order.Id).ToListAsync();
            if (!items.Any()) throw new InvalidOperationException("Transfer must contain at least one item.");

            var productIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == order.SourceWarehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync();

            foreach (var line in items)
            {
                var inv = inventories.FirstOrDefault(i => i.ProductId == line.ProductId);
                var qty = line.Quantity ?? 0;
                var available = (inv?.Quantity ?? 0) - (inv?.ReservedQuantity ?? 0);
                if (qty <= 0 || inv == null || available < qty)
                    throw new InvalidOperationException($"OUT_OF_STOCK ProductId={line.ProductId}, available={available}, required={qty}");
            }

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var line in items)
                {
                    var inv = inventories.First(i => i.ProductId == line.ProductId);
                    var qty = line.Quantity ?? 0;
                    inv.ReservedQuantity = (inv.ReservedQuantity ?? 0) + qty;
                    inv.LastUpdated = now;
                }

                order.Status = TransferStatuses.Approved;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            await AddActivityAsync(actorUserId, "TRANSFER_APPROVED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> RejectAsync(int companyId, int actorUserId, int transferOrderId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("reason is required", nameof(reason));

            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAsync(actorUserId, companyId);
            if (!string.Equals(order.Status, TransferStatuses.PendingApproval, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only PENDING_APPROVAL can be rejected.");

            order.Status = TransferStatuses.Rejected;
            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, $"TRANSFER_REJECTED:{reason}", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> StartPickingAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.SourceWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.Approved);

            order.Status = TransferStatuses.Picking;
            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_PICKING_STARTED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> MarkPackedAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.SourceWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.Picking);

            order.Status = TransferStatuses.Packed;
            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_PACKED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> ShipAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.SourceWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.Packed);

            var items = await _context.TransferOrderItems.Where(x => x.TransferOrderId == order.Id).ToListAsync();
            var productIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == order.SourceWarehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync();

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var line in items)
                {
                    var inv = inventories.FirstOrDefault(i => i.ProductId == line.ProductId);
                    var qty = line.Quantity ?? 0;
                    if (inv == null || (inv.ReservedQuantity ?? 0) < qty || (inv.Quantity ?? 0) < qty)
                        throw new InvalidOperationException($"OUT_OF_STOCK_ON_SHIP ProductId={line.ProductId}");

                    inv.ReservedQuantity = (inv.ReservedQuantity ?? 0) - qty;
                    inv.Quantity = (inv.Quantity ?? 0) - qty;
                    inv.LastUpdated = now;
                }

                order.Status = TransferStatuses.InTransit;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            await AddActivityAsync(actorUserId, "TRANSFER_SHIPPED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> ReceiveAsync(int companyId, int actorUserId, int transferOrderId, ReceiveTransferOrderRequest request)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.DestinationWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.InTransit);

            var lines = request.Items?.ToList() ?? new List<ReceiveTransferItemRequest>();
            if (!lines.Any()) throw new InvalidOperationException("receive items required");

            var orderItems = await _context.TransferOrderItems.Where(x => x.TransferOrderId == order.Id).ToListAsync();
            var reqByProduct = orderItems.Where(i => i.ProductId.HasValue).ToDictionary(i => i.ProductId!.Value, i => i.Quantity ?? 0);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var line in lines)
                {
                    if (!reqByProduct.ContainsKey(line.ProductId)) throw new InvalidOperationException($"INVALID_PRODUCT {line.ProductId}");
                    if (line.ReceivedQuantity < 0) throw new InvalidOperationException("INVALID_RECEIVED_QUANTITY");

                    var inv = await _context.Inventories.FirstOrDefaultAsync(i => i.WarehouseId == order.DestinationWarehouseId && i.ProductId == line.ProductId);
                    if (inv == null)
                    {
                        inv = new Inventory { WarehouseId = order.DestinationWarehouseId, ProductId = line.ProductId, Quantity = 0, ReservedQuantity = 0, LastUpdated = now };
                        _context.Inventories.Add(inv);
                    }

                    inv.Quantity = (inv.Quantity ?? 0) + line.ReceivedQuantity;
                    inv.LastUpdated = now;
                }

                order.Status = TransferStatuses.Completed;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            await AddActivityAsync(actorUserId, string.IsNullOrWhiteSpace(request.Note) ? "TRANSFER_RECEIVED" : $"TRANSFER_RECEIVED:{request.Note}", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> CancelAsync(int companyId, int actorUserId, int transferOrderId, string? reason)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAsync(actorUserId, companyId);

            if (!new[] { TransferStatuses.Draft, TransferStatuses.PendingApproval, TransferStatuses.Approved }.Contains((order.Status ?? "").ToUpperInvariant()))
                throw new InvalidOperationException("Only DRAFT/PENDING_APPROVAL/APPROVED can be cancelled.");

            order.Status = TransferStatuses.Cancelled;
            await _context.SaveChangesAsync();
            await AddActivityAsync(actorUserId, string.IsNullOrWhiteSpace(reason) ? "TRANSFER_CANCELLED" : $"TRANSFER_CANCELLED:{reason}", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<List<TransferOrderListDto>> GetAllAsync(int companyId, int? sourceWarehouseId, int? destinationWarehouseId, string? status)
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

            var data = await query.OrderByDescending(x => x.CreatedAt).ToListAsync();
            return data.Select(MapList).ToList();
        }

        public async Task<TransferOrderDetailDto> GetByIdAsync(int companyId, int transferOrderId)
        {
            var order = await _context.TransferOrders
                .Include(t => t.SourceWarehouse)
                .Include(t => t.DestinationWarehouse)
                .Include(t => t.CreatedByNavigation)
                .Include(t => t.TransferOrderItems)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(t => t.Id == transferOrderId);

            if (order == null) throw new InvalidOperationException("Transfer order not found.");
            if ((order.SourceWarehouse?.CompanyId ?? 0) != companyId || (order.DestinationWarehouse?.CompanyId ?? 0) != companyId)
                throw new InvalidOperationException("Transfer order out of company scope.");

            var timeline = await _context.ActivityLogs
                .Where(a => a.Entity == "TransferOrder" && a.EntityId == order.Id)
                .OrderBy(a => a.Timestamp)
                .Select(a => new TransferOrderTimelineDto(a.Id, a.Action, a.Timestamp, a.UserId, a.User != null ? a.User.FullName : null))
                .ToListAsync();

            return MapDetail(order, timeline);
        }

        public async Task<List<TransferAvailabilityDto>> CheckAvailabilityAsync(int companyId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            var items = await _context.TransferOrderItems.Include(i => i.Product).Where(i => i.TransferOrderId == order.Id).ToListAsync();

            var productIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == order.SourceWarehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync();

            return items.Select(i =>
            {
                var inv = inventories.FirstOrDefault(x => x.ProductId == i.ProductId);
                var available = (inv?.Quantity ?? 0) - (inv?.ReservedQuantity ?? 0);
                var required = i.Quantity ?? 0;
                return new TransferAvailabilityDto(i.ProductId ?? 0, i.Product?.Name, required, available, available >= required);
            }).ToList();
        }

        private static TransferOrderListDto MapList(TransferOrder t)
        {
            var totalItems = t.TransferOrderItems?.Count ?? 0;
            var totalQuantity = t.TransferOrderItems?.Sum(x => x.Quantity ?? 0) ?? 0;
            return new TransferOrderListDto(t.Id, t.SourceWarehouseId, t.SourceWarehouse?.Name, t.DestinationWarehouseId, t.DestinationWarehouse?.Name, t.CreatedBy, t.CreatedByNavigation?.FullName, t.Status, t.CreatedAt, totalItems, totalQuantity);
        }

        private static TransferOrderDetailDto MapDetail(TransferOrder t, IEnumerable<TransferOrderTimelineDto> timeline)
        {
            var items = (t.TransferOrderItems ?? new List<TransferOrderItem>())
                .Select(i => new TransferOrderItemDto(i.Id, i.ProductId, i.Product?.Name, i.Quantity))
                .ToList();

            return new TransferOrderDetailDto(t.Id, t.SourceWarehouseId, t.SourceWarehouse?.Name, t.DestinationWarehouseId, t.DestinationWarehouse?.Name, t.CreatedBy, t.CreatedByNavigation?.FullName, t.Status, t.CreatedAt, items, timeline);
        }

        private async Task<TransferOrder> GetOrderInCompanyAsync(int companyId, int transferOrderId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id", nameof(companyId));
            if (transferOrderId <= 0) throw new ArgumentException("Invalid transferOrderId", nameof(transferOrderId));

            var order = await _context.TransferOrders
                .Include(t => t.SourceWarehouse)
                .Include(t => t.DestinationWarehouse)
                .FirstOrDefaultAsync(t => t.Id == transferOrderId);

            if (order == null) throw new InvalidOperationException("Transfer order not found.");
            if ((order.SourceWarehouse?.CompanyId ?? 0) != companyId || (order.DestinationWarehouse?.CompanyId ?? 0) != companyId)
                throw new InvalidOperationException("Transfer order out of company scope.");

            return order;
        }

        private async Task EnsureManagerAndOwnerAsync(TransferOrder order, int actorUserId, int companyId)
        {
            await EnsureManagerAsync(actorUserId, companyId);
            if (order.CreatedBy != actorUserId) throw new InvalidOperationException("Only creator manager can modify this transfer.");
        }

        private async Task EnsureManagerAsync(int userId, int companyId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) throw new InvalidOperationException("User not found.");
            if ((user.CompanyId ?? 0) != companyId) throw new InvalidOperationException("User out of company scope.");
            if ((user.RoleId ?? 0) != 3) throw new InvalidOperationException("Only Manager(roleId=3).");
        }

        private async Task EnsureStaffAssignedToWarehouseAsync(int userId, int warehouseId, int companyId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) throw new InvalidOperationException("User not found.");
            if ((user.CompanyId ?? 0) != companyId) throw new InvalidOperationException("User out of company scope.");
            if ((user.RoleId ?? 0) != 4) throw new InvalidOperationException("Only Staff(roleId=4).");

            var assigned = await _context.WarehouseAssignments.AsNoTracking().AnyAsync(a => a.WarehouseId == warehouseId && a.UserId == userId);
            if (!assigned) throw new InvalidOperationException("Staff is not assigned to warehouse.");
        }

        private async Task<Warehouse> GetWarehouseInCompanyAsync(int warehouseId, int companyId)
        {
            var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId);
            if (warehouse == null) throw new InvalidOperationException($"Warehouse {warehouseId} not found.");
            if ((warehouse.CompanyId ?? 0) != companyId) throw new InvalidOperationException($"Warehouse {warehouseId} out of company scope.");
            if (string.Equals(warehouse.Status, "inactive", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Warehouse {warehouseId} inactive.");
            return warehouse;
        }

        private async Task EnsureProductInCompanyAsync(int productId, int companyId)
        {
            var ok = await _context.Products.AsNoTracking().AnyAsync(p => p.Id == productId && p.CompanyId == companyId);
            if (!ok) throw new InvalidOperationException($"Product {productId} not found in company.");
        }

        private async Task AddActivityAsync(int userId, string action, int transferOrderId)
        {
            _context.ActivityLogs.Add(new ActivityLog
            {
                UserId = userId,
                Action = action,
                Entity = "TransferOrder",
                EntityId = transferOrderId,
                Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await _context.SaveChangesAsync();
        }

        private static void EnsureCanEdit(string? status)
        {
            if (!string.Equals(status, TransferStatuses.Draft, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(status, TransferStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only DRAFT/REJECTED can be edited.");
        }

        private static void EnsureStatus(TransferOrder order, string expected)
        {
            if (!string.Equals(order.Status, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"INVALID_STATE: expected {expected}, current {order.Status}");
        }

        private static void ValidateCompanyAndUser(int companyId, int userId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id", nameof(companyId));
            if (userId <= 0) throw new ArgumentException("Invalid user id", nameof(userId));
        }

        private static void ValidatePositive(int value, string name)
        {
            if (value <= 0) throw new ArgumentException($"{name} must be positive", name);
        }

        private static void ValidateWarehouses(int sourceWarehouseId, int destinationWarehouseId)
        {
            if (sourceWarehouseId <= 0) throw new ArgumentException("SourceWarehouseId must be positive", nameof(sourceWarehouseId));
            if (destinationWarehouseId <= 0) throw new ArgumentException("DestinationWarehouseId must be positive", nameof(destinationWarehouseId));
            if (sourceWarehouseId == destinationWarehouseId) throw new InvalidOperationException("Source and destination must be different.");
        }
    }
}
