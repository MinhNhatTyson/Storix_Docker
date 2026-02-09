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

        public async Task<InboundRequest> CreateInventoryInboundTicketRequest(InboundRequest request, IEnumerable<ProductPrice>? productPrices = null)
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

            // Persist within a transaction — also persist productPrices if provided
            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.InboundRequests.Add(request);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                if (productPrices != null)
                {
                    var pricesList = productPrices.ToList();
                    if (pricesList.Any())
                    {
                        // Ensure Date is set
                        var nowDate = DateOnly.FromDateTime(DateTime.UtcNow);
                        foreach (var p in pricesList)
                        {
                            if (p.Date == null)
                                p.Date = nowDate;
                        }

                        _context.ProductPrices.AddRange(pricesList);
                        await _context.SaveChangesAsync().ConfigureAwait(false);
                    }
                }

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

        public async Task<InboundOrder> CreateInboundOrderFromRequestAsync(int inboundRequestId, int createdBy, int? staffId)
        {
            var inboundRequest = await _context.InboundRequests
                .Include(r => r.InboundOrderItems)
                .FirstOrDefaultAsync(r => r.Id == inboundRequestId)
                .ConfigureAwait(false);

            if (inboundRequest == null)
                throw new InvalidOperationException($"InboundRequest with id {inboundRequestId} not found.");

            if (!string.Equals(inboundRequest.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("InboundRequest must be in 'Approved' status to create an InboundOrder (ticket).");

            // Build new InboundOrder inheriting fields except CreatedAt, Status, CreatedBy
            var inboundOrder = new InboundOrder
            {
                WarehouseId = inboundRequest.WarehouseId,
                SupplierId = inboundRequest.SupplierId,
                CreatedBy = createdBy,
                StaffId = staffId, 
                Status = "Created",
                InboundRequestId = inboundRequest.Id,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                ReferenceCode = $"INB-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}"
            };

            // Copy items: ExpectedQuantity from request items. Do not link InboundOrderId yet.
            foreach (var reqItem in inboundRequest.InboundOrderItems)
            {
                var orderItem = new InboundOrderItem
                {
                    ProductId = reqItem.ProductId,
                    ExpectedQuantity = reqItem.ExpectedQuantity,
                    ReceivedQuantity = reqItem.ReceivedQuantity // usually null/0 initially
                };
                inboundOrder.InboundOrderItems.Add(orderItem);
            }

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.InboundOrders.Add(inboundOrder);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return inboundOrder;
        }

        public async Task<InboundOrder> UpdateInboundOrderItemsAsync(int inboundOrderId, IEnumerable<InboundOrderItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var order = await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == inboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"InboundOrder with id {inboundOrderId} not found.");

            // Validate items: each must have ProductId and at least one quantity to update
            foreach (var i in items)
            {
                if (i.ProductId == null || i.ProductId <= 0)
                    throw new InvalidOperationException("Each item must have a valid ProductId.");
            }

            // Update existing items or add new ones. We will not remove items that are not present in the payload.
            foreach (var incoming in items)
            {
                if (incoming.Id > 0)
                {
                    var existing = order.InboundOrderItems.FirstOrDefault(x => x.Id == incoming.Id);
                    if (existing == null)
                        throw new InvalidOperationException($"InboundOrderItem with id {incoming.Id} not found in order {inboundOrderId}.");

                    // Update allowed fields
                    existing.ExpectedQuantity = incoming.ExpectedQuantity;
                    existing.ReceivedQuantity = incoming.ReceivedQuantity;
                    existing.ProductId = incoming.ProductId;
                }
                else
                {
                    // Try to find by ProductId first
                    var existingByProduct = order.InboundOrderItems.FirstOrDefault(x => x.ProductId == incoming.ProductId);
                    if (existingByProduct != null)
                    {
                        existingByProduct.ExpectedQuantity = incoming.ExpectedQuantity;
                        existingByProduct.ReceivedQuantity = incoming.ReceivedQuantity;
                    }
                    else
                    {
                        var newItem = new InboundOrderItem
                        {
                            ProductId = incoming.ProductId,
                            ExpectedQuantity = incoming.ExpectedQuantity,
                            ReceivedQuantity = incoming.ReceivedQuantity,
                            InboundOrder = order
                        };
                        order.InboundOrderItems.Add(newItem);
                    }
                }
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return order;
        }
        public async Task<List<InboundRequest>> GetAllInboundRequestsAsync(int companyId)
        {
            return await _context.InboundRequests
                .Include(r => r.InboundOrderItems)
                .Include(r => r.Supplier)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r => r.RequestedByNavigation.CompanyId == companyId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<InboundOrder>> GetAllInboundOrdersAsync(int companyId)
        {
            return await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Supplier)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Where(o => o.CreatedByNavigation.CompanyId == companyId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }
        public async Task<InboundRequest> GetInboundRequestByIdAsync(int companyId, int id)
        {
            var request = await _context.InboundRequests
                .Include(r => r.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.Supplier)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r => r.RequestedByNavigation.CompanyId == companyId)
                .FirstOrDefaultAsync(r => r.Id == id)
                .ConfigureAwait(false);

            if (request == null)
                throw new InvalidOperationException($"InboundRequest with id {id} not found.");

            return request;
        }

        public async Task<InboundOrder> GetInboundOrderByIdAsync(int companyId, int id)
        {
            var order = await _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Supplier)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Include(o => o.InboundRequest)
                .Where(o => o.CreatedByNavigation.CompanyId == companyId)
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"InboundOrder with id {id} not found.");

            return order;
        }
        public async Task<bool> InboundRequestCodeExistsAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            return await _context.InboundRequests.AnyAsync(r => r.Code == code).ConfigureAwait(false);
        }
        public async Task<List<InboundOrder>> GetInboundOrdersByStaffAsync(int companyId, int staffId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (staffId <= 0) throw new ArgumentException("Invalid staff id.", nameof(staffId));

            var query = _context.InboundOrders
                .Include(o => o.InboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Supplier)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Include(o => o.Staff)
                .Where(o => o.StaffId == staffId && o.Warehouse != null && o.Warehouse.CompanyId == companyId)
                .OrderByDescending(o => o.CreatedAt);

            return await query.ToListAsync().ConfigureAwait(false);
        }
    }
}
