using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IInventoryOutboundService
    {
        Task<OutboundRequest> CreateOutboundRequestAsync(CreateOutboundRequestRequest request);

        Task<IReadOnlyList<InventoryAvailabilityResponse>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds);

        Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status);

        Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note, string? pricingMethod = "LastPurchasePrice");

        Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<UpdateOutboundOrderItemRequest> items);

        Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status);

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
        IEnumerable<CreateOutboundOrderItemRequest> Items,
        string? Reason = null);

    public sealed record UpdateOutboundRequestStatusRequest(int ApproverId, string Status);

    public sealed record UpdateOutboundLocationAssignmentRequest(string BinId, int Quantity);

    // Keep payload shape aligned with inbound edit-items:
    // id, productId, expectedQuantity, receivedQuantity, locations[]
    public sealed record UpdateOutboundOrderItemRequest(
        int Id,
        int ProductId,
        int? ExpectedQuantity,
        int? ReceivedQuantity,
        IEnumerable<UpdateOutboundLocationAssignmentRequest>? Locations);

    public sealed record CreateOutboundOrderFromRequestRequest(int CreatedBy, int? StaffId, string? Note, string? PricingMethod = "LastPurchasePrice");

    public sealed record UpdateOutboundOrderStatusRequest(int PerformedBy, string Status);

    public sealed record InventoryAvailabilityResponse(int ProductId, int AvailableQuantity);
    public sealed record OutboundWarehouseDto(int Id, string? Name);

    public sealed record OutboundUserDto(int Id, string? FullName, string? Email, string? Phone);

    public sealed record OutboundOrderItemDto(
        int Id,
        int? ProductId,
        string? ProductName,
        int? ExpectedQuantity,
        int? ReceivedQuantity,
        int? Quantity,
        double? Price,
        double? CostPrice,
        string? PricingMethod);

    public sealed record OutboundRequestDto(
        int Id,
        int? WarehouseId,
        int? RequestedBy,
        int? ApprovedBy,
        string? Destination,
        string? Reason,
        string? ReferenceCode,
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