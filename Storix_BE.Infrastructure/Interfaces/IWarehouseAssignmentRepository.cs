using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IWarehouseAssignmentRepository
    {
        Task<Warehouse?> GetWarehouseByIdAsync(int warehouseId);
        Task<List<Warehouse>> GetWarehousesByCompanyIdAsync(int companyId);
        Task<WarehouseAssignment?> GetAssignmentAsync(int userId, int warehouseId);
        Task<List<WarehouseAssignment>> GetAssignmentsByCompanyIdAsync(int companyId);
        Task<List<WarehouseAssignment>> GetAssignmentsByWarehouseIdAsync(int warehouseId);
        Task<int> CountAssignmentsByWarehouseIdAsync(int warehouseId);
        Task<int> CountAssignmentsByUserIdAsync(int userId);
        Task<bool> HasActiveWarehouseOperationsAsync(int userId, int warehouseId);
        Task<int> UpdateRoleInAssignmentsAsync(int userId, string roleInWarehouse);
        Task<int> AddAssignmentAsync(WarehouseAssignment assignment);
        Task<bool> RemoveAssignmentAsync(WarehouseAssignment assignment);
        Task<Warehouse> CreateWarehouseAsync(Warehouse warehouse);
    }
}
