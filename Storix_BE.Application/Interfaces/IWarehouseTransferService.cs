using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IWarehouseTransferService
    {
        Task<TransferOrderDetailDto> CreateDraftAsync(int companyId, int createdBy, CreateTransferOrderRequest request);
        Task<TransferOrderDetailDto> UpdateDraftAsync(int companyId, int actorUserId, int transferOrderId, UpdateTransferOrderRequest request);
        Task<TransferOrderDetailDto> AddItemAsync(int companyId, int actorUserId, int transferOrderId, AddTransferOrderItemRequest request);
        Task<TransferOrderDetailDto> UpdateItemAsync(int companyId, int actorUserId, int transferOrderId, int itemId, UpdateTransferOrderItemRequest request);
        Task<TransferOrderDetailDto> RemoveItemAsync(int companyId, int actorUserId, int transferOrderId, int itemId);

        Task<TransferOrderDetailDto> SubmitAsync(int companyId, int actorUserId, int transferOrderId);
        Task<TransferOrderDetailDto> ApproveAsync(int companyId, int actorUserId, int transferOrderId);
        Task<TransferOrderDetailDto> RejectAsync(int companyId, int actorUserId, int transferOrderId, string reason);

        Task<TransferOrderDetailDto> StartPickingAsync(int companyId, int actorUserId, int transferOrderId);
        Task<TransferOrderDetailDto> MarkPackedAsync(int companyId, int actorUserId, int transferOrderId);
        Task<TransferOrderDetailDto> ShipAsync(int companyId, int actorUserId, int transferOrderId);
        Task<TransferOrderDetailDto> ReceiveAsync(int companyId, int actorUserId, int transferOrderId, ReceiveTransferOrderRequest request);
        Task<TransferOrderDetailDto> QualityCheckAsync(int companyId, int actorUserId, int transferOrderId, TransferQualityCheckRequest request);

        Task<TransferOrderDetailDto> CancelAsync(int companyId, int actorUserId, int transferOrderId, string? reason);

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

    public sealed record CreateTransferOrderRequest(int SourceWarehouseId, int DestinationWarehouseId, int? CarrierUserId = null, bool SubmitAfterCreate = false);
    public sealed record UpdateTransferOrderRequest(int SourceWarehouseId, int DestinationWarehouseId, int? CarrierUserId = null);
    public sealed record AddTransferOrderItemRequest(int ProductId, int Quantity);
    public sealed record UpdateTransferOrderItemRequest(int ProductId, int Quantity);

    public sealed record ReceiveTransferItemRequest(int ProductId, int ReceivedQuantity, int? DamagedQuantity);
    public sealed record ReceiveTransferOrderRequest(IEnumerable<ReceiveTransferItemRequest> Items, string? Note);

    public sealed record TransferQualityCheckItemRequest(int ProductId, int OkQuantity, int BadQuantity, string? Note);
    public sealed record TransferQualityCheckRequest(IEnumerable<TransferQualityCheckItemRequest> Items, string? Note);

    public sealed record TransferOrderItemDto(int Id, int? ProductId, string? ProductName, int? Quantity);

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
}