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
    public class WarehouseAssignmentRepository : IWarehouseAssignmentRepository
    {
        private readonly StorixDbContext _context;
        private static readonly string[] InactiveStatuses = { "completed", "cancelled", "canceled", "done", "closed" };

        public WarehouseAssignmentRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<Warehouse?> GetWarehouseByIdAsync(int warehouseId)
        {
            return await _context.Warehouses.FindAsync(warehouseId);
        }

        public async Task<List<Warehouse>> GetWarehousesByCompanyIdAsync(int companyId)
        {
            return await _context.Warehouses
                .Where(w => w.CompanyId == companyId)
                .OrderBy(w => w.Id)
                .ToListAsync();
        }

        public async Task<WarehouseAssignment?> GetAssignmentAsync(int userId, int warehouseId)
        {
            return await _context.WarehouseAssignments
                .Include(x => x.User)
                .Include(x => x.Warehouse)
                .FirstOrDefaultAsync(x => x.UserId == userId && x.WarehouseId == warehouseId);
        }

        public async Task<List<WarehouseAssignment>> GetAssignmentsByCompanyIdAsync(int companyId)
        {
            return await _context.WarehouseAssignments
                .Include(x => x.User)
                .Include(x => x.Warehouse)
                .Where(x => x.Warehouse != null && x.Warehouse.CompanyId == companyId)
                .OrderBy(x => x.Id)
                .ToListAsync();
        }

        public async Task<List<WarehouseAssignment>> GetAssignmentsByWarehouseIdAsync(int warehouseId)
        {
            return await _context.WarehouseAssignments
                .Include(x => x.User)
                .Include(x => x.Warehouse)
                .Where(x => x.WarehouseId == warehouseId)
                .OrderBy(x => x.Id)
                .ToListAsync();
        }

        public async Task<int> CountAssignmentsByWarehouseIdAsync(int warehouseId)
        {
            return await _context.WarehouseAssignments.CountAsync(x => x.WarehouseId == warehouseId);
        }

        public async Task<int> CountAssignmentsByUserIdAsync(int userId)
        {
            return await _context.WarehouseAssignments.CountAsync(x => x.UserId == userId);
        }

        public async Task<bool> HasActiveWarehouseOperationsAsync(int userId, int warehouseId)
        {
            var hasInboundOrders = await _context.InboundOrders.AnyAsync(o =>
                o.WarehouseId == warehouseId &&
                o.CreatedBy == userId &&
                (o.Status == null || !InactiveStatuses.Contains(o.Status.ToLower())));
            if (hasInboundOrders) return true;

            var hasOutboundOrders = await _context.OutboundOrders.AnyAsync(o =>
                o.WarehouseId == warehouseId &&
                (o.CreatedBy == userId || o.StaffId == userId) &&
                (o.Status == null || !InactiveStatuses.Contains(o.Status.ToLower())));
            if (hasOutboundOrders) return true;

            var hasTransferOrders = await _context.TransferOrders.AnyAsync(o =>
                (o.SourceWarehouseId == warehouseId || o.DestinationWarehouseId == warehouseId) &&
                o.CreatedBy == userId &&
                (o.Status == null || !InactiveStatuses.Contains(o.Status.ToLower())));
            if (hasTransferOrders) return true;

            var hasInboundRequests = await _context.InboundRequests.AnyAsync(r =>
                r.WarehouseId == warehouseId &&
                (r.RequestedBy == userId || r.ApprovedBy == userId) &&
                (r.Status == null || !InactiveStatuses.Contains(r.Status.ToLower())));
            if (hasInboundRequests) return true;

            var hasOutboundRequests = await _context.OutboundRequests.AnyAsync(r =>
                r.WarehouseId == warehouseId &&
                (r.RequestedBy == userId || r.ApprovedBy == userId) &&
                (r.Status == null || !InactiveStatuses.Contains(r.Status.ToLower())));
            if (hasOutboundRequests) return true;

            return await _context.InventoryCountsTickets.AnyAsync(t =>
                t.WarehouseId == warehouseId &&
                (t.AssignedTo == userId || t.PerformedBy == userId) &&
                (t.Status == null || !InactiveStatuses.Contains(t.Status.ToLower())));
        }

        public async Task<int> UpdateRoleInAssignmentsAsync(int userId, string roleInWarehouse)
        {
            var assignments = await _context.WarehouseAssignments
                .Where(x => x.UserId == userId)
                .ToListAsync();

            if (assignments.Count == 0) return 0;

            foreach (var assignment in assignments)
            {
                assignment.RoleInWarehouse = roleInWarehouse;
            }

            return await _context.SaveChangesAsync();
        }

        public async Task<int> AddAssignmentAsync(WarehouseAssignment assignment)
        {
            _context.WarehouseAssignments.Add(assignment);
            return await _context.SaveChangesAsync();
        }

        public async Task<bool> RemoveAssignmentAsync(WarehouseAssignment assignment)
        {
            _context.WarehouseAssignments.Remove(assignment);
            await _context.SaveChangesAsync();
            return true;
        }
        public async Task<Warehouse> CreateWarehouseAsync(Warehouse warehouse)
        {
            if (warehouse == null) throw new ArgumentNullException(nameof(warehouse));

            // Set timestamps for warehouse and related entities where applicable
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            warehouse.CreatedAt = now;
            if (warehouse.StorageZones != null)
            {
                foreach (var z in warehouse.StorageZones)
                {
                    z.CreatedAt = now;
                    if (z.Shelves != null)
                    {
                        foreach (var s in z.Shelves)
                        {
                            s.CreatedAt = now;
                            // shelf levels/bins do not have CreatedAt in model, but set IdCode if available
                        }
                    }
                }
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Warehouses.Add(warehouse);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                return warehouse;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        public async Task<bool> UpdateWarehouseStructureAsync(int warehouseId, Warehouse warehouseStructure)
        {
            if (warehouseStructure == null) throw new System.ArgumentNullException(nameof(warehouseStructure));
            if (warehouseId <= 0) throw new System.ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            // Load existing warehouse with related collections to remove them safely
            var existing = await _context.Warehouses
                .Include(w => w.NavEdges)
                    .ThenInclude(e => e.NodeFromNavigation)
                .Include(w => w.NavEdges)
                    .ThenInclude(e => e.NodeToNavigation)
                .Include(w => w.NavNodes)
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                        .ThenInclude(s => s.ShelfLevels)
                            .ThenInclude(l => l.ShelfLevelBins)
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                        .ThenInclude(s => s.ShelfNodes)
                            .ThenInclude(sn => sn.Node)
                .FirstOrDefaultAsync(w => w.Id == warehouseId);

            if (existing == null) throw new System.InvalidOperationException("Warehouse not found.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Explicitly remove nested children to avoid leftover duplicates when cascade is not configured
                // Remove shelf level bins
                var bins = existing.StorageZones?
                    .SelectMany(z => z.Shelves ?? Enumerable.Empty<Shelf>())
                    .SelectMany(s => s.ShelfLevels ?? Enumerable.Empty<ShelfLevel>())
                    .SelectMany(l => l.ShelfLevelBins ?? Enumerable.Empty<ShelfLevelBin>())
                    .ToList() ?? new List<ShelfLevelBin>();
                if (bins.Any()) _context.ShelfLevelBins.RemoveRange(bins);

                // Remove shelf levels
                var levels = existing.StorageZones?
                    .SelectMany(z => z.Shelves ?? Enumerable.Empty<Shelf>())
                    .SelectMany(s => s.ShelfLevels ?? Enumerable.Empty<ShelfLevel>())
                    .ToList() ?? new List<ShelfLevel>();
                if (levels.Any()) _context.ShelfLevels.RemoveRange(levels);

                // Remove shelf nodes (associations)
                var shelfNodes = existing.StorageZones?
                    .SelectMany(z => z.Shelves ?? Enumerable.Empty<Shelf>())
                    .SelectMany(s => s.ShelfNodes ?? Enumerable.Empty<ShelfNode>())
                    .ToList() ?? new List<ShelfNode>();
                if (shelfNodes.Any()) _context.ShelfNodes.RemoveRange(shelfNodes);

                // Remove shelves
                var shelves = existing.StorageZones?
                    .SelectMany(z => z.Shelves ?? Enumerable.Empty<Shelf>())
                    .ToList() ?? new List<Shelf>();
                if (shelves.Any()) _context.Shelves.RemoveRange(shelves);

                // Remove zones
                var zones = existing.StorageZones?.ToList() ?? new List<StorageZone>();
                if (zones.Any()) _context.StorageZones.RemoveRange(zones);

                // Remove nav edges
                var edges = existing.NavEdges?.ToList() ?? new List<NavEdge>();
                if (edges.Any()) _context.NavEdges.RemoveRange(edges);

                // Remove nav nodes
                var nodes = existing.NavNodes?.ToList() ?? new List<NavNode>();
                if (nodes.Any()) _context.NavNodes.RemoveRange(nodes);

                await _context.SaveChangesAsync();

                // Update dimensions
                existing.Width = warehouseStructure.Width;
                existing.Height = warehouseStructure.Height;
                existing.Length = warehouseStructure.Length;

                var now = System.DateTime.SpecifyKind(System.DateTime.UtcNow, System.DateTimeKind.Unspecified);

                // Keep track of nodes we add to avoid adding the same logical node twice
                var addedNodesByIdCode = new Dictionary<string, NavNode>(System.StringComparer.OrdinalIgnoreCase);

                // Add new NavNodes (if any)
                if (warehouseStructure.NavNodes != null)
                {
                    foreach (var n in warehouseStructure.NavNodes)
                    {
                        // Use the provided IdCode as key; avoid adding duplicate node instances
                        n.Warehouse = existing;
                        n.Id = 0;
                        _context.NavNodes.Add(n);
                        if (!string.IsNullOrWhiteSpace(n.IdCode) && !addedNodesByIdCode.ContainsKey(n.IdCode))
                            addedNodesByIdCode[n.IdCode] = n;
                    }
                }

                // Add new StorageZones (and nested shelves/levels/bins)
                if (warehouseStructure.StorageZones != null)
                {
                    foreach (var z in warehouseStructure.StorageZones)
                    {
                        z.Warehouse = existing;
                        z.Id = 0;
                        z.CreatedAt = z.CreatedAt ?? now;
                        _context.StorageZones.Add(z);

                        if (z.Shelves != null)
                        {
                            foreach (var s in z.Shelves)
                            {
                                s.Zone = z;
                                s.Id = 0;
                                s.CreatedAt = s.CreatedAt ?? now;

                                if (s.ShelfLevels != null)
                                {
                                    foreach (var lvl in s.ShelfLevels)
                                    {
                                        lvl.Shelf = s;
                                        lvl.Id = 0;
                                        if (lvl.ShelfLevelBins != null)
                                        {
                                            foreach (var b in lvl.ShelfLevelBins)
                                            {
                                                b.Level = lvl;
                                                b.Id = 0;
                                            }
                                        }
                                    }
                                }

                                if (s.ShelfNodes != null)
                                {
                                    foreach (var sn in s.ShelfNodes)
                                    {
                                        sn.Shelf = s;
                                        sn.Id = 0;

                                        if (sn.Node != null)
                                        {
                                            // If the node was added previously in NavNodes collection, reuse it.
                                            if (!string.IsNullOrWhiteSpace(sn.Node.IdCode) && addedNodesByIdCode.TryGetValue(sn.Node.IdCode, out var existingNode))
                                            {
                                                // reuse existing tracked node instance
                                                sn.Node = existingNode;
                                            }
                                            else
                                            {
                                                // add node and register it
                                                sn.Node.Warehouse = existing;
                                                sn.Node.Id = 0;
                                                _context.NavNodes.Add(sn.Node);
                                                if (!string.IsNullOrWhiteSpace(sn.Node.IdCode))
                                                    addedNodesByIdCode[sn.Node.IdCode] = sn.Node;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Add new NavEdges (after nodes are added) and ensure they reference tracked node instances
                if (warehouseStructure.NavEdges != null)
                {
                    foreach (var e in warehouseStructure.NavEdges)
                    {
                        // If edge object contains NodeFromNavigation/NodeToNavigation with IdCode, try to replace with tracked instance
                        if (e.NodeFromNavigation != null && !string.IsNullOrWhiteSpace(e.NodeFromNavigation.IdCode) && addedNodesByIdCode.TryGetValue(e.NodeFromNavigation.IdCode, out var fromNode))
                        {
                            e.NodeFromNavigation = fromNode;
                        }

                        if (e.NodeToNavigation != null && !string.IsNullOrWhiteSpace(e.NodeToNavigation.IdCode) && addedNodesByIdCode.TryGetValue(e.NodeToNavigation.IdCode, out var toNode))
                        {
                            e.NodeToNavigation = toNode;
                        }

                        e.Warehouse = existing;
                        e.Id = 0;
                        _context.NavEdges.Add(e);
                    }
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                _context.ChangeTracker.Clear();
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        public async Task<Warehouse?> GetWarehouseWithStructureAsync(int warehouseId)
        {
            if (warehouseId <= 0) return null;

            return await _context.Warehouses
                .Include(w => w.Company)
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                        .ThenInclude(s => s.ShelfLevels)
                            .ThenInclude(l => l.ShelfLevelBins)
                                .ThenInclude(b => b.Inventory)
                                    .ThenInclude(i => i.Product)
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                        .ThenInclude(s => s.ShelfNodes)
                            .ThenInclude(sn => sn.Node)
                .Include(w => w.NavNodes)
                .Include(w => w.NavEdges)
                    .ThenInclude(e => e.NodeFromNavigation)
                .Include(w => w.NavEdges)
                    .ThenInclude(e => e.NodeToNavigation)
                .FirstOrDefaultAsync(w => w.Id == warehouseId);
        }
        public async Task<bool> DeleteWarehouseAsync(int warehouseId)
        {
            if (warehouseId <= 0) throw new System.InvalidOperationException("Invalid warehouse id.");

            var warehouse = await _context.Warehouses
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                        .ThenInclude(s => s.ShelfLevels)
                            .ThenInclude(l => l.ShelfLevelBins)
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                        .ThenInclude(s => s.ShelfNodes)
                            .ThenInclude(sn => sn.Node)
                .Include(w => w.NavEdges)
                .Include(w => w.NavNodes)
                .Include(w => w.WarehouseAssignments)
                .FirstOrDefaultAsync(w => w.Id == warehouseId);

            if (warehouse == null) return false;

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Remove nested shelf level bins
                var bins = warehouse.StorageZones?
                    .SelectMany(z => z.Shelves ?? Enumerable.Empty<Shelf>())
                    .SelectMany(s => s.ShelfLevels ?? Enumerable.Empty<ShelfLevel>())
                    .SelectMany(l => l.ShelfLevelBins ?? Enumerable.Empty<ShelfLevelBin>())
                    .ToList() ?? new List<ShelfLevelBin>();
                if (bins.Any()) _context.ShelfLevelBins.RemoveRange(bins);

                // Remove shelf levels
                var levels = warehouse.StorageZones?
                    .SelectMany(z => z.Shelves ?? Enumerable.Empty<Shelf>())
                    .SelectMany(s => s.ShelfLevels ?? Enumerable.Empty<ShelfLevel>())
                    .ToList() ?? new List<ShelfLevel>();
                if (levels.Any()) _context.ShelfLevels.RemoveRange(levels);

                // Remove shelf nodes (associations)
                var shelfNodes = warehouse.StorageZones?
                    .SelectMany(z => z.Shelves ?? Enumerable.Empty<Shelf>())
                    .SelectMany(s => s.ShelfNodes ?? Enumerable.Empty<ShelfNode>())
                    .ToList() ?? new List<ShelfNode>();
                if (shelfNodes.Any()) _context.ShelfNodes.RemoveRange(shelfNodes);

                // Remove shelves
                var shelves = warehouse.StorageZones?
                    .SelectMany(z => z.Shelves ?? Enumerable.Empty<Shelf>())
                    .ToList() ?? new List<Shelf>();
                if (shelves.Any()) _context.Shelves.RemoveRange(shelves);

                // Remove zones
                var zones = warehouse.StorageZones?.ToList() ?? new List<StorageZone>();
                if (zones.Any()) _context.StorageZones.RemoveRange(zones);

                // Remove nav edges
                var edges = warehouse.NavEdges?.ToList() ?? new List<NavEdge>();
                if (edges.Any()) _context.NavEdges.RemoveRange(edges);

                // Remove nav nodes
                var nodes = warehouse.NavNodes?.ToList() ?? new List<NavNode>();
                if (nodes.Any()) _context.NavNodes.RemoveRange(nodes);

                // Remove warehouse assignments
                var assignments = warehouse.WarehouseAssignments?.ToList() ?? new List<WarehouseAssignment>();
                if (assignments.Any()) _context.WarehouseAssignments.RemoveRange(assignments);

                // Finally remove the warehouse itself
                _context.Warehouses.Remove(warehouse);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
