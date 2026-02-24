using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Asn1.Ocsp;
using Storix_BE.Domain.Exception;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class UserService : IUserService
    {
        private readonly IEmailService _emailService;
        private readonly IUserRepository _accRepository;
        private readonly IWarehouseAssignmentService _assignmentService;
        private readonly IConfiguration _configuration;
        private readonly IImageService _imageService;
        public UserService(IUserRepository accRepository, IEmailService emailService, IWarehouseAssignmentService assignmentService, IConfiguration configuration, IImageService imageService)
        {
            _accRepository = accRepository;
            _emailService = emailService;
            _assignmentService = assignmentService;
            _configuration = configuration;
            _imageService = imageService;
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
            var user = await _accRepository.SignupNewAccount(fullName, email, phoneNumber, password, address, companyCode);
            await _emailService.SendEmailAsync(email,
                "Storix - New account confirmation",
                "<h1>Thank you!</h1><p>you have successfuly registered a new Storix account with the following detail: </p>"
            );
            return user;
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

        private static bool IsInactiveStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            return status.Equals("inactive", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("deactivated", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("locked", StringComparison.OrdinalIgnoreCase);
        }

        private int? GetMaxUsersPerCompany()
        {
            var value = _configuration.GetValue<int?>("Policy:MaxUsersPerCompany");
            return value.HasValue && value.Value > 0 ? value.Value : null;
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _accRepository.GetByEmailAsync(email);
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            return await _accRepository.GetUserByIdWithRoleAsync(userId);
        }

        public async Task<List<User>> GetUsersForCallerAsync(int callerUserId, int callerRoleId)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);
            var caller = await _accRepository.GetUserByIdWithRoleAsync(callerUserId);
            if (caller?.CompanyId == null)
                throw new InvalidOperationException("Caller is not assigned to a company.");
            return await _accRepository.GetUsersByCompanyIdAsync(caller.CompanyId.Value);
        }

        public async Task<User> CreateUserAsync(int callerUserId, int callerRoleId, CreateUserRequest request)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);
            var caller = await _accRepository.GetUserByIdWithRoleAsync(callerUserId);
            if (caller?.CompanyId == null)
                throw new InvalidOperationException("Caller is not assigned to a company.");
            var companyId = caller.CompanyId.Value;
            if (request.RoleName != "Manager" && request.RoleName != "Staff")
                throw new BusinessRuleException("BR-ACC-02", "Invalid role assignment.");

            var company = await _accRepository.GetCompanyByIdAsync(companyId);
            if (company == null || IsInactiveStatus(company.Status))
                throw new BusinessRuleException("BR-ACC-04", "Company is inactive.");

            var existing = await _accRepository.GetByEmailAsync(request.Email);
            if (existing != null)
                throw new BusinessRuleException("BR-ACC-01", "Duplicate email.");

            var maxUsers = GetMaxUsersPerCompany();
            if (maxUsers.HasValue)
            {
                var currentCount = await _accRepository.CountUsersByCompanyAsync(companyId);
                if (currentCount >= maxUsers.Value)
                    throw new BusinessRuleException("BR-ACC-03", "Company user limit reached.");
            }

            return await _accRepository.CreateUserAsync(
                companyId,
                request.FullName,
                request.Email,
                request.Phone,
                request.Password,
                request.RoleName);
        }

        public async Task<User?> UpdateUserAsync(int userId, int callerUserId, int callerRoleId, UpdateUserRequest request)
        {
            EnsureCompanyAdministratorAsync(callerRoleId);
            var caller = await _accRepository.GetUserByIdWithRoleAsync(callerUserId);
            if (caller?.CompanyId == null)
                throw new InvalidOperationException("Caller is not assigned to a company.");
            var companyId = caller.CompanyId.Value;
            var user = await _accRepository.GetUserByIdWithRoleAsync(userId);
            if (user == null || user.CompanyId != companyId)
            {
                if (user == null)
                    return null;
                throw new InvalidOperationException("User not in your company.");
            }

            var company = await _accRepository.GetCompanyByIdAsync(companyId);
            if (company == null || IsInactiveStatus(company.Status))
                throw new BusinessRuleException("BR-ACC-04", "Company is inactive.");

            if (user.Status != null && user.Status.Equals("Locked", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("BR-ACC-08", "Account is locked.");

            var currentRole = await _accRepository.GetRoleByIdAsync(user.RoleId ?? 0);
            if (currentRole?.Name == "Super Admin")
                throw new InvalidOperationException("Cannot edit Super Admin.");
            if (currentRole?.Name == "Company Administrator")
                throw new InvalidOperationException("Cannot edit Company Administrator.");
            if (request.Status != null) user.Status = request.Status;
            if (request.RoleName != null)
            {
                if (userId == callerUserId)
                    throw new BusinessRuleException("BR-ACC-06", "Self role modification is not allowed.");

                if (request.RoleName != "Manager" && request.RoleName != "Staff")
                    throw new BusinessRuleException("BR-ACC-02", "Invalid role assignment.");

                var hasActiveOps = await _accRepository.HasActiveOperationsAsync(userId);
                if (hasActiveOps)
                    throw new BusinessRuleException("BR-ACC-07", "Role change blocked due to active tasks.");

                var newRole = await _accRepository.GetRoleByNameAsync(request.RoleName);
                if (newRole != null)
                    user.RoleId = newRole.Id;

                await _assignmentService.UpdateRoleInAssignmentsAsync(userId, request.RoleName);
            }
            user.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _accRepository.UpdateAsync(user);
            return user;
        }

        public async Task<bool> DeleteUserAsync(int userId, int callerUserId, int callerRoleId)
        {
            await EnsureCompanyAdministratorAsync(callerRoleId);
            var caller = await _accRepository.GetUserByIdWithRoleAsync(callerUserId);
            if (caller?.CompanyId == null)
                throw new InvalidOperationException("Caller is not assigned to a company.");
            var companyId = caller.CompanyId.Value;
            var user = await _accRepository.GetUserByIdWithRoleAsync(userId);
            if (user == null)
                return false;
            if (user.CompanyId != companyId)
                throw new InvalidOperationException("User not in your company.");

            var company = await _accRepository.GetCompanyByIdAsync(companyId);
            if (company == null || IsInactiveStatus(company.Status))
                throw new BusinessRuleException("BR-ACC-04", "Company is inactive.");

            if (user.Id == callerUserId)
                throw new InvalidOperationException("You cannot delete your own account.");
            var role = await _accRepository.GetRoleByIdAsync(user.RoleId ?? 0);
            if (role?.Name == "Super Admin")
                throw new InvalidOperationException("Cannot delete Super Admin.");

            if (role?.Name == "Company Administrator")
            {
                var adminCount = await _accRepository.CountCompanyAdminsAsync(companyId);
                if (adminCount <= 1)
                    throw new BusinessRuleException("BR-ACC-09", "Cannot delete the last Company Administrator.");
            }

            var hasActiveOps = await _accRepository.HasActiveOperationsAsync(userId);
            if (hasActiveOps)
                throw new BusinessRuleException("BR-ACC-10", "User has active warehouse operations.");

            await _accRepository.RemoveAsync(user);
            return true;
        }

        public async Task<User> UpdateProfileAsync(int userId, UpdateProfileDto dto)
        {
            string? imageUrl = null;

            if (dto.Avatar != null)
            {
                imageUrl = await _imageService.UploadProductImageAsync(dto.Avatar);
            }

            var user = new User
            {
                Id = userId,
                CompanyId = dto.CompanyId,
                FullName = dto.FullName,
                Email = dto.Email,
                Phone = dto.Phone,
                PasswordHash = dto.Password,
                Avatar = imageUrl
            };
            return await _accRepository.UpdateProfileAsync(user);
        }

        public async Task<UserProfileDto?> GetUser(int userId)
        {
            if (userId <= 0) throw new InvalidOperationException("Invalid user id.");
            var user = await _accRepository.GetUserByIdWithRoleAsync(userId);
            if (user == null) return null;

            return new UserProfileDto(
                user.Id,
                user.CompanyId,
                user.FullName,
                user.Email,
                user.Phone,
                user.Role?.Name,
                user.Status,
                user.Avatar);
        }

        public async Task<LoginResponse> AuthenticateAsync(string email, string password)
        {
            var user = await _accRepository.Login(email, password);
            if (user == null) throw new UnauthorizedAccessException("Invalid email or password.");

            var accessToken = GenerateAccessToken(user);
            var refreshTokenStr = GenerateRefreshTokenString();
            var refreshDays = _configuration.GetValue<int?>("Jwt:RefreshTokenExpiryDays") ?? 7;
            var expiresAt = DateTime.UtcNow.AddDays(refreshDays);

            await _accRepository.CreateRefreshTokenAsync(user.Id, refreshTokenStr, expiresAt);

            return new LoginResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenStr,
                UserId = user.Id,
                RoleId = user.RoleId,
                CompanyId = user.CompanyId
            };
        }

        public async Task<TokenResponse> RefreshTokenAsync(string refreshToken)
        {
            var existing = await _accRepository.GetRefreshTokenAsync(refreshToken);
            if (existing == null || existing.IsRevoked == true || (existing.ExpiredAt != null && existing.ExpiredAt.Value.ToUniversalTime() < DateTime.UtcNow))
            {
                throw new InvalidOperationException("Refresh token invalid or expired.");
            }

            var user = await _accRepository.GetUserByIdWithRoleAsync(existing.UserId ?? 0);
            if (user == null) throw new InvalidOperationException("User not found for refresh token.");

            // revoke old refresh token (rotation)
            await _accRepository.RevokeRefreshTokenAsync(refreshToken);

            var newRefreshToken = GenerateRefreshTokenString();
            var refreshDays = _configuration.GetValue<int?>("Jwt:RefreshTokenExpiryDays") ?? 7;
            var expiresAt = DateTime.UtcNow.AddDays(refreshDays);
            await _accRepository.CreateRefreshTokenAsync(user.Id, newRefreshToken, expiresAt);

            var newAccessToken = GenerateAccessToken(user);

            return new TokenResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            };
        }

        public async Task LogoutAsync(string refreshToken)
        {
            var existing = await _accRepository.GetRefreshTokenAsync(refreshToken);
            if (existing == null) return;
            await _accRepository.RevokeRefreshTokenAsync(refreshToken);
        }

        private string GenerateAccessToken(User user)
        {
            var key = _configuration.GetValue<string>("Jwt:Key") ?? _configuration.GetValue<string>("SECRET_KEY");
            var issuer = _configuration.GetValue<string>("Jwt:Issuer") ?? _configuration.GetValue<string>("SECRET_ISSUER");
            var audience = _configuration.GetValue<string>("Jwt:Audience") ?? _configuration.GetValue<string>("SECRET_AUDIENCE");

            var expiryMinutes = _configuration.GetValue<int?>("Jwt:AccessTokenExpiryMinutes") ?? 120;

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.Email, user.Email ?? string.Empty),
                new(ClaimTypes.Role, user.RoleId?.ToString() ?? string.Empty),
                new("CompanyId", user.CompanyId?.ToString() ?? string.Empty)
            };

            var token = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string GenerateRefreshTokenString()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
        public async Task<List<User>> GetUsersByWarehouseAsync(int warehouseId)
        {
            var users = await _accRepository.GetUsersByWarehouseIdAsync(warehouseId);

            var result = new List<User>();
            foreach (var u in users)
            {
                var role = await _accRepository.GetRoleByIdAsync(u.RoleId ?? 0);
                if (role?.Name == "Manager" || role?.Name == "Staff")
                    result.Add(u);
            }

            return result;
        }
        public async Task<List<User>> GetStaffsByCompanyIdAsync(int companyId)
        {
            var users = await _accRepository.GetStaffsByCompanyIdAsync(companyId);
            return users;
        }
    }
}
