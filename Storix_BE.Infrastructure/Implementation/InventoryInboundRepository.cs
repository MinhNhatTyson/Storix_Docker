using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class InventoryInboundRepository : IInventoryInboundRepository
    {
        private readonly StorixDbContext _context;

        public InventoryInboundRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<InboundRequest> CreateInventoryInboundTicketRequest(InboundRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Request must contain at least one product item
            if (request.InboundOrderItems == null || !request.InboundOrderItems.Any())
            {
                throw new InvalidOperationException("InboundRequest must contain at least one InboundOrderItem describing product and expected quantity.");
            }

            // Basic per-item validation
            var invalidItem = request.InboundOrderItems.FirstOrDefault(i => i.ProductId == null || i.ExpectedQuantity == null || i.ExpectedQuantity <= 0);
            if (invalidItem != null)
            {
                throw new InvalidOperationException("All InboundOrderItems must specify a ProductId and ExpectedQuantity > 0.");
            }

            // Verify products exist
            var productIds = request.InboundOrderItems.Select(i => i.ProductId!.Value).Distinct().ToList();
            var existingProductIds = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var missing = productIds.Except(existingProductIds).ToList();
            if (missing.Any())
            {
                throw new InvalidOperationException($"Products not found: {string.Join(',', missing)}");
            }

            // Optional: validate referenced warehouse/supplier/requestedBy exist when provided
            if (request.WarehouseId.HasValue)
            {
                var wh = await _context.Warehouses.FindAsync(request.WarehouseId.Value).ConfigureAwait(false);
                if (wh == null) throw new InvalidOperationException($"Warehouse with id {request.WarehouseId.Value} not found.");
            }
            if (request.SupplierId.HasValue)
            {
                var sup = await _context.Suppliers.FindAsync(request.SupplierId.Value).ConfigureAwait(false);
                if (sup == null) throw new InvalidOperationException($"Supplier with id {request.SupplierId.Value} not found.");
            }

            // Set defaults
            request.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            if (string.IsNullOrWhiteSpace(request.Status))
            {
                request.Status = "Pending";
            }

            // Ensure child items are correctly linked to the request
            foreach (var item in request.InboundOrderItems)
            {
                item.InboundRequest = request;
            }

            // Persist within a transaction
            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.InboundRequests.Add(request);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return request;
        }

        public async Task<InboundRequest> UpdateInventoryInboundTicketRequestStatus(int ticketRequestId, int approverId, string status)
        {
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var inbound = await _context.InboundRequests
                .FirstOrDefaultAsync(r => r.Id == ticketRequestId)
                .ConfigureAwait(false);

            if (inbound == null)
            {
                throw new InvalidOperationException($"InboundRequest with id {ticketRequestId} not found.");
            }

            inbound.Status = status;
            inbound.ApprovedBy = approverId;
            inbound.ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return inbound;
        }
    }
}
