using Microsoft.Extensions.Configuration;
using Storix_BE.Domain.Exception;
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
        private readonly IConfiguration _configuration;

        public WarehouseAssignmentService(IUserRepository userRepository, IWarehouseAssignmentRepository assignmentRepository, IConfiguration configuration)
        {
            _userRepository = userRepository;
            _assignmentRepository = assignmentRepository;
            _configuration = configuration;
        }

        private async Task EnsureCompanyAdministratorAsync(int callerRoleId)
        {
            var role = await _userRepository.GetRoleByIdAsync(callerRoleId);
            if (role?.Name != "Company Administrator")
                throw new UnauthorizedAccessException("Only Company Administrator can assign warehouses.");
        }

        private static bool IsInactiveStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            return status.Equals("inactive", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("deactivated", StringComparison.OrdinalIgnoreCase);
        }

        private int? GetMaxUsersPerWarehouse()
        {
            var value = _configuration.GetValue<int?>("Policy:MaxUsersPerWarehouse");
            return value.HasValue && value.Value > 0 ? value.Value : null;
        }
        public async Task<List<Warehouse>> GetWarehousesByCompanyAsync(int companyId, int callerRoleId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            EnsureCompanyAdministratorAsync(callerRoleId);
            return await _assignmentRepository.GetWarehousesByCompanyIdAsync(companyId);
        }
        public async Task<List<WarehouseAssignment>> GetAssignmentsByCompanyAsync(int companyId, int callerRoleId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            await EnsureCompanyAdministratorAsync(callerRoleId);
            return await _assignmentRepository.GetAssignmentsByCompanyIdAsync(companyId);
        }

        public async Task<List<WarehouseAssignment>> GetAssignmentsByWarehouseAsync(int companyId, int callerRoleId, int warehouseId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            await EnsureCompanyAdministratorAsync(callerRoleId);

            var warehouse = await _assignmentRepository.GetWarehouseByIdAsync(warehouseId);
            if (warehouse == null)
                throw new BusinessRuleException("BR-WH-01", "Warehouse not found.");
            if (warehouse.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

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
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            await EnsureCompanyAdministratorAsync(callerRoleId);

            var user = await _userRepository.GetUserByIdWithRoleAsync(request.UserId);
            if (user == null)
                throw new InvalidOperationException("User not found.");
            if (user.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

            var userRole = await _userRepository.GetRoleByIdAsync(user.RoleId ?? 0);
            if (userRole?.Name == "Super Admin")
                throw new InvalidOperationException("Cannot assign warehouse to Super Admin.");
            if (userRole?.Name == "Company Administrator")
                throw new InvalidOperationException("Cannot assign warehouse to Company Administrator.");
            if (userRole?.Name != "Manager" && userRole?.Name != "Staff")
                throw new BusinessRuleException("BR-WH-03", "Role not eligible for warehouse assignment.");

            var warehouse = await _assignmentRepository.GetWarehouseByIdAsync(request.WarehouseId);
            if (warehouse == null)
                throw new BusinessRuleException("BR-WH-01", "Warehouse not found.");
            if (warehouse.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");
            if (IsInactiveStatus(warehouse.Status))
                throw new BusinessRuleException("BR-WH-02", "Warehouse is inactive.");

            var existing = await _assignmentRepository.GetAssignmentAsync(request.UserId, request.WarehouseId);
            if (existing != null)
                throw new BusinessRuleException("BR-WH-04", "User already assigned to this warehouse.");

            var maxUsers = GetMaxUsersPerWarehouse();
            if (maxUsers.HasValue)
            {
                var currentCount = await _assignmentRepository.CountAssignmentsByWarehouseIdAsync(request.WarehouseId);
                if (currentCount >= maxUsers.Value)
                    throw new BusinessRuleException("BR-WH-05", "Warehouse capacity policy exceeded.");
            }

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
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            await EnsureCompanyAdministratorAsync(callerRoleId);

            var assignment = await _assignmentRepository.GetAssignmentAsync(userId, warehouseId);
            if (assignment == null)
                return false;

            if (assignment.Warehouse?.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

            if (assignment.User?.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

            var hasActiveTasks = await _assignmentRepository.HasActiveWarehouseOperationsAsync(userId, warehouseId);
            if (hasActiveTasks)
                throw new BusinessRuleException("BR-WH-06", "User has active tasks in warehouse.");

            var userRole = await _userRepository.GetRoleByIdAsync(assignment.User?.RoleId ?? 0);
            if (userRole?.Name == "Manager")
            {
                var assignmentCount = await _assignmentRepository.CountAssignmentsByUserIdAsync(userId);
                if (assignmentCount <= 1)
                    throw new BusinessRuleException("BR-WH-07", "Manager must have at least one warehouse.");
            }

            await _assignmentRepository.RemoveAssignmentAsync(assignment);
            return true;
        }

        public async Task<int> UpdateRoleInAssignmentsAsync(int userId, string roleInWarehouse)
        {
            if (userId <= 0) throw new InvalidOperationException("Invalid user id.");
            if (string.IsNullOrWhiteSpace(roleInWarehouse))
                throw new InvalidOperationException("Role in warehouse is required.");

            return await _assignmentRepository.UpdateRoleInAssignmentsAsync(userId, roleInWarehouse);
        }
    }
}
