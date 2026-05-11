
using Storix_BE.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IInboundReturnRepository
    {
        // ── Reads ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the InboundOrder with its items and QC records,
        /// scoped to the given company. Throws if not found.
        /// </summary>
        Task<InboundOrder> GetInboundOrderForReturnAsync(int companyId, int inboundOrderId);

        /// <summary>
        /// Returns a single return order by id, scoped to company.
        /// Throws InvalidOperationException if not found.
        /// </summary>
        Task<InboundReturnOrder> GetReturnOrderByIdAsync(int companyId, int returnOrderId);

        /// <summary>
        /// Returns all return orders for a given inbound order, scoped to company.
        /// </summary>
        Task<List<InboundReturnOrder>> GetReturnOrdersByInboundOrderAsync(
            int companyId, int inboundOrderId);

        // ── Writes ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new InboundReturnOrder (status = PENDING) and its line items.
        /// Also sets InboundQualityCheck.ReturnStatus = "PENDING" on affected QC rows.
        /// Transitions the InboundOrder status to RETURN_PENDING.
        /// </summary>
        Task<InboundReturnOrder> CreateReturnOrderAsync(
            int inboundOrderId,
            int createdBy,
            string? reason,
            IEnumerable<ReturnOrderItemSaveDto> items);

        /// <summary>
        /// Transitions the return order from PENDING → APPROVED.
        /// Sets InboundQualityCheck.ReturnStatus = "APPROVED" on affected rows.
        /// Transitions the InboundOrder status to RETURN_APPROVED.
        /// </summary>
        Task<InboundReturnOrder> ApproveReturnOrderAsync(
            int returnOrderId,
            int approvedBy,
            string? reason);

        /// <summary>
        /// Transitions the return order from APPROVED → SENT.
        /// Sets InboundQualityCheck.ReturnStatus = "SENT" on affected rows.
        /// Transitions the InboundOrder status to RETURNED.
        /// </summary>
        Task<InboundReturnOrder> MarkReturnSentAsync(
            int returnOrderId,
            int sentBy);

        // ── Internal DTO ─────────────────────────────────────────────────────

        /// <summary>
        /// Data needed to persist one return line item.
        /// </summary>
        public sealed record ReturnOrderItemSaveDto(
            int InboundOrderItemId,
            int QualityCheckId,
            int ProductId,
            int ReturnQuantity,
            string? FailureReason);
    }
}