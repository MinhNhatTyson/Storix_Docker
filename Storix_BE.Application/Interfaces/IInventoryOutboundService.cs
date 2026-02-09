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
        Task<List<OutboundRequestDto>> GetAllOutboundRequestsAsync(int companyId, int? warehouseId);
        Task<OutboundRequestDto> GetOutboundRequestByIdAsync(int companyId, int id);
        Task<List<OutboundOrderDto>> GetAllOutboundOrdersAsync(int companyId, int? warehouseId);
        Task<OutboundOrderDto> GetOutboundOrderByIdAsync(int companyId, int id);
        Task<List<OutboundOrderDto>> GetOutboundOrdersByStaffAsync(int companyId, int staffId);
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
    public sealed record OutboundWarehouseDto(int Id, string? Name);

    public sealed record OutboundUserDto(int Id, string? FullName, string? Email, string? Phone);

    public sealed record OutboundOrderItemDto(int Id, int? ProductId, string? ProductName, int? Quantity, double? Price);

    public sealed record OutboundRequestDto(
        int Id,
        int? WarehouseId,
        int? RequestedBy,
        int? ApprovedBy,
        string? Destination,
        string? Status,
        double? TotalPrice,
        DateTime? CreatedAt,
        DateTime? ApprovedAt,
        IEnumerable<OutboundOrderItemDto> Items,
        OutboundWarehouseDto? Warehouse,
        OutboundUserDto? RequestedByUser,
        OutboundUserDto? ApprovedByUser);

    public sealed record OutboundOrderDto(
        int Id,
        int? WarehouseId,
        int? CreatedBy,
        int? StaffId,
        string? Destination,
        string? Status,
        string? Note,
        DateTime? CreatedAt,
        IEnumerable<OutboundOrderItemDto> Items,
        OutboundWarehouseDto? Warehouse,
        OutboundUserDto? CreatedByUser);
}
