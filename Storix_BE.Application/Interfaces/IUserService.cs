using Microsoft.AspNetCore.Http;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System;
using System.Collections.Generic;
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
        Task<User> SignupNewAccount(string fullName, string email, string phoneNumber, string password, string address, string companyCode);
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
        Task<User?> GetUserByIdAsync(int userId);
        Task<List<User>> GetUsersForCallerAsync(int callerUserId, int callerRoleId);
        Task<User> CreateUserAsync(int callerUserId, int callerRoleId, CreateUserRequest request);
        Task<User?> UpdateUserAsync(int userId, int callerUserId, int callerRoleId, UpdateUserRequest request);
        Task<bool> DeleteUserAsync(int userId, int callerUserId, int callerRoleId);
        Task<User> UpdateProfileAsync(int userId, UpdateProfileDto dto);
        Task<UserProfileDto?> GetUser(int userId);
        Task<LoginResponse> AuthenticateAsync(string email, string password);
        Task<TokenResponse> RefreshTokenAsync(string refreshToken);
        Task LogoutAsync(string refreshToken);
        Task<List<User>> GetUsersByWarehouseAsync(int warehouseId);
        Task<List<User>> GetStaffsByCompanyIdAsync(int companyId);
    }

    public sealed record CreateUserRequest(string FullName, string Email, string? Phone, string Password, string RoleName);
    public sealed record UpdateUserRequest(string? RoleName, string? Status);
    public sealed record UserProfileDto(
        int Id,
        int? CompanyId,
        string? FullName,
        string? Email,
        string? Phone,
        string? RoleName,
        string? Status,
        string? Avatar);
    public sealed record UpdateProfileDto(
        int? CompanyId,
        string? FullName,
        string? Email,
        string? Phone,
        string? Password,
        IFormFile? Avatar);
}

