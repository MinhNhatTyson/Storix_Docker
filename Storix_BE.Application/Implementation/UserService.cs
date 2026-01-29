using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _accRepository;
        public UserService(IUserRepository accRepository)
        {
            _accRepository = accRepository;
        }
        public async Task<User> Login(string email, string password)
        {
            return await _accRepository.Login(email, password);
        }

        public async Task<User> LoginWithGoogleAsync(ClaimsPrincipal? claimsPrincipal)
        {
            return await _accRepository.LoginWithGoogleAsync(claimsPrincipal);
        }

        public async Task<User> SignupNewAccount(
            string fullName,
            string email,
            string phoneNumber,
            string password,
            string address,
            string companyCode)
        {
            return await _accRepository.SignupNewAccount(
                fullName,
                email,
                phoneNumber,
                password,
                address,
                companyCode);
        }
        public async Task<User> RegisterCompanyAsync(
            string companyName,
            string? businessCode,
            string? address,
            string? contactEmail,
            string? contactPhone,
            string adminFullName,
            string adminEmail,
            string? adminPhone,
            string password)
        {
            return await _accRepository.RegisterCompanyAdministratorAsync(
                companyName,
                businessCode,
                address,
                contactEmail,
                contactPhone,
                adminFullName,
                adminEmail,
                adminPhone,
                password);
        }

        private async Task EnsureCompanyAdministratorAsync(int callerRoleId)
        {
            var role = await _accRepository.GetRoleByIdAsync(callerRoleId);
            if (role?.Name != "Company Administrator")
                throw new UnauthorizedAccessException("Only Company Administrator can manage accounts.");
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _accRepository.GetByEmailAsync(email);
        }

        public async Task<List<User>> GetUsersByCompanyAsync(int companyId, int callerRoleId)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);
            return await _accRepository.GetUsersByCompanyIdAsync(companyId);
        }

        public async Task<User> CreateUserAsync(int companyId, int callerRoleId, CreateUserRequest request)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);
            if (request.RoleName != "Manager" && request.RoleName != "Staff")
                throw new InvalidOperationException("Only Manager or Staff role can be assigned.");
            return await _accRepository.CreateUserAsync(
                companyId,
                request.FullName,
                request.Email,
                request.Phone,
                request.Password,
                request.RoleName);
        }

        public async Task<User?> UpdateUserAsync(int userId, int companyId, int callerRoleId, UpdateUserRequest request)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);
            var user = await _accRepository.GetUserByIdWithRoleAsync(userId);
            if (user == null || user.CompanyId != companyId)
            {
                if (user == null)
                    return null;
                throw new InvalidOperationException("User not in your company.");
            }
            var currentRole = await _accRepository.GetRoleByIdAsync(user.RoleId ?? 0);
            if (currentRole?.Name == "Super Admin")
                throw new InvalidOperationException("Cannot edit Super Admin.");
            if (currentRole?.Name == "Company Administrator")
                throw new InvalidOperationException("Cannot edit Company Administrator.");
            if (request.FullName != null) user.FullName = request.FullName;
            if (request.Email != null) user.Email = request.Email;
            if (request.Phone != null) user.Phone = request.Phone;
            if (request.Status != null) user.Status = request.Status;
            if (request.Password != null) user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            if (request.RoleName != null)
            {
                if (request.RoleName != "Manager" && request.RoleName != "Staff")
                    throw new InvalidOperationException("Only Manager or Staff role can be assigned.");
                var newRole = await _accRepository.GetRoleByNameAsync(request.RoleName);
                if (newRole != null)
                    user.RoleId = newRole.Id;
            }
            user.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _accRepository.UpdateAsync(user);
            return user;
        }

        public async Task<bool> DeleteUserAsync(int userId, int companyId, int callerRoleId, int callerUserId)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);
            var user = await _accRepository.GetUserByIdWithRoleAsync(userId);
            if (user == null)
                return false;
            if (user.CompanyId != companyId)
                throw new InvalidOperationException("User not in your company.");
            if (user.Id == callerUserId)
                throw new InvalidOperationException("You cannot delete your own account.");
            var role = await _accRepository.GetRoleByIdAsync(user.RoleId ?? 0);
            if (role?.Name == "Super Admin")
                throw new InvalidOperationException("Cannot delete Super Admin.");
            if (role?.Name == "Company Administrator")
                throw new InvalidOperationException("Cannot delete Company Administrator.");
            await _accRepository.RemoveAsync(user);
            return true;
        }
    }
}
