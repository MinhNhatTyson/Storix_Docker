using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IWarehouseAssignmentService
    {
        Task<List<WarehouseAssignment>> GetAssignmentsByCompanyAsync(int companyId, int callerRoleId);
        Task<List<WarehouseAssignment>> GetAssignmentsByWarehouseAsync(int companyId, int callerRoleId, int warehouseId);
        Task<WarehouseAssignment> AssignWarehouseAsync(int companyId, int callerRoleId, AssignWarehouseRequest request);
        Task<bool> UnassignWarehouseAsync(int companyId, int callerRoleId, int userId, int warehouseId);
    }

    public sealed record AssignWarehouseRequest(int UserId, int WarehouseId, string? RoleInWarehouse);
}
