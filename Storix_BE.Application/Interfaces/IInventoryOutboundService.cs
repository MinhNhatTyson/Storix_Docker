using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IInventoryOutboundService
    {
        Task<OutboundRequest> CreateOutboundRequestAsync(CreateOutboundRequestRequest request);

        Task<IReadOnlyList<InventoryAvailabilityResponse>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds);

        Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status);

        Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note);

        Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<UpdateOutboundOrderItemRequest> items);

        Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status);

        Task<OutboundOrder> ConfirmOutboundOrderAsync(int outboundOrderId, int performedBy);
    }

    public sealed record CreateOutboundOrderItemRequest(int ProductId, int Quantity);

    public sealed record CreateOutboundRequestRequest(
        int? WarehouseId,
        string? Destination,
        int RequestedBy,
        IEnumerable<CreateOutboundOrderItemRequest> Items);

    public sealed record UpdateOutboundRequestStatusRequest(int ApproverId, string Status);

    public sealed record UpdateOutboundOrderItemRequest(int Id, int ProductId, int? Quantity);

    public sealed record CreateOutboundOrderFromRequestRequest(int CreatedBy, int? StaffId, string? Note);

    public sealed record ConfirmOutboundOrderRequest(int PerformedBy);

    public sealed record UpdateOutboundOrderStatusRequest(int PerformedBy, string Status);

    public sealed record InventoryAvailabilityResponse(int ProductId, int AvailableQuantity);
}
