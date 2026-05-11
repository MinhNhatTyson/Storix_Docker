// ============================================================
// FILE: Storix_BE.Repository/Implementation/InboundReturnRepository.cs
// ── Create this as a NEW file ──
// ============================================================

using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class InboundReturnRepository : IInboundReturnRepository
    {
        private readonly StorixDbContext _context;

        public InboundReturnRepository(StorixDbContext context)
        {
            _context = context;
        }

        // ── Reads ────────────────────────────────────────────────────────────

        public async Task<InboundOrder> GetInboundOrderForReturnAsync(
            int companyId, int inboundOrderId)
        {
            var order = await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .Include(o => o.Supplier)
                .Include(o => o.Staff)
                .FirstOrDefaultAsync(o =>
                    o.Id == inboundOrderId &&
                    o.Warehouse != null &&
                    o.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException(
                    $"InboundOrder with id {inboundOrderId} not found " +
                    $"for company {companyId}.");

            return order;
        }

        public async Task<InboundReturnOrder> GetReturnOrderByIdAsync(
            int companyId, int returnOrderId)
        {
            var returnOrder = await _context.InboundReturnOrders
                .Include(r => r.ReturnOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.ReturnOrderItems)
                    .ThenInclude(i => i.QualityCheck)
                .Include(r => r.ReturnOrderItems)
                    .ThenInclude(i => i.InboundOrderItem)
                .Include(r => r.InboundOrder)
                    .ThenInclude(o => o.Warehouse)
                .Include(r => r.Supplier)
                .Include(r => r.CreatedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .FirstOrDefaultAsync(r =>
                    r.Id == returnOrderId &&
                    r.InboundOrder.Warehouse != null &&
                    r.InboundOrder.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (returnOrder == null)
                throw new InvalidOperationException(
                    $"InboundReturnOrder with id {returnOrderId} not found " +
                    $"for company {companyId}.");

            return returnOrder;
        }

        public async Task<List<InboundReturnOrder>> GetReturnOrdersByInboundOrderAsync(
            int companyId, int inboundOrderId)
        {
            return await _context.InboundReturnOrders
                .Include(r => r.ReturnOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.ReturnOrderItems)
                    .ThenInclude(i => i.QualityCheck)
                .Include(r => r.Supplier)
                .Include(r => r.CreatedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r =>
                    r.InboundOrderId == inboundOrderId &&
                    r.InboundOrder.Warehouse != null &&
                    r.InboundOrder.Warehouse.CompanyId == companyId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        // ── Writes ───────────────────────────────────────────────────────────

        public async Task<InboundReturnOrder> CreateReturnOrderAsync(
            int inboundOrderId,
            int createdBy,
            string? reason,
            IEnumerable<IInboundReturnRepository.ReturnOrderItemSaveDto> items)
        {
            if (inboundOrderId <= 0)
                throw new ArgumentException(
                    "Invalid inboundOrderId.", nameof(inboundOrderId));
            if (createdBy <= 0)
                throw new ArgumentException("Invalid createdBy.", nameof(createdBy));

            var itemList = items?.ToList()
                ?? throw new ArgumentNullException(nameof(items));

            if (!itemList.Any())
                throw new InvalidOperationException(
                    "A return order must contain at least one item.");

            var order = await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == inboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException(
                    $"InboundOrder with id {inboundOrderId} not found.");

            // Only allow creating a return when order is in QUALITY_CHECK
            if (!string.Equals(order.Status, "QUALITY_CHECK",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"A return order can only be created when the inbound order is " +
                    $"in 'QUALITY_CHECK' status. Current status: '{order.Status}'.");

            // Validate each return line against its QC record
            var qcIds = itemList.Select(i => i.QualityCheckId).Distinct().ToList();
            var qcRecords = await _context.InboundQualityChecks
                .Where(q => qcIds.Contains(q.Id) && q.InboundOrderId == inboundOrderId)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var dto in itemList)
            {
                var qc = qcRecords.FirstOrDefault(q => q.Id == dto.QualityCheckId);

                if (qc == null)
                    throw new InvalidOperationException(
                        $"QualityCheck {dto.QualityCheckId} not found for " +
                        $"InboundOrder {inboundOrderId}.");

                if (qc.FailedQuantity <= 0)
                    throw new InvalidOperationException(
                        $"QualityCheck {dto.QualityCheckId} has no failed units " +
                        $"to return (FailedQuantity = {qc.FailedQuantity}).");

                if (dto.ReturnQuantity <= 0)
                    throw new InvalidOperationException(
                        $"ReturnQuantity must be > 0 " +
                        $"(QualityCheckId = {dto.QualityCheckId}).");

                if (dto.ReturnQuantity > qc.FailedQuantity)
                    throw new InvalidOperationException(
                        $"ReturnQuantity ({dto.ReturnQuantity}) cannot exceed " +
                        $"FailedQuantity ({qc.FailedQuantity}) " +
                        $"for QualityCheckId {dto.QualityCheckId}.");
            }

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await using var tx = await _context.Database
                .BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                var returnOrder = new InboundReturnOrder
                {
                    InboundOrderId = inboundOrderId,
                    SupplierId = order.SupplierId,
                    WarehouseId = order.WarehouseId,
                    Status = "PENDING",
                    Reason = reason?.Trim(),
                    CreatedBy = createdBy,
                    CreatedAt = now
                };

                foreach (var dto in itemList)
                {
                    returnOrder.ReturnOrderItems.Add(new InboundReturnOrderItem
                    {
                        InboundOrderItemId = dto.InboundOrderItemId,
                        QualityCheckId = dto.QualityCheckId,
                        ProductId = dto.ProductId > 0 ? dto.ProductId : null,
                        ReturnQuantity = dto.ReturnQuantity,
                        FailureReason = dto.FailureReason?.Trim()
                    });
                }

                _context.InboundReturnOrders.Add(returnOrder);

                // Mark affected QC rows as PENDING
                foreach (var qc in qcRecords)
                    qc.ReturnStatus = "PENDING";

                // Transition inbound order status
                order.Status = "RETURN_PENDING";

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);

                return await GetReturnOrderByIdAsync(
                    await ResolveCompanyIdAsync(inboundOrderId),
                    returnOrder.Id).ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<InboundReturnOrder> ApproveReturnOrderAsync(
            int returnOrderId,
            int approvedBy,
            string? reason)
        {
            if (returnOrderId <= 0)
                throw new ArgumentException(
                    "Invalid returnOrderId.", nameof(returnOrderId));
            if (approvedBy <= 0)
                throw new ArgumentException("Invalid approvedBy.", nameof(approvedBy));

            var returnOrder = await _context.InboundReturnOrders
                .Include(r => r.ReturnOrderItems)
                .Include(r => r.InboundOrder)
                .FirstOrDefaultAsync(r => r.Id == returnOrderId)
                .ConfigureAwait(false);

            if (returnOrder == null)
                throw new InvalidOperationException(
                    $"InboundReturnOrder with id {returnOrderId} not found.");

            if (!string.Equals(returnOrder.Status, "PENDING",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Only a PENDING return order can be approved. " +
                    $"Current status: '{returnOrder.Status}'.");

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await using var tx = await _context.Database
                .BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                returnOrder.Status = "APPROVED";
                returnOrder.ApprovedBy = approvedBy;
                returnOrder.ApprovedAt = now;

                if (!string.IsNullOrWhiteSpace(reason))
                    returnOrder.Reason = reason.Trim();

                // Update QC rows
                var qcIds = returnOrder.ReturnOrderItems
                    .Select(i => i.QualityCheckId)
                    .Distinct()
                    .ToList();

                var qcRecords = await _context.InboundQualityChecks
                    .Where(q => qcIds.Contains(q.Id))
                    .ToListAsync()
                    .ConfigureAwait(false);

                foreach (var qc in qcRecords)
                    qc.ReturnStatus = "APPROVED";

                // Transition inbound order
                returnOrder.InboundOrder.Status = "RETURN_APPROVED";

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);

                return await GetReturnOrderByIdAsync(
                    await ResolveCompanyIdAsync(returnOrder.InboundOrderId),
                    returnOrderId).ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<InboundReturnOrder> MarkReturnSentAsync(
            int returnOrderId,
            int sentBy)
        {
            if (returnOrderId <= 0)
                throw new ArgumentException(
                    "Invalid returnOrderId.", nameof(returnOrderId));
            if (sentBy <= 0)
                throw new ArgumentException("Invalid sentBy.", nameof(sentBy));

            var returnOrder = await _context.InboundReturnOrders
                .Include(r => r.ReturnOrderItems)
                .Include(r => r.InboundOrder)
                .FirstOrDefaultAsync(r => r.Id == returnOrderId)
                .ConfigureAwait(false);

            if (returnOrder == null)
                throw new InvalidOperationException(
                    $"InboundReturnOrder with id {returnOrderId} not found.");

            if (!string.Equals(returnOrder.Status, "APPROVED",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Only an APPROVED return order can be marked as sent. " +
                    $"Current status: '{returnOrder.Status}'.");

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await using var tx = await _context.Database
                .BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                returnOrder.Status = "SENT";
                returnOrder.SentAt = now;

                // Update QC rows
                var qcIds = returnOrder.ReturnOrderItems
                    .Select(i => i.QualityCheckId)
                    .Distinct()
                    .ToList();

                var qcRecords = await _context.InboundQualityChecks
                    .Where(q => qcIds.Contains(q.Id))
                    .ToListAsync()
                    .ConfigureAwait(false);

                foreach (var qc in qcRecords)
                    qc.ReturnStatus = "SENT";

                // Transition inbound order to RETURNED
                returnOrder.InboundOrder.Status = "RETURNED";

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);

                return await GetReturnOrderByIdAsync(
                    await ResolveCompanyIdAsync(returnOrder.InboundOrderId),
                    returnOrderId).ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private async Task<int> ResolveCompanyIdAsync(int inboundOrderId)
        {
            var companyId = await _context.InboundOrders
                .Where(o => o.Id == inboundOrderId)
                .Select(o => o.Warehouse != null ? o.Warehouse.CompanyId : null)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return companyId ?? 0;
        }
    }
}