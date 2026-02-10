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

            return await _context.StockCountsTickets.AnyAsync(t =>
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
    }
}
