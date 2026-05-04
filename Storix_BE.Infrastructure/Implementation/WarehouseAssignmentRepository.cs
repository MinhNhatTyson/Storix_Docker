using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
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
        public async Task<List<ZoneResponse>> GetZoneIdsByWarehouseIdAsync(int warehouseId)
        {
            if (warehouseId <= 0) return new List<ZoneResponse>();
            var list = await _context.StorageZones
                .AsNoTracking()
                .Where(z => z.WarehouseId == warehouseId)
                .Select(z => new ZoneResponse
                {
                    Id = z.Id,
                    Code = z.Code ?? "",
                    IsEsd = z.IsEsd,
                    IsMsd = z.IsMsd,
                    IsCold = z.IsCold,
                    IsVulnerable = z.IsVulnerable,
                    IsHighValue = z.IsHighValue,
                    Width = z.Width,
                    Height = z.Height,
                    Length = z.Length
                })
                .ToListAsync();
            return list;
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
            if (warehouseStructure == null) throw new ArgumentNullException(nameof(warehouseStructure));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            var existing = await _context.Warehouses
                .Include(w => w.NavEdges)
                .Include(w => w.NavNodes)
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                        .ThenInclude(s => s.ShelfLevels)
                            .ThenInclude(l => l.ShelfLevelBins)
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                        .ThenInclude(s => s.ShelfNodes)
                .FirstOrDefaultAsync(w => w.Id == warehouseId);

            if (existing == null) throw new InvalidOperationException("Warehouse not found.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Update warehouse dimensions
                existing.Width = warehouseStructure.Width;
                existing.Height = warehouseStructure.Height;
                existing.Length = warehouseStructure.Length;

                var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                // ── NavNodes ──────────────────────────────────────────────────────────
                var incomingNodes = warehouseStructure.NavNodes ?? new List<NavNode>();
                var incomingNodeCodes = incomingNodes
                    .Where(n => !string.IsNullOrWhiteSpace(n.IdCode))
                    .Select(n => n.IdCode!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Remove nodes that no longer exist
                var nodesToRemove = existing.NavNodes
                    .Where(n => string.IsNullOrWhiteSpace(n.IdCode) || !incomingNodeCodes.Contains(n.IdCode))
                    .ToList();
                if (nodesToRemove.Any()) _context.NavNodes.RemoveRange(nodesToRemove);

                // Build a map of existing nodes by IdCode for upsert
                var existingNodeByCode = existing.NavNodes
                    .Where(n => !string.IsNullOrWhiteSpace(n.IdCode))
                    .ToDictionary(n => n.IdCode!, StringComparer.OrdinalIgnoreCase);

                // Upsert nodes; track all resulting node entities by IdCode
                var nodeByCode = new Dictionary<string, NavNode>(StringComparer.OrdinalIgnoreCase);
                foreach (var incoming in incomingNodes)
                {
                    if (string.IsNullOrWhiteSpace(incoming.IdCode))
                    {
                        // No IdCode — always insert
                        incoming.WarehouseId = warehouseId;
                        incoming.Id = 0;
                        _context.NavNodes.Add(incoming);
                        continue;
                    }

                    if (existingNodeByCode.TryGetValue(incoming.IdCode, out var existingNode))
                    {
                        // Update in place
                        existingNode.XCoordinate = incoming.XCoordinate;
                        existingNode.YCoordinate = incoming.YCoordinate;
                        existingNode.Type = incoming.Type;
                        existingNode.Radius = incoming.Radius;
                        existingNode.Side = incoming.Side;
                        nodeByCode[incoming.IdCode] = existingNode;
                    }
                    else
                    {
                        // Insert new
                        incoming.WarehouseId = warehouseId;
                        incoming.Id = 0;
                        _context.NavNodes.Add(incoming);
                        nodeByCode[incoming.IdCode] = incoming;
                    }
                }

                // Save so new nodes get their IDs before edges reference them
                await _context.SaveChangesAsync();

                // Refresh nodeByCode with newly inserted nodes (they now have IDs)
                foreach (var incoming in incomingNodes.Where(n => !string.IsNullOrWhiteSpace(n.IdCode)))
                {
                    if (!nodeByCode.ContainsKey(incoming.IdCode!))
                        nodeByCode[incoming.IdCode!] = incoming;
                }

                // ── NavEdges ──────────────────────────────────────────────────────────
                var incomingEdges = warehouseStructure.NavEdges ?? new List<NavEdge>();
                var incomingEdgeCodes = incomingEdges
                    .Where(e => !string.IsNullOrWhiteSpace(e.IdCode))
                    .Select(e => e.IdCode!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var edgesToRemove = existing.NavEdges
                    .Where(e => string.IsNullOrWhiteSpace(e.IdCode) || !incomingEdgeCodes.Contains(e.IdCode))
                    .ToList();
                if (edgesToRemove.Any()) _context.NavEdges.RemoveRange(edgesToRemove);

                var existingEdgeByCode = existing.NavEdges
                    .Where(e => !string.IsNullOrWhiteSpace(e.IdCode))
                    .ToDictionary(e => e.IdCode!, StringComparer.OrdinalIgnoreCase);

                foreach (var incoming in incomingEdges)
                {
                    // Resolve node references by IdCode
                    NavNode? fromNode = null;
                    NavNode? toNode = null;

                    if (incoming.NodeFromNavigation != null && !string.IsNullOrWhiteSpace(incoming.NodeFromNavigation.IdCode))
                        nodeByCode.TryGetValue(incoming.NodeFromNavigation.IdCode, out fromNode);

                    if (incoming.NodeToNavigation != null && !string.IsNullOrWhiteSpace(incoming.NodeToNavigation.IdCode))
                        nodeByCode.TryGetValue(incoming.NodeToNavigation.IdCode, out toNode);

                    if (string.IsNullOrWhiteSpace(incoming.IdCode))
                    {
                        incoming.WarehouseId = warehouseId;
                        incoming.Id = 0;
                        incoming.NodeFromNavigation = fromNode;
                        incoming.NodeToNavigation = toNode;
                        _context.NavEdges.Add(incoming);
                        continue;
                    }

                    if (existingEdgeByCode.TryGetValue(incoming.IdCode, out var existingEdge))
                    {
                        existingEdge.Distance = incoming.Distance;
                        if (fromNode != null) { existingEdge.NodeFrom = fromNode.Id; existingEdge.NodeFromNavigation = fromNode; }
                        if (toNode != null) { existingEdge.NodeTo = toNode.Id; existingEdge.NodeToNavigation = toNode; }
                    }
                    else
                    {
                        incoming.WarehouseId = warehouseId;
                        incoming.Id = 0;
                        incoming.NodeFromNavigation = fromNode;
                        incoming.NodeToNavigation = toNode;
                        _context.NavEdges.Add(incoming);
                    }
                }

                // ── StorageZones ──────────────────────────────────────────────────────
                var incomingZones = warehouseStructure.StorageZones ?? new List<StorageZone>();
                var incomingZoneCodes = incomingZones
                    .Where(z => !string.IsNullOrWhiteSpace(z.IdCode))
                    .Select(z => z.IdCode!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Remove zones (and cascade: shelves → levels → bins → shelfNodes) no longer present
                var zonesToRemove = existing.StorageZones
                    .Where(z => string.IsNullOrWhiteSpace(z.IdCode) || !incomingZoneCodes.Contains(z.IdCode))
                    .ToList();

                foreach (var zone in zonesToRemove)
                {
                    foreach (var shelf in zone.Shelves ?? Enumerable.Empty<Shelf>())
                    {
                        var binsToDelete = shelf.ShelfLevels?.SelectMany(l => l.ShelfLevelBins ?? Enumerable.Empty<ShelfLevelBin>()).ToList() ?? new List<ShelfLevelBin>();
                        if (binsToDelete.Any()) _context.ShelfLevelBins.RemoveRange(binsToDelete);

                        var levelsToDelete = shelf.ShelfLevels?.ToList() ?? new List<ShelfLevel>();
                        if (levelsToDelete.Any()) _context.ShelfLevels.RemoveRange(levelsToDelete);

                        var shelfNodesToDelete = shelf.ShelfNodes?.ToList() ?? new List<ShelfNode>();
                        if (shelfNodesToDelete.Any()) _context.ShelfNodes.RemoveRange(shelfNodesToDelete);
                    }

                    var shelvesToDelete = zone.Shelves?.ToList() ?? new List<Shelf>();
                    if (shelvesToDelete.Any()) _context.Shelves.RemoveRange(shelvesToDelete);
                }
                if (zonesToRemove.Any()) _context.StorageZones.RemoveRange(zonesToRemove);

                var existingZoneByCode = existing.StorageZones
                    .Where(z => !string.IsNullOrWhiteSpace(z.IdCode))
                    .ToDictionary(z => z.IdCode!, StringComparer.OrdinalIgnoreCase);

                foreach (var incomingZone in incomingZones)
                {
                    StorageZone targetZone;

                    if (!string.IsNullOrWhiteSpace(incomingZone.IdCode) && existingZoneByCode.TryGetValue(incomingZone.IdCode, out var existingZone))
                    {
                        // Update zone fields
                        existingZone.Code = incomingZone.Code;
                        existingZone.XCoordinate = incomingZone.XCoordinate;
                        existingZone.YCoordinate = incomingZone.YCoordinate;
                        existingZone.Width = incomingZone.Width;
                        existingZone.Height = incomingZone.Height;
                        existingZone.Length = incomingZone.Length;
                        existingZone.IsEsd = incomingZone.IsEsd;
                        existingZone.IsMsd = incomingZone.IsMsd;
                        existingZone.IsCold = incomingZone.IsCold;
                        existingZone.IsVulnerable = incomingZone.IsVulnerable;
                        existingZone.IsHighValue = incomingZone.IsHighValue;
                        targetZone = existingZone;
                    }
                    else
                    {
                        // Insert new zone
                        incomingZone.WarehouseId = warehouseId;
                        incomingZone.Id = 0;
                        incomingZone.CreatedAt = incomingZone.CreatedAt ?? now;
                        _context.StorageZones.Add(incomingZone);
                        targetZone = incomingZone;
                    }

                    // ── Shelves ───────────────────────────────────────────────────────
                    var incomingShelves = incomingZone.Shelves ?? new List<Shelf>();
                    var incomingShelfCodes = incomingShelves
                        .Where(s => !string.IsNullOrWhiteSpace(s.IdCode))
                        .Select(s => s.IdCode!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var existingShelves = targetZone.Shelves?.ToList() ?? new List<Shelf>();

                    var shelvesToRemove = existingShelves
                        .Where(s => string.IsNullOrWhiteSpace(s.IdCode) || !incomingShelfCodes.Contains(s.IdCode))
                        .ToList();

                    foreach (var shelf in shelvesToRemove)
                    {
                        var binsToDelete = shelf.ShelfLevels?.SelectMany(l => l.ShelfLevelBins ?? Enumerable.Empty<ShelfLevelBin>()).ToList() ?? new List<ShelfLevelBin>();
                        if (binsToDelete.Any()) _context.ShelfLevelBins.RemoveRange(binsToDelete);

                        var levelsToDelete = shelf.ShelfLevels?.ToList() ?? new List<ShelfLevel>();
                        if (levelsToDelete.Any()) _context.ShelfLevels.RemoveRange(levelsToDelete);

                        var shelfNodesToDelete = shelf.ShelfNodes?.ToList() ?? new List<ShelfNode>();
                        if (shelfNodesToDelete.Any()) _context.ShelfNodes.RemoveRange(shelfNodesToDelete);
                    }
                    if (shelvesToRemove.Any()) _context.Shelves.RemoveRange(shelvesToRemove);

                    var existingShelfByCode = existingShelves
                        .Where(s => !string.IsNullOrWhiteSpace(s.IdCode))
                        .ToDictionary(s => s.IdCode!, StringComparer.OrdinalIgnoreCase);

                    foreach (var incomingShelf in incomingShelves)
                    {
                        Shelf targetShelf;

                        if (!string.IsNullOrWhiteSpace(incomingShelf.IdCode) && existingShelfByCode.TryGetValue(incomingShelf.IdCode, out var existingShelf))
                        {
                            // Update shelf fields
                            existingShelf.Code = incomingShelf.Code;
                            existingShelf.XCoordinate = incomingShelf.XCoordinate;
                            existingShelf.YCoordinate = incomingShelf.YCoordinate;
                            existingShelf.Width = incomingShelf.Width;
                            existingShelf.Height = incomingShelf.Height;
                            existingShelf.Length = incomingShelf.Length;
                            existingShelf.Capacity = incomingShelf.Capacity;
                            targetShelf = existingShelf;
                        }
                        else
                        {
                            // Insert new shelf
                            incomingShelf.Zone = targetZone;
                            incomingShelf.Id = 0;
                            incomingShelf.CreatedAt = incomingShelf.CreatedAt ?? now;
                            _context.Shelves.Add(incomingShelf);
                            targetShelf = incomingShelf;
                        }

                        // ── ShelfLevels ───────────────────────────────────────────────
                        var incomingLevels = incomingShelf.ShelfLevels ?? new List<ShelfLevel>();
                        var incomingLevelCodes = incomingLevels
                            .Where(l => !string.IsNullOrWhiteSpace(l.IdCode))
                            .Select(l => l.IdCode!)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var existingLevels = targetShelf.ShelfLevels?.ToList() ?? new List<ShelfLevel>();

                        var levelsToRemove = existingLevels
                            .Where(l => string.IsNullOrWhiteSpace(l.IdCode) || !incomingLevelCodes.Contains(l.IdCode))
                            .ToList();

                        foreach (var level in levelsToRemove)
                        {
                            var binsToDelete = level.ShelfLevelBins?.ToList() ?? new List<ShelfLevelBin>();
                            if (binsToDelete.Any()) _context.ShelfLevelBins.RemoveRange(binsToDelete);
                        }
                        if (levelsToRemove.Any()) _context.ShelfLevels.RemoveRange(levelsToRemove);

                        var existingLevelByCode = existingLevels
                            .Where(l => !string.IsNullOrWhiteSpace(l.IdCode))
                            .ToDictionary(l => l.IdCode!, StringComparer.OrdinalIgnoreCase);

                        foreach (var incomingLevel in incomingLevels)
                        {
                            ShelfLevel targetLevel;

                            if (!string.IsNullOrWhiteSpace(incomingLevel.IdCode) && existingLevelByCode.TryGetValue(incomingLevel.IdCode, out var existingLevel))
                            {
                                existingLevel.Code = incomingLevel.Code;
                                targetLevel = existingLevel;
                            }
                            else
                            {
                                incomingLevel.Shelf = targetShelf;
                                incomingLevel.Id = 0;
                                _context.ShelfLevels.Add(incomingLevel);
                                targetLevel = incomingLevel;
                            }

                            // ── ShelfLevelBins ─────────────────────────────────────────
                            var incomingBins = incomingLevel.ShelfLevelBins ?? new List<ShelfLevelBin>();
                            var incomingBinCodes = incomingBins
                                .Where(b => !string.IsNullOrWhiteSpace(b.IdCode))
                                .Select(b => b.IdCode!)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                            var existingBins = targetLevel.ShelfLevelBins?.ToList() ?? new List<ShelfLevelBin>();

                            var binsToRemove = existingBins
                                .Where(b => string.IsNullOrWhiteSpace(b.IdCode) || !incomingBinCodes.Contains(b.IdCode))
                                .ToList();
                            if (binsToRemove.Any()) _context.ShelfLevelBins.RemoveRange(binsToRemove);

                            var existingBinByCode = existingBins
                                .Where(b => !string.IsNullOrWhiteSpace(b.IdCode))
                                .ToDictionary(b => b.IdCode!, StringComparer.OrdinalIgnoreCase);

                            foreach (var incomingBin in incomingBins)
                            {
                                if (!string.IsNullOrWhiteSpace(incomingBin.IdCode) && existingBinByCode.TryGetValue(incomingBin.IdCode, out var existingBin))
                                {
                                    // Update bin fields — preserve InventoryId and Percentage (managed by inbound flow)
                                    existingBin.Code = incomingBin.Code;
                                    existingBin.Width = incomingBin.Width;
                                    existingBin.Height = incomingBin.Height;
                                    existingBin.Length = incomingBin.Length;
                                    existingBin.Status = incomingBin.Status;
                                }
                                else
                                {
                                    incomingBin.Level = targetLevel;
                                    incomingBin.Id = 0;
                                    _context.ShelfLevelBins.Add(incomingBin);
                                }
                            }
                        }

                        // ── ShelfNodes ────────────────────────────────────────────────
                        var incomingShelfNodes = incomingShelf.ShelfNodes ?? new List<ShelfNode>();
                        var incomingShelfNodeCodes = incomingShelfNodes
                            .Where(sn => !string.IsNullOrWhiteSpace(sn.IdCode))
                            .Select(sn => sn.IdCode!)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var existingShelfNodes = targetShelf.ShelfNodes?.ToList() ?? new List<ShelfNode>();

                        var shelfNodesToRemove = existingShelfNodes
                            .Where(sn => string.IsNullOrWhiteSpace(sn.IdCode) || !incomingShelfNodeCodes.Contains(sn.IdCode))
                            .ToList();
                        if (shelfNodesToRemove.Any()) _context.ShelfNodes.RemoveRange(shelfNodesToRemove);

                        var existingShelfNodeByCode = existingShelfNodes
                            .Where(sn => !string.IsNullOrWhiteSpace(sn.IdCode))
                            .ToDictionary(sn => sn.IdCode!, StringComparer.OrdinalIgnoreCase);

                        foreach (var incomingShelfNode in incomingShelfNodes)
                        {
                            // Resolve the NavNode this ShelfNode points to
                            NavNode? resolvedNode = null;
                            if (incomingShelfNode.Node != null && !string.IsNullOrWhiteSpace(incomingShelfNode.Node.IdCode))
                                nodeByCode.TryGetValue(incomingShelfNode.Node.IdCode, out resolvedNode);

                            if (!string.IsNullOrWhiteSpace(incomingShelfNode.IdCode) && existingShelfNodeByCode.TryGetValue(incomingShelfNode.IdCode, out var existingShelfNode))
                            {
                                if (resolvedNode != null)
                                {
                                    existingShelfNode.NodeId = resolvedNode.Id;
                                    existingShelfNode.Node = resolvedNode;
                                }
                            }
                            else
                            {
                                incomingShelfNode.Shelf = targetShelf;
                                incomingShelfNode.Id = 0;
                                if (resolvedNode != null)
                                {
                                    incomingShelfNode.Node = resolvedNode;
                                    incomingShelfNode.NodeId = resolvedNode.Id;
                                }
                                _context.ShelfNodes.Add(incomingShelfNode);
                            }
                        }
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

            // 1. Load the warehouse shell first (fast, no joins)
            var warehouse = await _context.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == warehouseId);

            if (warehouse == null) return null;

            // 2. Fire all heavy sub-queries in parallel — each is a flat, focused query
            var nodes = await _context.NavNodes
    .AsNoTracking()
    .Where(n => n.WarehouseId == warehouseId)
    .ToListAsync();

            var edges = await _context.NavEdges
                .AsNoTracking()
                .Where(e => e.WarehouseId == warehouseId)
                .Select(e => new NavEdge
                {
                    Id = e.Id,
                    IdCode = e.IdCode,
                    NodeFrom = e.NodeFrom,
                    NodeTo = e.NodeTo,
                    Distance = e.Distance,
                    WarehouseId = e.WarehouseId,
                    NodeFromNavigation = new NavNode { Id = e.NodeFromNavigation!.Id, IdCode = e.NodeFromNavigation.IdCode },
                    NodeToNavigation = new NavNode { Id = e.NodeToNavigation!.Id, IdCode = e.NodeToNavigation.IdCode }
                })
                .ToListAsync();

            var zones = await _context.StorageZones
                .AsNoTracking()
                .Where(z => z.WarehouseId == warehouseId)
                .ToListAsync();

            var shelves = await _context.Shelves
                .AsNoTracking()
                .Where(s => s.Zone != null && s.Zone.WarehouseId == warehouseId)
                .ToListAsync();

            var levels = await _context.ShelfLevels
                .AsNoTracking()
                .Where(l => l.Shelf != null && l.Shelf.Zone != null && l.Shelf.Zone.WarehouseId == warehouseId)
                .ToListAsync();

            var bins = await _context.ShelfLevelBins
                .AsNoTracking()
                .Where(b => b.Level != null && b.Level.Shelf != null
                            && b.Level.Shelf.Zone != null
                            && b.Level.Shelf.Zone.WarehouseId == warehouseId)
                .Select(b => new ShelfLevelBin
                {
                    Id = b.Id,
                    LevelId = b.LevelId,
                    Code = b.Code,
                    IdCode = b.IdCode,
                    Width = b.Width,
                    Height = b.Height,
                    Length = b.Length,
                    Status = b.Status,
                    Percentage = b.Percentage,
                    InventoryId = b.InventoryId,
                    Inventory = b.InventoryId == null ? null : new Inventory
                    {
                        Id = b.Inventory!.Id,
                        ProductId = b.Inventory.ProductId
                    }
                })
                .ToListAsync();

            var shelfNodes = await _context.ShelfNodes
                .AsNoTracking()
                .Where(sn => sn.Shelf != null && sn.Shelf.Zone != null
                             && sn.Shelf.Zone.WarehouseId == warehouseId)
                .Select(sn => new ShelfNode
                {
                    Id = sn.Id,
                    ShelfId = sn.ShelfId,
                    NodeId = sn.NodeId,
                    IdCode = sn.IdCode,
                    Node = new NavNode
                    {
                        Id = sn.Node!.Id,
                        IdCode = sn.Node.IdCode,
                        XCoordinate = sn.Node.XCoordinate,
                        YCoordinate = sn.Node.YCoordinate,
                        Side = sn.Node.Side,
                        Type = sn.Node.Type,
                        Radius = sn.Node.Radius
                    }
                })
                .ToListAsync();

            // 3. Stitch the object graph in memory (pure dictionary lookups — O(n))
            var levelsByShelfId = levels.GroupBy(l => l.ShelfId ?? 0)
                .ToDictionary(g => g.Key, g => g.ToList());

            var binsByLevelId = bins.GroupBy(b => b.LevelId ?? 0)
                .ToDictionary(g => g.Key, g => g.ToList());

            var shelfNodesByShelfId = shelfNodes.GroupBy(sn => sn.ShelfId ?? 0)
                .ToDictionary(g => g.Key, g => g.ToList());

            var shelvesByZoneId = shelves.GroupBy(s => s.ZoneId ?? 0)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var level in levels)
            {
                level.ShelfLevelBins = binsByLevelId.TryGetValue(level.Id, out var b) ? b : new List<ShelfLevelBin>();
            }

            foreach (var shelf in shelves)
            {
                shelf.ShelfLevels = levelsByShelfId.TryGetValue(shelf.Id, out var l) ? l : new List<ShelfLevel>();
                shelf.ShelfNodes = shelfNodesByShelfId.TryGetValue(shelf.Id, out var sn) ? sn : new List<ShelfNode>();
            }

            foreach (var zone in zones)
            {
                zone.Shelves = shelvesByZoneId.TryGetValue(zone.Id, out var s) ? s : new List<Shelf>();
            }

            warehouse.NavNodes = nodes;
            warehouse.NavEdges = edges;
            warehouse.StorageZones = zones;

            return warehouse;
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
        public async Task<bool> DisableWarehouseAsync(int warehouseId)
        {
            var warehouse = await _context.Warehouses
                .FirstOrDefaultAsync(w => w.Id == warehouseId)
                .ConfigureAwait(false);

            if (warehouse == null)
            {
                return false;
            }

            // Find active inbound orders for this warehouse
            var activeInboundIds = await _context.InboundOrders
                .Where(o => o.WarehouseId == warehouseId
                            && (o.Status == null || !InactiveStatuses.Contains(o.Status.ToLower())))
                .Select(o => o.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            // Find active outbound orders for this warehouse
            var activeOutboundIds = await _context.OutboundOrders
                .Where(o => o.WarehouseId == warehouseId
                            && (o.Status == null || !InactiveStatuses.Contains(o.Status.ToLower())))
                .Select(o => o.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            if (activeInboundIds.Any() || activeOutboundIds.Any())
            {
                var messages = new List<string>();
                if (activeInboundIds.Any())
                    messages.Add($"Active inbound orders: {string.Join(", ", activeInboundIds)}");
                if (activeOutboundIds.Any())
                    messages.Add($"Active outbound orders: {string.Join(", ", activeOutboundIds)}");

                // Throw so service/controller can present message to caller
                throw new InvalidOperationException($"Cannot disable warehouse. {string.Join("; ", messages)}");
            }

            // Mark warehouse as inactive/disabled (use "Inactive" status to be consistent with other code)
            warehouse.Status = "Inactive";
            warehouse.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _context.Warehouses.Update(warehouse);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }
        public async Task<Warehouse?> GetWarehouseStructureWithoutBinAsync(int warehouseId)
        {
            if (warehouseId <= 0) return null;

            var warehouse = await _context.Warehouses
                .AsNoTracking()
                .Where(w => w.Id == warehouseId)
                .Include(w => w.StorageZones)
                    .ThenInclude(z => z.Shelves)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return warehouse;
        }

        // Returns all ShelfLevels for the shelf along with their ShelfLevelBins (levels + bins)
        public async Task<List<ShelfLevel>> GetLevelsAndBinsByShelfIdAsync(int shelfId)
        {
            if (shelfId <= 0) return new List<ShelfLevel>();

            var levels = await _context.ShelfLevels
                .AsNoTracking()
                .Where(l => l.ShelfId == shelfId)
                .Include(l => l.Shelf)
                    .ThenInclude(s => s.Zone)
                .Include(l => l.ShelfLevelBins)
                .ToListAsync()
                .ConfigureAwait(false);

            return levels;
        }
    }
}
