using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IInboundReturnService
    {
        /// <summary>
        /// Staff flags failed units for return, creating a PENDING return order.
        /// InboundOrder transitions to RETURN_PENDING.
        /// </summary>
        Task<InboundReturnOrderDto> CreateReturnOrderAsync(
            int companyId,
            int inboundOrderId,
            CreateReturnOrderRequest request);

        /// <summary>
        /// Manager/Admin approves a PENDING return order.
        /// InboundOrder transitions to RETURN_APPROVED.
        /// Staff who created the return is notified.
        /// </summary>
        Task<InboundReturnOrderDto> ApproveReturnOrderAsync(
            int companyId,
            int returnOrderId,
            ApproveReturnOrderRequest request);

        /// <summary>
        /// Staff marks the goods as physically shipped back to the supplier.
        /// Return order transitions to SENT. InboundOrder transitions to RETURNED.
        /// </summary>
        Task<InboundReturnOrderDto> MarkReturnSentAsync(
            int companyId,
            int returnOrderId,
            MarkReturnSentRequest request);

        /// <summary>
        /// Returns a single return order by id, scoped to company.
        /// </summary>
        Task<InboundReturnOrderDto> GetReturnOrderByIdAsync(
            int companyId,
            int returnOrderId);

        /// <summary>
        /// Returns all return orders for a given inbound order, scoped to company.
        /// </summary>
        Task<List<InboundReturnOrderDto>> GetReturnOrdersByInboundOrderAsync(
            int companyId,
            int inboundOrderId);
    }

    // ── Request records ──────────────────────────────────────────────────────

    /// <summary>One line item in a return order request.</summary>
    public sealed record ReturnOrderItemRequest(
        /// <summary>InboundOrderItem.Id from the original order.</summary>
        int InboundOrderItemId,

        /// <summary>InboundQualityCheck.Id that produced the failed units.</summary>
        int QualityCheckId,

        /// <summary>How many failed units to return. Must be ≤ QC.FailedQuantity.</summary>
        int ReturnQuantity,

        /// <summary>
        /// Optional override of the failure reason from the QC record.
        /// If null, the QC record's FailureReason is copied automatically.
        /// </summary>
        string? FailureReason);

    public sealed record CreateReturnOrderRequest(
        /// <summary>UserId of the staff member creating the return.</summary>
        int CreatedBy,

        /// <summary>Optional overall note from the staff member.</summary>
        string? Reason,

        IEnumerable<ReturnOrderItemRequest> Items);

    public sealed record ApproveReturnOrderRequest(
        /// <summary>UserId of the manager/admin approving the return.</summary>
        int ApprovedBy,

        /// <summary>Optional manager note appended to the return order reason.</summary>
        string? Reason);

    public sealed record MarkReturnSentRequest(
        /// <summary>UserId of the staff member marking goods as shipped.</summary>
        int SentBy);

    // ── Response DTOs ────────────────────────────────────────────────────────

    public sealed record ReturnOrderItemDto(
        int Id,
        int InboundOrderItemId,
        int QualityCheckId,
        int? ProductId,
        string? ProductName,
        string? ProductSku,
        int ReturnQuantity,
        string? FailureReason);

    public sealed record InboundReturnOrderDto(
        int Id,
        int InboundOrderId,
        int? SupplierId,
        string? SupplierName,
        int? WarehouseId,
        string? WarehouseName,

        /// <summary>PENDING | APPROVED | SENT</summary>
        string Status,

        string? Reason,
        int CreatedBy,
        string? CreatedByName,
        int? ApprovedBy,
        string? ApprovedByName,
        DateTime CreatedAt,
        DateTime? ApprovedAt,
        DateTime? SentAt,
        IEnumerable<ReturnOrderItemDto> Items);

}
