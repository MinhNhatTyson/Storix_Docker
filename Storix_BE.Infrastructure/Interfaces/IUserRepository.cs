using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IUserRepository
    {
        Task<User> Login(string email, string password);
        Task<User> LoginWithGoogleAsync(ClaimsPrincipal? claimsPrincipal);
        Task<User> SignupNewAccount(string fullName, string email, string phoneNumber, string password, string address, string companyCode);
        Task<User> RegisterCompanyAdministratorAsync(
            string companyName,
            string? businessCode,
            string? address,
            string? contactEmail,
            string? contactPhone,
            string adminFullName,
            string adminEmail,
            string? adminPhone,
            string password);

        Task<User?> GetByEmailAsync(string email);
        Task<List<User>> GetUsersByCompanyIdAsync(int companyId);
        Task<User> CreateUserAsync(int companyId, string fullName, string email, string? phone, string password, string roleName);
        Task<Role?> GetRoleByIdAsync(int roleId);
        Task<Role?> GetRoleByNameAsync(string name);
        Task<User?> GetUserByIdWithRoleAsync(int userId);
        Task<int> UpdateAsync(User user);
        Task<bool> RemoveAsync(User user);
        Task<User> UpdateProfileAsync(int userId, UpdateProfileDto dto);
    }
}
