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
    public class InventoryCountRepository : IInventoryCountRepository
    {
        private readonly StorixDbContext _context;

        public InventoryCountRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<Inventory>> ListInventoryProductsAsync(int companyId, int warehouseId, IEnumerable<int>? productIds = null)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouseId.", nameof(warehouseId));

            var warehouse = await _context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId).ConfigureAwait(false);
            if (warehouse == null) throw new InvalidOperationException("Warehouse not found.");
            if (!warehouse.CompanyId.HasValue || warehouse.CompanyId.Value != companyId)
                throw new InvalidOperationException("Warehouse does not belong to your company.");

            var query = _context.Inventories
                .Include(i => i.Product)
                .Include(i => i.Warehouse)
                .Where(i => i.WarehouseId == warehouseId && i.Warehouse != null && i.Warehouse.CompanyId == companyId);

            if (productIds != null)
            {
                var ids = productIds.Where(x => x > 0).Distinct().ToList();
                if (ids.Count > 0)
                    query = query.Where(i => i.ProductId.HasValue && ids.Contains(i.ProductId.Value));
            }

            return await query.OrderBy(i => i.ProductId).ToListAsync().ConfigureAwait(false);
        }

        public async Task<InventoryCountsTicket> CreateTicketAsync(int companyId, int warehouseId, int createdByUserId, string? name, string? type, string? description, IEnumerable<int>? productIds, int? assignedTo = null)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouseId.", nameof(warehouseId));
            if (createdByUserId <= 0) throw new ArgumentException("Invalid createdByUserId.", nameof(createdByUserId));

            var warehouse = await _context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId).ConfigureAwait(false);
            if (warehouse == null) throw new InvalidOperationException("Warehouse not found.");
            if (!warehouse.CompanyId.HasValue || warehouse.CompanyId.Value != companyId)
                throw new InvalidOperationException("Warehouse does not belong to your company.");

            var inventories = await ListInventoryProductsAsync(companyId, warehouseId, productIds).ConfigureAwait(false);
            if (inventories.Count == 0)
                throw new InvalidOperationException("No inventory found for the selected warehouse or products.");

            if (productIds != null)
            {
                var requested = productIds.Where(x => x > 0).Distinct().ToList();
                if (requested.Count > 0)
                {
                    var found = inventories.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToHashSet();
                    var missing = requested.Where(id => !found.Contains(id)).ToList();
                    if (missing.Count > 0)
                        throw new InvalidOperationException($"Some products are not in inventory for this warehouse: {string.Join(", ", missing)}.");
                }
            }

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                var ticket = new InventoryCountsTicket
                {
                    WarehouseId = warehouseId,
                    PerformedBy = createdByUserId,
                    AssignedTo = assignedTo,
                    Name = string.IsNullOrWhiteSpace(name) ? $"InventoryCount-{warehouseId}-{DateTime.UtcNow:yyyyMMddHHmmss}" : name,
                    Type = string.IsNullOrWhiteSpace(type) ? "InventoryCount" : type,
                    Status = "Counting",
                    Description = description,
                    CreatedAt = now,
                    ExecutedDay = null,
                    FinishedDay = null
                };

                _context.InventoryCountsTickets.Add(ticket);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                foreach (var inv in inventories)
                {
                    if (!inv.ProductId.HasValue) continue;

                    var item = new InventoryCountItem
                    {
                        InventoryCountId = ticket.Id,
                        ProductId = inv.ProductId,
                        SystemQuantity = inv.Quantity ?? 0,
                        CountedQuantity = null,
                        Discrepancy = null,
                        Status = null,
                        Description = null
                    };
                    _context.InventoryCountItems.Add(item);
                }

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);

                return await GetTicketByIdAsync(companyId, ticket.Id).ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<List<InventoryCountsTicket>> ListTicketsAsync(int companyId, int? warehouseId, string? status)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));

            var query = _context.InventoryCountsTickets
                .Include(t => t.Warehouse)
                .Include(t => t.InventoryCountItems)
                .Where(t => t.Warehouse != null && t.Warehouse.CompanyId == companyId);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                query = query.Where(t => t.WarehouseId == warehouseId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(t => t.Status != null && t.Status.ToLower() == status.ToLower());

            return await query.OrderByDescending(t => t.CreatedAt).ToListAsync().ConfigureAwait(false);
        }

        public async Task<InventoryCountsTicket> GetTicketByIdAsync(int companyId, int ticketId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (ticketId <= 0) throw new ArgumentException("Invalid ticketId.", nameof(ticketId));

            var ticket = await _context.InventoryCountsTickets
                .Include(t => t.Warehouse)
                .Include(t => t.InventoryCountItems)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(t => t.Id == ticketId && t.Warehouse != null && t.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (ticket == null)
                throw new InvalidOperationException("Inventory count ticket not found or does not belong to your company.");

            return ticket;
        }

        public async Task<InventoryCountItem> GetItemByIdAsync(int companyId, int itemId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (itemId <= 0) throw new ArgumentException("Invalid itemId.", nameof(itemId));

            var item = await _context.InventoryCountItems
                .Include(i => i.InventoryCount)
                    .ThenInclude(t => t!.Warehouse)
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.InventoryCount != null && i.InventoryCount.Warehouse != null && i.InventoryCount.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (item == null)
                throw new InvalidOperationException("Inventory count item not found or does not belong to your company.");

            return item;
        }

        public async Task<InventoryCountItem> UpdateCountedQuantityAsync(int companyId, int itemId, int countedQuantity, string? description = null, bool? status = null)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (itemId <= 0) throw new ArgumentException("Invalid itemId.", nameof(itemId));
            if (countedQuantity < 0)
                throw new ArgumentException("Counted quantity must be greater than or equal to 0.", nameof(countedQuantity));

            var item = await _context.InventoryCountItems
                .Include(i => i.InventoryCount)
                    .ThenInclude(t => t!.Warehouse)
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.InventoryCount != null && i.InventoryCount.Warehouse != null && i.InventoryCount.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (item == null)
                throw new InvalidOperationException("Inventory count item not found or does not belong to your company.");

            if (item.InventoryCount == null)
                throw new InvalidOperationException("Inventory count item is not linked to a ticket.");

            if (string.Equals(item.InventoryCount.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot update counted quantity for an approved ticket.");

            item.CountedQuantity = countedQuantity;
            var systemQty = item.SystemQuantity ?? 0;
            item.Discrepancy = countedQuantity - systemQty;

            if (description != null) item.Description = description;
            if (status.HasValue) item.Status = status.Value;

            await _context.SaveChangesAsync().ConfigureAwait(false);
            return item;
        }

        public async Task MarkTicketReadyForApprovalAsync(int companyId, int ticketId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (ticketId <= 0) throw new ArgumentException("Invalid ticketId.", nameof(ticketId));

            var ticket = await _context.InventoryCountsTickets
                .Include(t => t.Warehouse)
                .Include(t => t.InventoryCountItems)
                .FirstOrDefaultAsync(t => t.Id == ticketId && t.Warehouse != null && t.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (ticket == null)
                throw new InvalidOperationException("Inventory count ticket not found or does not belong to your company.");

            if (string.Equals(ticket.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot mark an approved ticket as ready.");

            var missingCounts = ticket.InventoryCountItems.Where(i => !i.CountedQuantity.HasValue).Select(i => i.Id).ToList();
            if (missingCounts.Count > 0)
                throw new InvalidOperationException("Some items are missing counted quantity. Please enter counted quantity for all items before running the check.");

            ticket.Status = "ReadyForApproval";
            ticket.ExecutedDay = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task ApplyApprovalAsync(int companyId, int ticketId, int performedByUserId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (ticketId <= 0) throw new ArgumentException("Invalid ticketId.", nameof(ticketId));
            if (performedByUserId <= 0) throw new ArgumentException("Invalid performedByUserId.", nameof(performedByUserId));

            var ticket = await _context.InventoryCountsTickets
                .Include(t => t.Warehouse)
                .Include(t => t.InventoryCountItems)
                .FirstOrDefaultAsync(t => t.Id == ticketId && t.Warehouse != null && t.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (ticket == null)
                throw new InvalidOperationException("Inventory count ticket not found or does not belong to your company.");

            if (string.Equals(ticket.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ticket is already approved.");

            if (!string.Equals(ticket.Status, "ReadyForApproval", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ticket must be in 'ReadyForApproval' status before approval.");

            if (!ticket.WarehouseId.HasValue)
                throw new InvalidOperationException("Ticket has no warehouse.");

            var missingCounts = ticket.InventoryCountItems.Where(i => !i.CountedQuantity.HasValue).Select(i => i.Id).ToList();
            if (missingCounts.Count > 0)
                throw new InvalidOperationException("Some items are missing counted quantity. Please enter counted quantity for all items before approving.");

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var warehouseId = ticket.WarehouseId.Value;

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                foreach (var item in ticket.InventoryCountItems)
                {
                    if (!item.ProductId.HasValue) continue;

                    var productId = item.ProductId.Value;
                    var newQty = item.CountedQuantity!.Value;

                    var inv = await _context.Inventories
                        .FirstOrDefaultAsync(i => i.WarehouseId == warehouseId && i.ProductId == productId)
                        .ConfigureAwait(false);

                    var oldQty = inv?.Quantity ?? 0;
                    if (inv == null)
                    {
                        inv = new Inventory
                        {
                            WarehouseId = warehouseId,
                            ProductId = productId,
                            Quantity = newQty,
                            ReservedQuantity = 0,
                            LastUpdated = now
                        };
                        _context.Inventories.Add(inv);
                    }
                    else
                    {
                        inv.Quantity = newQty;
                        inv.LastUpdated = now;
                    }

                    var delta = newQty - oldQty;
                    if (delta != 0)
                    {
                        _context.InventoryTransactions.Add(new InventoryTransaction
                        {
                            WarehouseId = warehouseId,
                            ProductId = productId,
                            TransactionType = "InventoryCountAdjustment",
                            QuantityChange = delta,
                            ReferenceId = ticket.Id,
                            PerformedBy = performedByUserId,
                            CreatedAt = now
                        });
                    }
                }

                ticket.Status = "Approved";
                ticket.FinishedDay = now;
                ticket.PerformedBy = performedByUserId;
                ticket.ExecutedDay ??= now;

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
