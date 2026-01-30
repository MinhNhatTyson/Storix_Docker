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

        public WarehouseAssignmentRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<Warehouse?> GetWarehouseByIdAsync(int warehouseId)
        {
            return await _context.Warehouses.FindAsync(warehouseId);
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
