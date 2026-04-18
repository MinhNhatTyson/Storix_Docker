using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.Service.Implementation
{
    public class WarehouseTransferService : IWarehouseTransferService
    {
        private readonly IWarehouseTransferRepository _warehouseTransferRepository;

        public WarehouseTransferService(IWarehouseTransferRepository warehouseTransferRepository)
        {
            _warehouseTransferRepository = warehouseTransferRepository;
        }

        public async Task<TransferOrderDetailDto> CreateDraftAsync(int companyId, int createdBy, CreateTransferOrderRequest request)
        {
            ValidateCompanyAndUser(companyId, createdBy);
            ValidateWarehouses(request.SourceWarehouseId, request.DestinationWarehouseId);

            await EnsureManagerAsync(createdBy, companyId);
            await GetWarehouseInCompanyAsync(request.SourceWarehouseId, companyId);
            await GetWarehouseInCompanyAsync(request.DestinationWarehouseId, companyId);

            if (request.CarrierUserId.HasValue && request.CarrierUserId.Value > 0)
                await EnsureStaffAssignedToWarehouseAsync(request.CarrierUserId.Value, request.SourceWarehouseId, companyId);

            var entity = new TransferOrder
            {
                SourceWarehouseId = request.SourceWarehouseId,
                DestinationWarehouseId = request.DestinationWarehouseId,
                CreatedBy = createdBy,
                Status = TransferStatuses.Draft,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            await _warehouseTransferRepository.CreateTransferOrderAsync(entity);

            if (request.CarrierUserId.HasValue && request.CarrierUserId.Value > 0)
                await AddActivityAsync(createdBy, $"CARRIER:{request.CarrierUserId.Value}", entity.Id);

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
            await _warehouseTransferRepository.SaveChangesAsync();

            if (request.CarrierUserId.HasValue && request.CarrierUserId.Value > 0)
                await AddActivityAsync(actorUserId, $"CARRIER:{request.CarrierUserId.Value}", order.Id);

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

            var existing = await _warehouseTransferRepository.GetTransferOrderItemByProductAsync(order.Id, request.ProductId);

            if (existing == null)
            {
                _warehouseTransferRepository.AddTransferOrderItem(new TransferOrderItem
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

            await _warehouseTransferRepository.SaveChangesAsync();
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

            var item = await _warehouseTransferRepository.GetTransferOrderItemAsync(order.Id, itemId);
            if (item == null) throw new InvalidOperationException("Transfer item not found.");

            item.ProductId = request.ProductId;
            item.Quantity = request.Quantity;
            await _warehouseTransferRepository.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_ITEM_UPDATED", order.Id);

            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> RemoveItemAsync(int companyId, int actorUserId, int transferOrderId, int itemId)
        {
            ValidatePositive(itemId, nameof(itemId));

            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            EnsureCanEdit(order.Status);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);

            var item = await _warehouseTransferRepository.GetTransferOrderItemAsync(order.Id, itemId);
            if (item == null) throw new InvalidOperationException("Transfer item not found.");

            _warehouseTransferRepository.RemoveTransferOrderItem(item);
            await _warehouseTransferRepository.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_ITEM_REMOVED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<List<TransferStaffSuggestionDto>> SuggestStaffAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);

            var sourceWarehouseId = order.SourceWarehouseId ?? 0;
            if (sourceWarehouseId <= 0)
                throw new InvalidOperationException("Transfer source warehouse is invalid.");

            var assignedStaff = await _warehouseTransferRepository.GetAssignedStaffInWarehouseAsync(sourceWarehouseId, companyId);

            var suggestions = new List<TransferStaffSuggestionDto>();
            foreach (var staff in assignedStaff)
            {
                if (staff == null) continue;
                var staffId = staff.Id;

                var activeTaskCount = await GetActiveTransferTaskCountAsync(companyId, staffId);
                var assignedWarehouseCount = await _warehouseTransferRepository.CountWarehouseAssignmentsByUserAsync(staffId);

                var score = Math.Max(0, 100 - (activeTaskCount * 15)) + Math.Min(assignedWarehouseCount, 5);
                var reason = activeTaskCount == 0
                    ? "Rảnh, phù hợp để nhận phiếu chuyển mới."
                    : $"Đang xử lý {activeTaskCount} phiếu chuyển.";

                suggestions.Add(new TransferStaffSuggestionDto(
                    staffId,
                    staff.FullName,
                    staff.Email,
                    assignedWarehouseCount,
                    activeTaskCount,
                    score,
                    reason));
            }

            return suggestions
                .OrderByDescending(x => x.SuggestionScore)
                .ThenBy(x => x.ActiveTransferTaskCount)
                .ThenBy(x => x.FullName)
                .ToList();
        }

        public async Task<TransferOrderDetailDto> AssignCarrierAsync(int companyId, int actorUserId, int transferOrderId, int carrierUserId)
        {
            ValidatePositive(carrierUserId, nameof(carrierUserId));

            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            EnsureCanEdit(order.Status);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);
            await EnsureStaffAssignedToWarehouseAsync(carrierUserId, order.SourceWarehouseId ?? 0, companyId);

            await AddActivityAsync(actorUserId, $"CARRIER:{carrierUserId}", order.Id);
            await AddActivityAsync(actorUserId, "TRANSFER_CARRIER_ASSIGNED", order.Id);

            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> SubmitAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAndOwnerAsync(order, actorUserId, companyId);

            if (!new[] { TransferStatuses.Draft, TransferStatuses.Rejected }.Contains((order.Status ?? "").ToUpperInvariant()))
                throw new InvalidOperationException("Only DRAFT/REJECTED can be submitted.");

            var hasItems = await _warehouseTransferRepository.AnyTransferItemsAsync(order.Id);
            if (!hasItems) throw new InvalidOperationException("Transfer must contain at least one item.");

            order.Status = TransferStatuses.PendingApproval;
            await _warehouseTransferRepository.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_SUBMITTED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> ApproveAsync(int companyId, int actorUserId, int transferOrderId, int? receiverStaffId = null)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAsync(actorUserId, companyId);
            if (!string.Equals(order.Status, TransferStatuses.PendingApproval, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only PENDING_APPROVAL can be approved.");

            var items = await _warehouseTransferRepository.GetTransferItemsByOrderIdAsync(order.Id);
            if (!items.Any()) throw new InvalidOperationException("Transfer must contain at least one item.");

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

            if (receiverStaffId.HasValue && receiverStaffId.Value > 0)
                await EnsureStaffAssignedToWarehouseAsync(receiverStaffId.Value, order.DestinationWarehouseId ?? 0, companyId);

            var carrierId = await ResolveCarrierUserIdAsync(order.Id);

            await _warehouseTransferRepository.ApproveTransferAsync(
                order,
                items,
                inventories,
                actorUserId,
                receiverStaffId,
                carrierId);

            await EnsureTicketAndItemLinksAsync(order);
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
            await _warehouseTransferRepository.SaveChangesAsync();
            await AddActivityAsync(actorUserId, $"TRANSFER_REJECTED:{reason}", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> StartPickingAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.SourceWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.Approved);

            order.Status = TransferStatuses.Picking;
            await _warehouseTransferRepository.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_PICKING_STARTED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> MarkPackedAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.SourceWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.Picking);

            order.Status = TransferStatuses.Packed;
            await _warehouseTransferRepository.SaveChangesAsync();
            await AddActivityAsync(actorUserId, "TRANSFER_PACKED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> ShipAsync(int companyId, int actorUserId, int transferOrderId)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.SourceWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.Packed);
            await _warehouseTransferRepository.ShipTransferAsync(order);

            await AddActivityAsync(actorUserId, "TRANSFER_SHIPPED", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> ReceiveAsync(int companyId, int actorUserId, int transferOrderId, ReceiveTransferOrderRequest request)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.DestinationWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.InTransit);
            await EnsureTicketAndItemLinksAsync(order);

            var lines = request.Items?.ToList() ?? new List<ReceiveTransferItemRequest>();
            if (!lines.Any()) throw new InvalidOperationException("receive items required");

            var orderItems = await _warehouseTransferRepository.GetTransferItemsByOrderIdAsync(order.Id);
            var reqByProduct = orderItems.Where(i => i.ProductId.HasValue).ToDictionary(i => i.ProductId!.Value, i => i.Quantity ?? 0);
            await _warehouseTransferRepository.ReceiveTransferAsync(
                order,
                lines.Select(x => (x.ProductId, x.ReceivedQuantity)).ToList(),
                reqByProduct);

            await AddActivityAsync(actorUserId, string.IsNullOrWhiteSpace(request.Note) ? "TRANSFER_RECEIVED" : $"TRANSFER_RECEIVED:{request.Note}", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> QualityCheckAsync(int companyId, int actorUserId, int transferOrderId, TransferQualityCheckRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureStaffAssignedToWarehouseAsync(actorUserId, order.SourceWarehouseId ?? 0, companyId);
            EnsureStatus(order, TransferStatuses.Approved);
            await EnsureTicketAndItemLinksAsync(order);

            var items = request.Items?.ToList() ?? new List<TransferQualityCheckItemRequest>();
            if (!items.Any()) throw new InvalidOperationException("quality items required");

            var orderItems = await _warehouseTransferRepository.GetTransferItemsByOrderIdAsync(order.Id);
            var reqByProduct = orderItems.Where(i => i.ProductId.HasValue)
                .ToDictionary(i => i.ProductId!.Value, i => i.Quantity ?? 0);

            foreach (var line in items)
            {
                if (!reqByProduct.ContainsKey(line.ProductId))
                    throw new InvalidOperationException($"INVALID_PRODUCT {line.ProductId}");
                if (line.OkQuantity < 0 || line.BadQuantity < 0)
                    throw new InvalidOperationException("INVALID_QUANTITY");
                if (line.OkQuantity + line.BadQuantity > reqByProduct[line.ProductId])
                    throw new InvalidOperationException($"QUALITY_OVERFLOW ProductId={line.ProductId}");
            }

            var outboundId = order.OutboundTicketId ?? await ResolveLinkedOutboundIdAsync(order.Id);
            if (outboundId == null)
                throw new InvalidOperationException("Linked outbound order not found.");

            var outbound = await _warehouseTransferRepository.GetOutboundOrderWithItemsAsync(outboundId.Value);
            if (outbound == null)
                throw new InvalidOperationException("Linked outbound order not found.");

            var status = items.Any(i => i.BadQuantity > 0) ? TransferStatuses.QualityIssue : TransferStatuses.QualityChecked;
            order.Status = status;
            await _warehouseTransferRepository.SaveChangesAsync();

            try
            {
                outbound.Status = items.Any(i => i.BadQuantity > 0) ? "IssueReported" : "QualityCheck";
                await _warehouseTransferRepository.SaveChangesAsync();
            }
            catch
            {
                // best effort outbound update
            }

            var note = string.IsNullOrWhiteSpace(request.Note) ? "QUALITY_CHECK" : $"QUALITY_CHECK:{request.Note}";
            await AddActivityAsync(actorUserId, note, order.Id);

            foreach (var line in items)
            {
                var itemNote = string.IsNullOrWhiteSpace(line.Note)
                    ? $"QUALITY_ITEM:{line.ProductId}:{line.OkQuantity}:{line.BadQuantity}"
                    : $"QUALITY_ITEM:{line.ProductId}:{line.OkQuantity}:{line.BadQuantity}:{line.Note}";
                await AddActivityAsync(actorUserId, itemNote, order.Id);
            }

            await AddActivityAsync(actorUserId, $"OUTBOUND_QUALITY_UPDATED:{outbound.Id}", order.Id);

            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<TransferOrderDetailDto> CancelAsync(int companyId, int actorUserId, int transferOrderId, string? reason)
        {
            var order = await GetOrderInCompanyAsync(companyId, transferOrderId);
            await EnsureManagerAsync(actorUserId, companyId);

            if (!new[] { TransferStatuses.Draft, TransferStatuses.PendingApproval, TransferStatuses.Approved }.Contains((order.Status ?? "").ToUpperInvariant()))
                throw new InvalidOperationException("Only DRAFT/PENDING_APPROVAL/APPROVED can be cancelled.");

            order.Status = TransferStatuses.Cancelled;
            await _warehouseTransferRepository.SaveChangesAsync();
            await AddActivityAsync(actorUserId, string.IsNullOrWhiteSpace(reason) ? "TRANSFER_CANCELLED" : $"TRANSFER_CANCELLED:{reason}", order.Id);
            return await GetByIdAsync(companyId, order.Id);
        }

        public async Task<List<TransferOrderListDto>> GetAllAsync(int companyId, int? sourceWarehouseId, int? destinationWarehouseId, string? status)
        {
            var data = await _warehouseTransferRepository.GetTransferOrdersByCompanyAsync(companyId, sourceWarehouseId, destinationWarehouseId, status);
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
            return new TransferOrderListDto(t.Id, t.SourceWarehouseId, t.SourceWarehouse?.Name, t.DestinationWarehouseId, t.DestinationWarehouse?.Name, t.CreatedBy, t.CreatedByNavigation?.FullName, t.Status, t.CreatedAt, totalItems, totalQuantity);
        }

        private static TransferOrderDetailDto MapDetail(TransferOrder t, IEnumerable<TransferOrderTimelineDto> timeline)
        {
            var items = (t.TransferOrderItems ?? new List<TransferOrderItem>())
                .Select(i => new TransferOrderItemDto(i.Id, i.ProductId, i.Product?.Name, i.Quantity, i.OutboundOrderItemId, i.InboundOrderItemId))
                .ToList();

            return new TransferOrderDetailDto(t.Id, t.SourceWarehouseId, t.SourceWarehouse?.Name, t.DestinationWarehouseId, t.DestinationWarehouse?.Name, t.CreatedBy, t.CreatedByNavigation?.FullName, t.Status, t.CreatedAt, items, timeline);
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

        private async Task EnsureManagerAndOwnerAsync(TransferOrder order, int actorUserId, int companyId)
        {
            await EnsureManagerAsync(actorUserId, companyId);
            if (order.CreatedBy != actorUserId) throw new InvalidOperationException("Only creator manager can modify this transfer.");
        }

        private async Task EnsureManagerAsync(int userId, int companyId)
        {
            var user = await _warehouseTransferRepository.GetUserByIdAsync(userId);
            if (user == null) throw new InvalidOperationException("User not found.");
            if ((user.CompanyId ?? 0) != companyId) throw new InvalidOperationException("User out of company scope.");
            if ((user.RoleId ?? 0) != 3) throw new InvalidOperationException("Only Manager(roleId=3).");
        }

        private async Task EnsureStaffAssignedToWarehouseAsync(int userId, int warehouseId, int companyId)
        {
            var user = await _warehouseTransferRepository.GetUserByIdAsync(userId);
            if (user == null) throw new InvalidOperationException("User not found.");
            if ((user.CompanyId ?? 0) != companyId) throw new InvalidOperationException("User out of company scope.");
            if ((user.RoleId ?? 0) != 4) throw new InvalidOperationException("Only Staff(roleId=4).");

            var assigned = await _warehouseTransferRepository.IsStaffAssignedToWarehouseAsync(userId, warehouseId);
            if (!assigned) throw new InvalidOperationException("Staff is not assigned to warehouse.");
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

        private async Task<int?> ResolveLinkedOutboundIdAsync(int transferOrderId)
        {
            return await ResolveLinkedTicketIdFromActivityAsync(transferOrderId, "LINK_OUTBOUND:");
        }

        private async Task<int?> ResolveLinkedInboundIdAsync(int transferOrderId)
        {
            return await ResolveLinkedTicketIdFromActivityAsync(transferOrderId, "LINK_INBOUND:");
        }

        private async Task<int?> ResolveLinkedTicketIdFromActivityAsync(int transferOrderId, string prefix)
        {
            var action = await _warehouseTransferRepository.GetLatestActivityActionAsync(transferOrderId, prefix);
            if (string.IsNullOrWhiteSpace(action)) return null;
            var parts = action.Split(':');
            if (parts.Length != 2) return null;
            return int.TryParse(parts[1], out var id) ? id : null;
        }

        private async Task EnsureTicketAndItemLinksAsync(TransferOrder order)
        {
            var ticketChanged = false;

            if (!order.OutboundTicketId.HasValue)
            {
                var outboundId = await ResolveLinkedOutboundIdAsync(order.Id);
                if (outboundId.HasValue)
                {
                    order.OutboundTicketId = outboundId.Value;
                    ticketChanged = true;
                }
            }

            if (!order.InboundTicketId.HasValue)
            {
                var inboundId = await ResolveLinkedInboundIdAsync(order.Id);
                if (inboundId.HasValue)
                {
                    order.InboundTicketId = inboundId.Value;
                    ticketChanged = true;
                }
            }

            if (ticketChanged)
            {
                await _warehouseTransferRepository.SaveChangesAsync();
            }

            if (order.OutboundTicketId.HasValue || order.InboundTicketId.HasValue)
            {
                await _warehouseTransferRepository.BackfillTransferItemLinksAsync(
                    order.Id,
                    order.OutboundTicketId,
                    order.InboundTicketId);
            }
        }

        private async Task<int?> ResolveCarrierUserIdAsync(int transferOrderId)
        {
            var action = await _warehouseTransferRepository.GetLatestActivityActionAsync(transferOrderId, "CARRIER:");

            if (string.IsNullOrWhiteSpace(action)) return null;
            var parts = action.Split(':');
            if (parts.Length != 2) return null;
            return int.TryParse(parts[1], out var id) ? id : null;
        }

        private async Task<int> GetActiveTransferTaskCountAsync(int companyId, int staffUserId)
        {
            var activeStatuses = new[]
            {
                TransferStatuses.Approved,
                TransferStatuses.Picking,
                TransferStatuses.Packed,
                TransferStatuses.InTransit,
                TransferStatuses.QualityChecked,
                TransferStatuses.QualityIssue
            };

            var orderIds = await _warehouseTransferRepository.GetTransferOrderIdsByCarrierAsync(staffUserId);

            if (!orderIds.Any()) return 0;

            return await _warehouseTransferRepository.CountActiveTransfersByOrderIdsAsync(companyId, orderIds, activeStatuses);
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
