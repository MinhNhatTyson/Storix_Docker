using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.Service.Implementation
{
    public class WarehouseTransferService : IWarehouseTransferService
    {
        private readonly IWarehouseTransferRepository _warehouseTransferRepository;
        private readonly ILogger<WarehouseTransferService> _logger;

        public WarehouseTransferService(IWarehouseTransferRepository warehouseTransferRepository, ILogger<WarehouseTransferService> logger)
        {
            _warehouseTransferRepository = warehouseTransferRepository;
            _logger = logger;
        }

        public async Task<TransferOrderDetailDto> CreateAsync(int companyId, int createdBy, CreateTransferOrderRequest request)
        {
            _logger.LogInformation("CreateAsync started. companyId={CompanyId}, createdBy={CreatedBy}, sourceWarehouseId={SourceWarehouseId}, destinationWarehouseId={DestinationWarehouseId}, originWarehouseStaffId={OriginWarehouseStaffId}, itemCount={ItemCount}",
                companyId, createdBy, request.SourceWarehouseId, request.DestinationWarehouseId, request.OriginWarehouseStaffId, request.Items?.Count() ?? 0);

            ValidateCompanyAndUser(companyId, createdBy);
            ValidateWarehouses(request.SourceWarehouseId, request.DestinationWarehouseId);

            _logger.LogInformation("Validating manager, origin staff, and warehouses for transfer creation.");
            await EnsureManagerAsync(createdBy, companyId);
            await GetWarehouseInCompanyAsync(request.SourceWarehouseId, companyId);
            await GetWarehouseInCompanyAsync(request.DestinationWarehouseId, companyId);
            await EnsureOriginWarehouseStaffAsync(request.OriginWarehouseStaffId, request.SourceWarehouseId, companyId);

            var items = request.Items?.ToList() ?? new List<CreateTransferOrderItemRequest>();
            if (!items.Any())
                throw new ArgumentException("Items are required.", nameof(request.Items));

            foreach (var line in items)
            {
                _logger.LogInformation("Validating transfer item. productId={ProductId}, quantity={Quantity}", line.ProductId, line.Quantity);
                ValidatePositive(line.ProductId, nameof(line.ProductId));
                ValidatePositive(line.Quantity, nameof(line.Quantity));
                await EnsureProductInCompanyAsync(line.ProductId, companyId);
            }

            var entity = new TransferOrder
            {
                SourceWarehouseId = request.SourceWarehouseId,
                DestinationWarehouseId = request.DestinationWarehouseId,
                CreatedBy = createdBy,
                Status = TransferStatuses.PendingApproval,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            try
            {
                _logger.LogInformation("Saving transfer order header.");
                await _warehouseTransferRepository.CreateTransferOrderAsync(entity);
                _logger.LogInformation("Transfer order header saved. transferOrderId={TransferOrderId}", entity.Id);

                foreach (var line in items)
                {
                    _logger.LogInformation("Adding transfer item. transferOrderId={TransferOrderId}, productId={ProductId}, quantity={Quantity}", entity.Id, line.ProductId, line.Quantity);
                    _warehouseTransferRepository.AddTransferOrderItem(new TransferOrderItem
                    {
                        TransferOrderId = entity.Id,
                        ProductId = line.ProductId,
                        Quantity = line.Quantity
                    });
                }

                _logger.LogInformation("Saving transfer items and activity log.");
                await _warehouseTransferRepository.SaveChangesAsync();
                await AddActivityAsync(createdBy, $"ORIGIN_STAFF:{request.OriginWarehouseStaffId}", entity.Id);
                await AddActivityAsync(createdBy, "TRANSFER_CREATED_AND_SUBMITTED", entity.Id);
                _logger.LogInformation("Transfer created successfully. transferOrderId={TransferOrderId}", entity.Id);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "DbUpdateException while creating transfer. companyId={CompanyId}, createdBy={CreatedBy}", companyId, createdBy);
                throw new InvalidOperationException(ex.InnerException?.Message ?? ex.Message, ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating transfer. companyId={CompanyId}, createdBy={CreatedBy}", companyId, createdBy);
                throw new InvalidOperationException($"Unable to create transfer: {ex.Message}", ex);
            }

            return await GetByIdAsync(companyId, entity.Id);
        }

        public async Task<TransferOrderDetailDto> DecideAsync(int companyId, int actorUserId, int transferOrderId, TransferDecisionRequest request)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureAdminAsync(actorUserId, companyId);

            if (request.IsApprove)
            {
                if (!string.Equals(order.Status, TransferStatuses.PendingApproval, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only PENDING_APPROVAL can be approved.");

                var items = await _warehouseTransferRepository.GetTransferItemsByOrderIdAsync(order.Id);
                if (!items.Any())
                    throw new InvalidOperationException("Transfer must contain at least one item.");

                var productIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
                var inventories = await _warehouseTransferRepository.GetInventoriesByWarehouseAndProductsAsync(order.SourceWarehouseId, productIds);

                foreach (var line in items)
                {
                    var inv = inventories.FirstOrDefault(i => i.ProductId == line.ProductId);
                    var qty = line.Quantity ?? 0;
                    var available = (inv?.Quantity ?? 0) - (inv?.ReservedQuantity ?? 0);
                    if (qty <= 0 || inv == null || available < qty)
                        throw new InvalidOperationException($"OUT_OF_STOCK ProductId={line.ProductId}, available={available}, required={qty}");
                }

                var originStaffId = await ResolveOriginStaffIdAsync(order.Id);
                if (!originStaffId.HasValue)
                    throw new InvalidOperationException("Origin staff not found for this transfer.");

                await _warehouseTransferRepository.ApproveTransferAsync(
                    order,
                    items,
                    inventories,
                    actorUserId,
                    originStaffId.Value,
                    null);

                await EnsureTicketAndItemLinksAsync(order);
                return await GetByIdAsync(companyId, order.Id);
            }

            await _warehouseTransferRepository.RejectTransferAsync(order, actorUserId, request.Reason);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> ApproveAsync(int companyId, int actorUserId, int transferOrderId, int? receiverStaffId = null)
        {
            return await DecideAsync(companyId, actorUserId, transferOrderId, new TransferDecisionRequest(true));
        }

        public async Task<TransferOrderDetailDto> RejectAsync(int companyId, int actorUserId, int transferOrderId, string? reason = null)
        {
            return await DecideAsync(companyId, actorUserId, transferOrderId, new TransferDecisionRequest(false, reason));
        }

        public async Task<List<TransferOrderListDto>> GetAllAsync(int companyId, int? sourceWarehouseId, int? destinationWarehouseId, string? status)
        {
            var data = await _warehouseTransferRepository.GetTransferOrdersByCompanyAsync(companyId, sourceWarehouseId, destinationWarehouseId, status);
            return data.Select(MapList).ToList();
        }

        public async Task<List<TransferOrderListDto>> GetAllBySourceWarehouseAsync(int companyId, int warehouseId, string? status)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouseId", nameof(warehouseId));

            await GetWarehouseInCompanyAsync(warehouseId, companyId);

            var data = await _warehouseTransferRepository.GetTransferOrdersByCompanyAsync(companyId, warehouseId, null, status);
            return data.Select(MapList).ToList();
        }

        public async Task<TransferOrderDetailDto> GetByIdAsync(int companyId, int transferOrderId)
        {
            var order = await _warehouseTransferRepository.GetTransferOrderDetailAsync(transferOrderId);
            if (order == null) throw new InvalidOperationException("Transfer order not found.");
            if ((order.SourceWarehouse?.CompanyId ?? 0) != companyId || (order.DestinationWarehouse?.CompanyId ?? 0) != companyId)
                throw new InvalidOperationException("Transfer order out of company scope.");

            await EnsureTicketAndItemLinksAsync(order);
            var timeline = (await _warehouseTransferRepository.GetActivitiesAsync(order.Id))
                .Select(a => new TransferOrderTimelineDto(a.Id, a.Action, a.Timestamp, a.UserId, a.User?.FullName))
                .ToList();

            return MapDetail(order, timeline);
        }

        public async Task<TransferOrderDetailDto> UpdateItemsAsync(int companyId, int actorUserId, int transferOrderId, UpdateTransferOrderItemsRequest request)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAsync(actorUserId, companyId);

            if (!string.Equals(order.Status, TransferStatuses.PendingApproval, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only PENDING_APPROVAL can be edited.");

            var items = request.Items?.ToList() ?? new List<CreateTransferOrderItemRequest>();
            if (!items.Any()) throw new ArgumentException("Items are required.", nameof(request.Items));

            foreach (var line in items)
            {
                ValidatePositive(line.ProductId, nameof(line.ProductId));
                ValidatePositive(line.Quantity, nameof(line.Quantity));
                await EnsureProductInCompanyAsync(line.ProductId, companyId);
            }

            var existing = await _warehouseTransferRepository.GetTransferItemsByOrderIdAsync(order.Id);
            foreach (var e in existing) _warehouseTransferRepository.RemoveTransferOrderItem(e);
            await _warehouseTransferRepository.SaveChangesAsync();

            foreach (var line in items)
            {
                _warehouseTransferRepository.AddTransferOrderItem(new TransferOrderItem
                {
                    TransferOrderId = order.Id,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity
                });
            }
            await _warehouseTransferRepository.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_ITEMS_UPDATED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> RemoveItemAsync(int companyId, int actorUserId, int transferOrderId, int itemId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAsync(actorUserId, companyId);

            if (!string.Equals(order.Status, TransferStatuses.PendingApproval, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only PENDING_APPROVAL can be edited.");

            await _warehouseTransferRepository.RemoveTransferOrderItemAsync(order.Id, itemId);
            await AddActivityAsync(actorUserId, $"TRANSFER_ITEM_REMOVED:{itemId}", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<List<TransferAvailabilityDto>> CheckAvailabilityAsync(int companyId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            var items = await _warehouseTransferRepository.GetTransferItemsWithProductByOrderIdAsync(order.Id);

            var productIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
            var inventories = await _warehouseTransferRepository.GetInventoriesByWarehouseAndProductsAsync(order.SourceWarehouseId, productIds);

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
            return new TransferOrderListDto(t.Id, t.SourceWarehouseId, t.SourceWarehouse?.Name, t.DestinationWarehouseId, t.DestinationWarehouse?.Name, t.CreatedBy, t.CreatedByNavigation?.FullName, t.Status, t.CreatedAt, t.OutboundTicketId, t.InboundTicketId, totalItems, totalQuantity);
        }

        private static TransferOrderDetailDto MapDetail(TransferOrder t, IEnumerable<TransferOrderTimelineDto> timeline)
        {
            var items = (t.TransferOrderItems ?? new List<TransferOrderItem>())
                .Select(i => new TransferOrderItemDto(i.Id, i.ProductId, i.Product?.Name, i.Product?.Image, i.Quantity, i.OutboundOrderItemId, i.InboundOrderItemId))
                .ToList();

            return new TransferOrderDetailDto(t.Id, t.SourceWarehouseId, t.SourceWarehouse?.Name, t.DestinationWarehouseId, t.DestinationWarehouse?.Name, t.CreatedBy, t.CreatedByNavigation?.FullName, t.Status, t.CreatedAt, t.OutboundTicketId, t.InboundTicketId, items, timeline);
        }

        private async Task<TransferOrder> GetOrderInCompanyAsync(int companyId, int transferOrderId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id", nameof(companyId));
            if (transferOrderId <= 0) throw new ArgumentException("Invalid transferOrderId", nameof(transferOrderId));

            var order = await _warehouseTransferRepository.GetTransferOrderWithWarehousesAsync(transferOrderId);
            if (order == null) throw new InvalidOperationException("Transfer order not found.");
            if ((order.SourceWarehouse?.CompanyId ?? 0) != companyId || (order.DestinationWarehouse?.CompanyId ?? 0) != companyId)
                throw new InvalidOperationException("Transfer order out of company scope.");

            return order;
        }

        private async Task EnsureManagerAsync(int userId, int companyId)
        {
            var user = await _warehouseTransferRepository.GetUserByIdAsync(userId);
            if (user == null) throw new InvalidOperationException("User not found.");
            if ((user.CompanyId ?? 0) != companyId) throw new InvalidOperationException("User out of company scope.");
            if ((user.RoleId ?? 0) != 3) throw new InvalidOperationException("Only Manager(roleId=3).");
        }

        private async Task EnsureAdminAsync(int userId, int companyId)
        {
            var user = await _warehouseTransferRepository.GetUserByIdAsync(userId);
            if (user == null) throw new InvalidOperationException("User not found.");
            if ((user.CompanyId ?? 0) != companyId) throw new InvalidOperationException("User out of company scope.");
            if ((user.RoleId ?? 0) != 2) throw new InvalidOperationException("Only Admin(roleId=2).");
        }

        private async Task EnsureOriginWarehouseStaffAsync(int userId, int warehouseId, int companyId)
        {
            var user = await _warehouseTransferRepository.GetUserByIdAsync(userId);
            if (user == null) throw new InvalidOperationException("User not found.");
            if ((user.CompanyId ?? 0) != companyId) throw new InvalidOperationException("User out of company scope.");
            if ((user.RoleId ?? 0) != 4) throw new InvalidOperationException("Only Staff(roleId=4).");

            var assigned = await _warehouseTransferRepository.IsStaffAssignedToWarehouseAsync(userId, warehouseId);
            if (!assigned) throw new InvalidOperationException("Origin warehouse staff is not assigned to source warehouse.");
        }

        private async Task<Warehouse> GetWarehouseInCompanyAsync(int warehouseId, int companyId)
        {
            var warehouse = await _warehouseTransferRepository.GetWarehouseByIdAsync(warehouseId);
            if (warehouse == null) throw new InvalidOperationException($"Warehouse {warehouseId} not found.");
            if ((warehouse.CompanyId ?? 0) != companyId) throw new InvalidOperationException($"Warehouse {warehouseId} out of company scope.");
            if (string.Equals(warehouse.Status, "inactive", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Warehouse {warehouseId} inactive.");
            return warehouse;
        }

        private async Task EnsureProductInCompanyAsync(int productId, int companyId)
        {
            var ok = await _warehouseTransferRepository.ProductInCompanyAsync(productId, companyId);
            if (!ok) throw new InvalidOperationException($"Product {productId} not found in company.");
        }

        private async Task AddActivityAsync(int userId, string action, int transferOrderId)
        {
            await _warehouseTransferRepository.AddActivityAsync(userId, action, transferOrderId);
        }

        private async Task EnsureTicketAndItemLinksAsync(TransferOrder order)
        {
            var ticketChanged = false;

            if (!order.OutboundTicketId.HasValue)
            {
                var outboundId = await ResolveLinkedTicketIdFromActivityAsync(order.Id, "LINK_OUTBOUND:");
                if (outboundId.HasValue)
                {
                    order.OutboundTicketId = outboundId.Value;
                    ticketChanged = true;
                }
            }

            if (!order.InboundTicketId.HasValue)
            {
                var inboundId = await ResolveLinkedTicketIdFromActivityAsync(order.Id, "LINK_INBOUND:");
                if (inboundId.HasValue)
                {
                    order.InboundTicketId = inboundId.Value;
                    ticketChanged = true;
                }
            }

            if (ticketChanged)
                await _warehouseTransferRepository.SaveChangesAsync();

            if (order.OutboundTicketId.HasValue || order.InboundTicketId.HasValue)
            {
                await _warehouseTransferRepository.BackfillTransferItemLinksAsync(order.Id, order.OutboundTicketId, order.InboundTicketId);
            }
        }

        private async Task<int?> ResolveLinkedTicketIdFromActivityAsync(int transferOrderId, string prefix)
        {
            var action = await _warehouseTransferRepository.GetLatestActivityActionAsync(transferOrderId, prefix);
            if (string.IsNullOrWhiteSpace(action)) return null;
            var parts = action.Split(':');
            if (parts.Length != 2) return null;
            return int.TryParse(parts[1], out var id) ? id : null;
        }

        private async Task<int?> ResolveOriginStaffIdAsync(int transferOrderId)
        {
            var action = await _warehouseTransferRepository.GetLatestActivityActionAsync(transferOrderId, "ORIGIN_STAFF:");
            if (string.IsNullOrWhiteSpace(action)) return null;
            var parts = action.Split(':');
            if (parts.Length != 2) return null;
            return int.TryParse(parts[1], out var id) ? id : null;
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
