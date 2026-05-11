// ============================================================
// FILE: Storix_BE.Service/Implementation/InboundReturnService.cs
// ── Create this as a NEW file ──
// ============================================================

using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class InboundReturnService : IInboundReturnService
    {
        private readonly IInboundReturnRepository _repo;
        private readonly IActivityLogRepository _activityLogRepo;
        private readonly INotificationService _notificationService;

        public InboundReturnService(
            IInboundReturnRepository repo,
            IActivityLogRepository activityLogRepo,
            INotificationService notificationService)
        {
            _repo = repo;
            _activityLogRepo = activityLogRepo;
            _notificationService = notificationService;
        }

        // ── Create ───────────────────────────────────────────────────────────

        public async Task<InboundReturnOrderDto> CreateReturnOrderAsync(
            int companyId,
            int inboundOrderId,
            CreateReturnOrderRequest request)
        {
            if (companyId <= 0)
                throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (inboundOrderId <= 0)
                throw new ArgumentException("Invalid inboundOrderId.", nameof(inboundOrderId));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.CreatedBy <= 0)
                throw new ArgumentException("Invalid CreatedBy.", nameof(request.CreatedBy));

            var itemList = request.Items?.ToList()
                ?? throw new InvalidOperationException(
                    "Return order must contain at least one item.");

            if (!itemList.Any())
                throw new InvalidOperationException(
                    "Return order must contain at least one item.");

            foreach (var item in itemList)
            {
                if (item.InboundOrderItemId <= 0)
                    throw new ArgumentException(
                        "Each return item must have a valid InboundOrderItemId.");
                if (item.QualityCheckId <= 0)
                    throw new ArgumentException(
                        "Each return item must have a valid QualityCheckId.");
                if (item.ReturnQuantity <= 0)
                    throw new ArgumentException(
                        $"ReturnQuantity must be > 0 " +
                        $"(InboundOrderItemId = {item.InboundOrderItemId}).");
            }

            // Retrieve the order first so we can resolve the FailureReason
            // default (copy from QC) when the caller omits it.
            var order = await _repo
                .GetInboundOrderForReturnAsync(companyId, inboundOrderId)
                .ConfigureAwait(false);

            // Build repo DTOs — resolve missing FailureReason from the QC records
            // already loaded via GetInboundOrderForReturnAsync navigation.
            var repoDtos = itemList.Select(i =>
            {
                // Try to find the matching QC record via navigation for default reason
                // (we load QC records separately in the repo; here we just pass through
                //  what the caller provided — the repo validates quantities).
                return new IInboundReturnRepository.ReturnOrderItemSaveDto(
                    InboundOrderItemId: i.InboundOrderItemId,
                    QualityCheckId: i.QualityCheckId,
                    ProductId: 0,   // repo resolves from QC / order item
                    ReturnQuantity: i.ReturnQuantity,
                    FailureReason: i.FailureReason);
            }).ToList();

            var returnOrder = await _repo.CreateReturnOrderAsync(
                inboundOrderId,
                request.CreatedBy,
                request.Reason,
                repoDtos).ConfigureAwait(false);

            // Activity log
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = request.CreatedBy,
                Action = "Create Inbound Return Order",
                Entity = "InboundReturnOrder",
                EntityId = returnOrder.Id,
                Timestamp = now
            }).ConfigureAwait(false);

            // Notify managers (best-effort)
            try
            {
                var companyIdForNotif = returnOrder.Warehouse?.CompanyId
                    ?? returnOrder.InboundOrder?.Warehouse?.CompanyId;

                if (companyIdForNotif.HasValue && companyIdForNotif.Value > 0)
                {
                    await _notificationService.SendNotificationToManagersAsync(
                        companyIdForNotif.Value,
                        title: "Return order pending approval",
                        message: $"A return order #{returnOrder.Id} for " +
                                        $"inbound order #{inboundOrderId} is awaiting " +
                                        $"your approval.",
                        type: "InboundReturnOrder",
                        category: "Inbound",
                        referenceType: "InboundReturnOrder",
                        referenceId: returnOrder.Id,
                        createdByUserId: request.CreatedBy
                    ).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Failed to notify managers for return order " +
                    $"{returnOrder.Id}: {ex.Message}");
            }

            return MapToDto(returnOrder);
        }

        // ── Approve ──────────────────────────────────────────────────────────

        public async Task<InboundReturnOrderDto> ApproveReturnOrderAsync(
            int companyId,
            int returnOrderId,
            ApproveReturnOrderRequest request)
        {
            if (companyId <= 0)
                throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (returnOrderId <= 0)
                throw new ArgumentException("Invalid returnOrderId.", nameof(returnOrderId));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.ApprovedBy <= 0)
                throw new ArgumentException("Invalid ApprovedBy.", nameof(request.ApprovedBy));

            var returnOrder = await _repo.ApproveReturnOrderAsync(
                returnOrderId,
                request.ApprovedBy,
                request.Reason).ConfigureAwait(false);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = request.ApprovedBy,
                Action = "Approve Inbound Return Order",
                Entity = "InboundReturnOrder",
                EntityId = returnOrder.Id,
                Timestamp = now
            }).ConfigureAwait(false);

            // Notify the staff member who created the return
            try
            {
                var staffUserId = returnOrder.CreatedBy;
                if (staffUserId > 0)
                {
                    await _notificationService.SendNotificationToUserAsync(
                        userId: staffUserId,
                        title: "Return order approved",
                        message: $"Return order #{returnOrder.Id} for inbound " +
                                         $"order #{returnOrder.InboundOrderId} has been " +
                                         $"approved. You can now ship the goods back.",
                        type: "InboundReturnOrder",
                        category: "Inbound",
                        referenceType: "InboundReturnOrder",
                        referenceId: returnOrder.Id,
                        createdByUserId: request.ApprovedBy
                    ).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Failed to notify staff for return order " +
                    $"approval {returnOrder.Id}: {ex.Message}");
            }

            return MapToDto(returnOrder);
        }

        // ── Mark Sent ────────────────────────────────────────────────────────

        public async Task<InboundReturnOrderDto> MarkReturnSentAsync(
            int companyId,
            int returnOrderId,
            MarkReturnSentRequest request)
        {
            if (companyId <= 0)
                throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (returnOrderId <= 0)
                throw new ArgumentException("Invalid returnOrderId.", nameof(returnOrderId));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.SentBy <= 0)
                throw new ArgumentException("Invalid SentBy.", nameof(request.SentBy));

            var returnOrder = await _repo.MarkReturnSentAsync(
                returnOrderId,
                request.SentBy).ConfigureAwait(false);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = request.SentBy,
                Action = "Mark Inbound Return Order Sent",
                Entity = "InboundReturnOrder",
                EntityId = returnOrder.Id,
                Timestamp = now
            }).ConfigureAwait(false);

            // Notify managers the goods have left the warehouse
            try
            {
                var companyIdForNotif = returnOrder.Warehouse?.CompanyId
                    ?? returnOrder.InboundOrder?.Warehouse?.CompanyId;

                if (companyIdForNotif.HasValue && companyIdForNotif.Value > 0)
                {
                    await _notificationService.SendNotificationToManagersAsync(
                        companyIdForNotif.Value,
                        title: "Return order shipped",
                        message: $"Return order #{returnOrder.Id} for inbound " +
                                        $"order #{returnOrder.InboundOrderId} has been " +
                                        $"marked as sent back to the supplier.",
                        type: "InboundReturnOrder",
                        category: "Inbound",
                        referenceType: "InboundReturnOrder",
                        referenceId: returnOrder.Id,
                        createdByUserId: request.SentBy
                    ).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Failed to notify managers for return sent " +
                    $"{returnOrder.Id}: {ex.Message}");
            }

            return MapToDto(returnOrder);
        }

        // ── Reads ────────────────────────────────────────────────────────────

        public async Task<InboundReturnOrderDto> GetReturnOrderByIdAsync(
            int companyId,
            int returnOrderId)
        {
            if (companyId <= 0)
                throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (returnOrderId <= 0)
                throw new ArgumentException("Invalid returnOrderId.", nameof(returnOrderId));

            var returnOrder = await _repo
                .GetReturnOrderByIdAsync(companyId, returnOrderId)
                .ConfigureAwait(false);

            return MapToDto(returnOrder);
        }

        public async Task<List<InboundReturnOrderDto>> GetReturnOrdersByInboundOrderAsync(
            int companyId,
            int inboundOrderId)
        {
            if (companyId <= 0)
                throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (inboundOrderId <= 0)
                throw new ArgumentException("Invalid inboundOrderId.", nameof(inboundOrderId));

            var orders = await _repo
                .GetReturnOrdersByInboundOrderAsync(companyId, inboundOrderId)
                .ConfigureAwait(false);

            return orders.Select(MapToDto).ToList();
        }

        // ── Private mapping ──────────────────────────────────────────────────

        private static InboundReturnOrderDto MapToDto(InboundReturnOrder r)
        {
            var items = (r.ReturnOrderItems ?? Enumerable.Empty<InboundReturnOrderItem>())
                .Select(i => new ReturnOrderItemDto(
                    Id: i.Id,
                    InboundOrderItemId: i.InboundOrderItemId,
                    QualityCheckId: i.QualityCheckId,
                    ProductId: i.ProductId,
                    ProductName: i.Product?.Name,
                    ProductSku: i.Product?.Sku,
                    ReturnQuantity: i.ReturnQuantity,
                    FailureReason: i.FailureReason
                        ?? i.QualityCheck?.FailureReason))
                .ToList();

            return new InboundReturnOrderDto(
                Id: r.Id,
                InboundOrderId: r.InboundOrderId,
                SupplierId: r.SupplierId,
                SupplierName: r.Supplier?.Name,
                WarehouseId: r.WarehouseId,
                WarehouseName: r.Warehouse?.Name,
                Status: r.Status,
                Reason: r.Reason,
                CreatedBy: r.CreatedBy,
                CreatedByName: r.CreatedByNavigation?.FullName,
                ApprovedBy: r.ApprovedBy,
                ApprovedByName: r.ApprovedByNavigation?.FullName,
                CreatedAt: r.CreatedAt,
                ApprovedAt: r.ApprovedAt,
                SentAt: r.SentAt,
                Items: items);
        }
    }
}