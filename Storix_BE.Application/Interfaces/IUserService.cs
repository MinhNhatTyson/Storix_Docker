using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IUserService
    {
        Task<User> Login(string email, string password);
        Task<User> LoginWithGoogleAsync(ClaimsPrincipal? claimsPrincipal);
        Task<User> RegisterCompanyAsync(
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
        Task<List<User>> GetUsersByCompanyAsync(int companyId, int callerRoleId);
        Task<User> CreateUserAsync(int companyId, int callerRoleId, CreateUserRequest request);
        Task<User?> UpdateUserAsync(int userId, int companyId, int callerRoleId, UpdateUserRequest request);
        Task<bool> DeleteUserAsync(int userId, int companyId, int callerRoleId, int callerUserId);
    }

    public sealed record CreateUserRequest(string FullName, string Email, string? Phone, string Password, string RoleName);
    public sealed record UpdateUserRequest(string? FullName, string? Email, string? Phone, string? Password, string? RoleName, string? Status);
}

