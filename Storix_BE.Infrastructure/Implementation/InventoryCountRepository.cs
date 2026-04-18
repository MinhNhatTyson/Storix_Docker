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
        public async Task<List<InventoryCountsTicket>> GetStockCountTicketsByWarehouseAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            var items = await _context.InventoryCountsTickets
                .Include(t => t.InventoryCountItems)
                    .ThenInclude(i => i.Product)
                .Include(t => t.PerformedByNavigation)
                .Where(t => t.Warehouse != null
                            && t.Warehouse.CompanyId == companyId
                            && t.WarehouseId == warehouseId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);

            return items;
        }

        public async Task<InventoryCountsTicket> CreateStockCountTicketAsync(InventoryCountsTicket ticket)
        {
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));
            if (ticket.InventoryCountItems == null || !ticket.InventoryCountItems.Any())
                throw new InvalidOperationException("Ticket must contain at least one InventoryCountItem.");
            var providedZoneIds = ticket.StorageZones?.Where(z => z.Id > 0).Select(z => z.Id).Distinct().ToList();
            if (providedZoneIds != null && providedZoneIds.Any())
            {
                var zones = await _context.StorageZones
                    .Where(z => providedZoneIds.Contains(z.Id))
                    .ToListAsync()
                    .ConfigureAwait(false);

                var missing = providedZoneIds.Except(zones.Select(z => z.Id)).ToList();
                if (missing.Any())
                    throw new InvalidOperationException($"StorageZone(s) not found: {string.Join(", ", missing)}");

                if (ticket.WarehouseId.HasValue)
                {
                    var invalidWarehouseZones = zones.Where(z => z.WarehouseId != ticket.WarehouseId.Value).Select(z => z.Id).ToList();
                    if (invalidWarehouseZones.Any())
                        throw new InvalidOperationException($"StorageZone(s) do not belong to warehouse {ticket.WarehouseId}: {string.Join(", ", invalidWarehouseZones)}");
                }

                // replace placeholders with tracked entities so EF will persist the many-to-many properly
                ticket.StorageZones.Clear();
                foreach (var z in zones)
                    ticket.StorageZones.Add(z);
            }

            ticket.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            if (string.IsNullOrWhiteSpace(ticket.Status))
                ticket.Status = "Approved";

            var productIds = ticket.InventoryCountItems.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
            Dictionary<int, int> systemQty = new();

            if (ticket.WarehouseId.HasValue && productIds.Any())
            {
                var inventories = await _context.Inventories
                    .Where(inv => inv.WarehouseId == ticket.WarehouseId && inv.ProductId.HasValue && productIds.Contains(inv.ProductId.Value))
                    .ToListAsync()
                    .ConfigureAwait(false);

                systemQty = inventories
                    .Where(inv => inv.ProductId.HasValue)
                    .GroupBy(inv => inv.ProductId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity ?? 0));
            }

            foreach (var item in ticket.InventoryCountItems)
            {
                item.InventoryCount = ticket;
                if (item.ProductId.HasValue && systemQty.TryGetValue(item.ProductId.Value, out var q))
                    item.SystemQuantity = q;
            }

            if (productIds.Any())
            {
                await ValidateProductsAvailabilityAsync(
                    productIds,
                    ticket.InventoryCountItems
                        .Where(i => i.LocationId.HasValue)
                        .Select(i => (ProductId: i.ProductId, LocationId: i.LocationId)),
                    ticket.WarehouseId
                ).ConfigureAwait(false);
            }

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.InventoryCountsTickets.Add(ticket);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return ticket;
        }

        public async Task<InventoryCountsTicket> UpdateStockCountTicketStatusAsync(int ticketId, int approverId, string status)
        {
            if (ticketId <= 0) throw new ArgumentException("Invalid ticket id.", nameof(ticketId));
            if (approverId <= 0) throw new ArgumentException("Invalid approver id.", nameof(approverId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var ticket = await _context.InventoryCountsTickets
                .Include(t => t.PerformedByNavigation)
                .FirstOrDefaultAsync(t => t.Id == ticketId)
                .ConfigureAwait(false);

            if (ticket == null)
                throw new InvalidOperationException($"InventoryCountsTicket with id {ticketId} not found.");

            ticket.Status = status;
            ticket.ApprovedBy = approverId;
            ticket.ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await _context.SaveChangesAsync().ConfigureAwait(false);
            return ticket;
        }

        public async Task<InventoryCountsTicket> GetStockCountTicketByIdAsync(int companyId, int id)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (id <= 0) throw new ArgumentException("Invalid ticket id.", nameof(id));

            var ticket = await _context.InventoryCountsTickets
                .Include(t => t.InventoryCountItems)
                    .ThenInclude(i => i.Product)
                .Include(t => t.InventoryCountItems)
                    .ThenInclude(i => i.Location)
                .Include(t => t.Warehouse)
                .Include(t => t.PerformedByNavigation)
                .FirstOrDefaultAsync(t => t.Id == id && t.Warehouse != null && t.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (ticket == null)
                throw new InvalidOperationException($"InventoryCountsTicket with id {id} not found for company {companyId}.");

            return ticket;
        }

        public async Task<List<InventoryCountsTicket>> GetStockCountTicketsByCompanyAsync(int companyId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));

            var items = await _context.InventoryCountsTickets
                .Include(t => t.InventoryCountItems)
                    .ThenInclude(i => i.Product)
                .Include(t => t.Warehouse)
                .Include(t => t.PerformedByNavigation)
                .Where(t => t.Warehouse != null && t.Warehouse.CompanyId == companyId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);

            return items;
        }

        public async Task<List<InventoryCountsTicket>> GetStockCountTicketsByStaffAsync(int companyId, int staffId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (staffId <= 0) throw new ArgumentException("Invalid staff id.", nameof(staffId));

            var items = await _context.InventoryCountsTickets
                .Include(t => t.InventoryCountItems)
                    .ThenInclude(i => i.Product)
                .Include(t => t.Warehouse)
                .Where(t => t.AssignedTo == staffId && t.Warehouse != null && t.Warehouse.CompanyId == companyId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);

            return items;
        }

        public async Task<InventoryCountsTicket> UpdateStockCountItemsAsync(int ticketId, IEnumerable<InventoryCountItem> items, int performedBy)
        {
            if (ticketId <= 0) throw new ArgumentException("Invalid ticket id.", nameof(ticketId));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (performedBy <= 0) throw new ArgumentException("Invalid performedBy.", nameof(performedBy));

            var ticket = await _context.InventoryCountsTickets
                .Include(t => t.InventoryCountItems)
                .FirstOrDefaultAsync(t => t.Id == ticketId)
                .ConfigureAwait(false);

            if (ticket == null)
                throw new InvalidOperationException($"InventoryCountsTicket with id {ticketId} not found.");

            var incomingProductIds = items
                .Where(i => i.ProductId.HasValue)
                .Select(i => i.ProductId!.Value)
                .Distinct()
                .ToList();

            var incomingLocations = items
                .Where(i => i.LocationId.HasValue)
                .Select(i => (ProductId: i.ProductId, LocationId: i.LocationId))
                .ToList();

            if (incomingProductIds.Any())
            {
                await ValidateProductsAvailabilityAsync(incomingProductIds, incomingLocations, ticket.WarehouseId).ConfigureAwait(false);
            }

            var existingItems = ticket.InventoryCountItems.ToList();
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            foreach (var incoming in items)
            {
                InventoryCountItem? existing = null;

                // 1) If client provided an explicit item Id, match by it.
                if (incoming.Id > 0)
                {
                    existing = existingItems.FirstOrDefault(i => i.Id == incoming.Id);
                }
                else
                {
                    // 2) If incoming specifies both ProductId and LocationId, prefer an existing item that matches both
                    if (incoming.ProductId.HasValue && incoming.LocationId.HasValue)
                    {
                        existing = existingItems.FirstOrDefault(i => i.ProductId == incoming.ProductId && i.LocationId == incoming.LocationId);
                    }

                    // 3) If still not found and incoming did NOT specify a LocationId, match a product-level item (LocationId == null)
                    if (existing == null && incoming.ProductId.HasValue && !incoming.LocationId.HasValue)
                    {
                        existing = existingItems.FirstOrDefault(i => i.ProductId == incoming.ProductId && !i.LocationId.HasValue);
                    }

                    // Note: if incoming has LocationId but only a product-level existing item exists, we DO NOT match it.
                    // This prevents overwriting location-specific counts with a separate location update; a new InventoryCountItem
                    // will be created instead so multiple bins/locations for the same product can be represented.
                }

                if (existing == null)
                {
                    var newItem = new InventoryCountItem
                    {
                        ProductId = incoming.ProductId,
                        LocationId = incoming.LocationId,
                        SystemQuantity = incoming.SystemQuantity,
                        CountedQuantity = incoming.CountedQuantity,
                        CountedBy = performedBy,
                        CountedAt = now,
                        Discrepancy = (incoming.CountedQuantity ?? 0) - (incoming.SystemQuantity ?? 0),
                        Status = incoming.CountedQuantity.HasValue ? true : (bool?)null,
                        FinalQuantity = incoming.CountedQuantity,
                        Description = incoming.Description
                    };
                    ticket.InventoryCountItems.Add(newItem);
                }
                else
                {
                    if (incoming.CountedQuantity.HasValue)
                    {
                        // If this is an existing item, update counted fields.
                        existing.CountedQuantity = incoming.CountedQuantity;
                        existing.CountedBy = performedBy;
                        existing.CountedAt = now;
                        existing.Discrepancy = (existing.CountedQuantity ?? 0) - (existing.SystemQuantity ?? 0);
                        existing.Status = true;
                        existing.FinalQuantity = existing.CountedQuantity;
                    }

                    // If incoming explicitly provides a LocationId and existing has none, assign it.
                    // Do not overwrite an existing LocationId with a different one.
                    if (incoming.LocationId.HasValue && !existing.LocationId.HasValue)
                        existing.LocationId = incoming.LocationId;

                    // Preserve description if provided (helps carry BinId strings that couldn't be resolved earlier)
                    if (!string.IsNullOrWhiteSpace(incoming.Description))
                        existing.Description = incoming.Description;
                }
            }

            var allItems = ticket.InventoryCountItems;
            var allHaveCount = allItems.Any() && allItems.All(i => i.CountedQuantity.HasValue);
            var anyHaveCount = allItems.Any(i => i.CountedQuantity.HasValue);

            if (allHaveCount)
            {
                ticket.Status = "Finished";
                ticket.FinishedDay = now;
            }
            else if (anyHaveCount)
            {
                ticket.Status = "In Progress";
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
            return ticket;
        }

        private async Task ValidateProductsAvailabilityAsync(
            IEnumerable<int> productIdsEnumerable,
            IEnumerable<(int? ProductId, int? LocationId)> productLocationPairs,
            int? warehouseId)
        {
            var productIds = productIdsEnumerable.Distinct().Where(id => id > 0).ToList();
            if (!productIds.Any()) return;


            var availableFromInventoriesQuery = _context.Inventories
                .AsNoTracking()
                .Where(inv => inv.ProductId.HasValue && productIds.Contains(inv.ProductId.Value) && (inv.Quantity ?? 0) > 0);

            if (warehouseId.HasValue)
                availableFromInventoriesQuery = availableFromInventoriesQuery.Where(inv => inv.WarehouseId == warehouseId.Value);

            var availableFromInventories = await availableFromInventoriesQuery
                .Select(inv => inv.ProductId!.Value)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);

            var availableFromLocationsQuery = _context.InventoryLocations
                .AsNoTracking()
                .Include(il => il.Inventory)
                .Where(il => (il.Quantity ?? 0) > 0 && il.Inventory != null && il.Inventory.ProductId.HasValue && productIds.Contains(il.Inventory.ProductId.Value));

            if (warehouseId.HasValue)
                availableFromLocationsQuery = availableFromLocationsQuery.Where(il => il.Inventory!.WarehouseId == warehouseId.Value);

            var availableFromLocations = await availableFromLocationsQuery
                .Select(il => il.Inventory!.ProductId!.Value)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);

            var availableSet = new HashSet<int>(availableFromInventories);
            foreach (var id in availableFromLocations) availableSet.Add(id);

            var missingProducts = productIds.Except(availableSet).ToList();

            if (missingProducts.Any())
            {
                var productInfos = await _context.Products
                    .AsNoTracking()
                    .Where(p => missingProducts.Contains(p.Id))
                    .Select(p => new { p.Id, p.Sku, p.Name })
                    .ToListAsync()
                    .ConfigureAwait(false);

                var names = productInfos.Select(p => string.IsNullOrWhiteSpace(p.Sku) ? (p.Name ?? p.Id.ToString()) : p.Sku).ToList();
                throw new InvalidOperationException($"Products without available inventory/locations with quantity > 0: {string.Join(", ", names)}");
            }


            var locationPairs = productLocationPairs.Where(p => p.LocationId.HasValue).ToList();
            if (locationPairs.Any())
            {
                var locationIds = locationPairs.Select(p => p.LocationId!.Value).Distinct().ToList();
                var locations = await _context.InventoryLocations
                    .AsNoTracking()
                    .Include(il => il.Inventory)
                    .Include(il => il.Shelf)
                        .ThenInclude(s => s.Zone)
                    .Where(il => locationIds.Contains(il.Id))
                    .ToListAsync()
                    .ConfigureAwait(false);

                var missingLocationIds = locationIds.Except(locations.Select(l => l.Id)).ToList();
                if (missingLocationIds.Any())
                    throw new InvalidOperationException($"InventoryLocation(s) not found: {string.Join(", ", missingLocationIds)}");


                var invalidPairs = new List<string>();
                foreach (var (prodIdNullable, locIdNullable) in locationPairs)
                {
                    var locId = locIdNullable!.Value;
                    var prodId = prodIdNullable;
                    var loc = locations.First(l => l.Id == locId);

                    var locQuantity = loc.Quantity ?? 0;
                    if (locQuantity <= 0)
                        invalidPairs.Add($"Location #{locId} has no quantity.");

                    var invProductId = loc.Inventory?.ProductId;
                    if (prodId.HasValue && invProductId.HasValue && prodId.Value != invProductId.Value)
                        invalidPairs.Add($"Location #{locId} does not belong to ProductId {prodId.Value}.");

                    if (warehouseId.HasValue)
                    {
                        // determine warehouse ownership via Inventory.WarehouseId or Shelf->Zone->WarehouseId fallback
                        var invWarehouseId = loc.Inventory?.WarehouseId;
                        if (!invWarehouseId.HasValue)
                        {
                            var shelfWarehouseId = loc.Shelf?.Zone?.WarehouseId;
                            if (!shelfWarehouseId.HasValue || shelfWarehouseId.Value != warehouseId.Value)
                                invalidPairs.Add($"Location #{locId} is not in warehouse {warehouseId.Value}.");
                        }
                        else if (invWarehouseId.Value != warehouseId.Value)
                        {
                            invalidPairs.Add($"Location #{locId} is not in warehouse {warehouseId.Value}.");
                        }
                    }
                }

                if (invalidPairs.Any())
                    throw new InvalidOperationException($"Invalid location assignments: {string.Join(" ; ", invalidPairs)}");
            }
        }
    }
}
