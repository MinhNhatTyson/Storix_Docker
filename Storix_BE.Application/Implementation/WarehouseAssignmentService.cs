using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class WarehouseAssignmentService : IWarehouseAssignmentService
    {
        private readonly IUserRepository _userRepository;
        private readonly IWarehouseAssignmentRepository _assignmentRepository;

        public WarehouseAssignmentService(IUserRepository userRepository, IWarehouseAssignmentRepository assignmentRepository)
        {
            _userRepository = userRepository;
            _assignmentRepository = assignmentRepository;
        }

        private async Task EnsureCompanyAdministratorAsync(int callerRoleId)
        {
            var role = await _userRepository.GetRoleByIdAsync(callerRoleId);
            if (role?.Name != "Company Administrator")
                throw new UnauthorizedAccessException("Only Company Administrator can assign warehouses.");
        }

        public async Task<List<WarehouseAssignment>> GetAssignmentsByCompanyAsync(int companyId, int callerRoleId)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);
            return await _assignmentRepository.GetAssignmentsByCompanyIdAsync(companyId);
        }

        public async Task<List<WarehouseAssignment>> GetAssignmentsByWarehouseAsync(int companyId, int callerRoleId, int warehouseId)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);

            var warehouse = await _assignmentRepository.GetWarehouseByIdAsync(warehouseId);
            if (warehouse == null)
                throw new InvalidOperationException("Warehouse not found.");
            if (warehouse.CompanyId != companyId)
                throw new InvalidOperationException("Warehouse not in your company.");

            var assignments = await _assignmentRepository.GetAssignmentsByWarehouseIdAsync(warehouseId);

            // Only return Manager/Staff assignments
            var result = new List<WarehouseAssignment>();
            foreach (var assignment in assignments)
            {
                var role = await _userRepository.GetRoleByIdAsync(assignment.User?.RoleId ?? 0);
                if (role?.Name == "Manager" || role?.Name == "Staff")
                    result.Add(assignment);
            }

            return result;
        }

        public async Task<WarehouseAssignment> AssignWarehouseAsync(int companyId, int callerRoleId, AssignWarehouseRequest request)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);

            var user = await _userRepository.GetUserByIdWithRoleAsync(request.UserId);
            if (user == null)
                throw new InvalidOperationException("User not found.");
            if (user.CompanyId != companyId)
                throw new InvalidOperationException("User not in your company.");

            var userRole = await _userRepository.GetRoleByIdAsync(user.RoleId ?? 0);
            if (userRole?.Name == "Super Admin")
                throw new InvalidOperationException("Cannot assign warehouse to Super Admin.");
            if (userRole?.Name == "Company Administrator")
                throw new InvalidOperationException("Cannot assign warehouse to Company Administrator.");
            if (userRole?.Name != "Manager" && userRole?.Name != "Staff")
                throw new InvalidOperationException("Only Manager or Staff can be assigned to a warehouse.");

            var warehouse = await _assignmentRepository.GetWarehouseByIdAsync(request.WarehouseId);
            if (warehouse == null)
                throw new InvalidOperationException("Warehouse not found.");
            if (warehouse.CompanyId != companyId)
                throw new InvalidOperationException("Warehouse not in your company.");

            var existing = await _assignmentRepository.GetAssignmentAsync(request.UserId, request.WarehouseId);
            if (existing != null)
                throw new InvalidOperationException("Assignment already exists.");

            var assignment = new WarehouseAssignment
            {
                UserId = request.UserId,
                WarehouseId = request.WarehouseId,
                RoleInWarehouse = request.RoleInWarehouse ?? userRole?.Name,
                AssignedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            await _assignmentRepository.AddAssignmentAsync(assignment);
            return assignment;
        }

        public async Task<bool> UnassignWarehouseAsync(int companyId, int callerRoleId, int userId, int warehouseId)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);

            var assignment = await _assignmentRepository.GetAssignmentAsync(userId, warehouseId);
            if (assignment == null)
                return false;

            if (assignment.Warehouse?.CompanyId != companyId)
                throw new InvalidOperationException("Warehouse not in your company.");

            if (assignment.User?.CompanyId != companyId)
                throw new InvalidOperationException("User not in your company.");

            await _assignmentRepository.RemoveAssignmentAsync(assignment);
            return true;
        }
    }
}
