using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IWarehouseTransferService
    {
        Task<TransferOrderDetailDto> CreateAsync(int companyId, int createdBy, CreateTransferOrderRequest request);
        Task<TransferOrderDetailDto> ApproveAsync(int companyId, int actorUserId, int transferOrderId, int? receiverStaffId = null);
        Task<List<TransferOrderListDto>> GetAllAsync(int companyId, int? sourceWarehouseId, int? destinationWarehouseId, string? status);
        Task<TransferOrderDetailDto> GetByIdAsync(int companyId, int transferOrderId);
        Task<List<TransferAvailabilityDto>> CheckAvailabilityAsync(int companyId, int transferOrderId);
    }

    public static class TransferStatuses
    {
        public const string Draft = "DRAFT";
        public const string PendingApproval = "PENDING_APPROVAL";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
        public const string Picking = "PICKING";
        public const string Packed = "PACKED";
        public const string InTransit = "IN_TRANSIT";
        public const string ReceivedFull = "RECEIVED_FULL";
        public const string ReceivedPartial = "RECEIVED_PARTIAL";
        public const string Completed = "COMPLETED";
        public const string QualityChecked = "QUALITY_CHECKED";
        public const string QualityIssue = "QUALITY_ISSUE";
        public const string Cancelled = "CANCELLED";
    }

    public sealed record CreateTransferOrderItemRequest(int ProductId, int Quantity);
    public sealed record CreateTransferOrderRequest(
        int SourceWarehouseId,
        int DestinationWarehouseId,
        IEnumerable<CreateTransferOrderItemRequest> Items,
        bool SubmitAfterCreate = true);
    public sealed record ApproveTransferOrderRequest(int? ReceiverStaffId);

    public sealed record TransferOrderItemDto(
        int Id,
        int? ProductId,
        string? ProductName,
        int? Quantity,
        int? OutboundOrderItemId,
        int? InboundOrderItemId);

    public sealed record TransferOrderListDto(
        int Id,
        int? SourceWarehouseId,
        string? SourceWarehouseName,
        int? DestinationWarehouseId,
        string? DestinationWarehouseName,
        int? CreatedBy,
        string? CreatedByName,
        string? Status,
        DateTime? CreatedAt,
        int TotalItems,
        int TotalQuantity);

    public sealed record TransferOrderTimelineDto(int Id, string? Action, DateTime? Timestamp, int? UserId, string? UserName);

    public sealed record TransferOrderDetailDto(
        int Id,
        int? SourceWarehouseId,
        string? SourceWarehouseName,
        int? DestinationWarehouseId,
        string? DestinationWarehouseName,
        int? CreatedBy,
        string? CreatedByName,
        string? Status,
        DateTime? CreatedAt,
        IEnumerable<TransferOrderItemDto> Items,
        IEnumerable<TransferOrderTimelineDto> Timeline);

    public sealed record TransferAvailabilityDto(int ProductId, string? ProductName, int RequiredQuantity, int AvailableQuantity, bool IsEnough);

    public sealed record TransferStaffSuggestionDto(
        int UserId,
        string? FullName,
        string? Email,
        int AssignedWarehouseCount,
        int ActiveTransferTaskCount,
        int SuggestionScore,
        string? Reason);
}

