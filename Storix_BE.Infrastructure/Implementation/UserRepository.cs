using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Exception;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace Storix_BE.Repository.Implementation
{
    public class UserRepository : GenericRepository<User>, IUserRepository
    {
        private readonly StorixDbContext _context;
        private static readonly string[] InactiveStatuses = { "completed", "cancelled", "canceled", "done", "closed" };
        public UserRepository(StorixDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<User> Login(string email, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email);

            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                if (user.Status != "Active")
                {
                    throw new Exception("This account has been banned");
                }
                return user;
            }
            return null;
        }

        public async Task<User> LoginWithGoogleAsync(ClaimsPrincipal? claimsPrincipal)
        {
            if (claimsPrincipal == null)
            {
                throw new ExternalLoginProviderException("Google", "Claims principal is null");
            }
            var email = claimsPrincipal?.FindFirst(ClaimTypes.Email)?.Value;
            if (email == null)
            {
                throw new ExternalLoginProviderException("Google", "Email is null");
            }

            var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email);

            if (user == null)
            {
                return null;
                /*var newUser = new User
                {
                    Email = email,
                    FullName = claimsPrincipal?.FindFirst(ClaimTypes.GivenName)?.Value ?? String.Empty,
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("123")
                };*/
                //Add user sau
            }

            return user;
        }

        public async Task<User> SignupNewAccount(string fullName, string email, string phoneNumber, string password, string address, string companyCode)
        {
            if (await _context.Users.AnyAsync(x => x.Email == email))
            {
                throw new Exception("Email already exists.");
            }
            if (await _context.Companies.AnyAsync(c => c.BusinessCode == companyCode) == false)
            {
                throw new Exception("Company code is invalid.");
            }
            var company = await _context.Companies.FirstOrDefaultAsync(c => c.BusinessCode == companyCode);
            var newUser = new User();
            var newId = await GenerateUniqueRandomIdAsync();
            if (company != null)
            {
                var passwordHash = HashPassword(password);
                newUser = new User
                {
                    Id = newId,
                    FullName = fullName,
                    Email = email,
                    Phone = phoneNumber,
                    PasswordHash = passwordHash,
                    Status = "Active",
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    CompanyId = company.Id,
                    Company = company,
                    RoleId = 2 //Mac dinh la Company admin
                };
            }
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return newUser;

            // Trường hợp muốn trả về field theo yêu cầu
            //var newlyCreatedUser = await this.GetAccountById(User.UserId);
            //var userDTO = new UserDTO
            //{
            //    UserID = newlyCreatedUser.UserId,
            //};
            //return userDTO;
        }

        public async Task<User> RegisterCompanyAdministratorAsync(
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
            // Ensure email is unique
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (existingUser != null)
            {
                throw new InvalidOperationException("Email is already registered.");
            }

            // Find Company Administrator role by name
            var companyAdminRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.Name == "Company Administrator");

            if (companyAdminRole == null)
            {
                throw new InvalidOperationException("Role 'Company Administrator' not found. Please seed this role in the database.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var company = new Company
                {
                    Name = companyName,
                    BusinessCode = businessCode,
                    Address = address,
                    ContactEmail = contactEmail,
                    ContactPhone = contactPhone,
                    SubscriptionPlan = null,
                    Status = "Active",
                    // Npgsql + timestamp without time zone yêu cầu DateTimeKind.Unspecified
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    UpdatedAt = null
                };

                _context.Companies.Add(company);
                await _context.SaveChangesAsync();

                var user = new User
                {
                    CompanyId = company.Id,
                    FullName = adminFullName,
                    Email = adminEmail,
                    Phone = adminPhone,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                    RoleId = companyAdminRole.Id,
                    Status = "Active",
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    UpdatedAt = null
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return user;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _context.Users
                .Include(u => u.Role)
                .Include(u => u.WarehouseAssignments)
                    .ThenInclude(a => a.Warehouse)
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<List<User>> GetUsersByCompanyIdAsync(int companyId)
        {
            return await _context.Users
                .Include(u => u.Role)
                .Include(u => u.WarehouseAssignments)
                    .ThenInclude(a => a.Warehouse)
                .Where(u => u.CompanyId == companyId)
                .OrderBy(u => u.Id)
                .ToListAsync();
        }

        public async Task<Company?> GetCompanyByIdAsync(int companyId)
        {
            return await _context.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
        }

        public async Task<int> CountUsersByCompanyAsync(int companyId)
        {
            return await _context.Users.CountAsync(u => u.CompanyId == companyId);
        }

        public async Task<int> CountCompanyAdminsAsync(int companyId)
        {
            return await _context.Users
                .Include(u => u.Role)
                .CountAsync(u => u.CompanyId == companyId && u.Role != null && u.Role.Name == "Company Administrator");
        }

        public async Task<bool> HasActiveOperationsAsync(int userId)
        {
            var hasInboundOrders = await _context.InboundOrders.AnyAsync(o =>
                o.CreatedBy == userId &&
                (o.Status == null || !InactiveStatuses.Contains(o.Status.ToLower())));
            if (hasInboundOrders) return true;

            var hasOutboundOrders = await _context.OutboundOrders.AnyAsync(o =>
                (o.CreatedBy == userId || o.StaffId == userId) &&
                (o.Status == null || !InactiveStatuses.Contains(o.Status.ToLower())));
            if (hasOutboundOrders) return true;

            var hasTransferOrders = await _context.TransferOrders.AnyAsync(o =>
                o.CreatedBy == userId &&
                (o.Status == null || !InactiveStatuses.Contains(o.Status.ToLower())));
            if (hasTransferOrders) return true;

            var hasInboundRequests = await _context.InboundRequests.AnyAsync(r =>
                (r.RequestedBy == userId || r.ApprovedBy == userId) &&
                (r.Status == null || !InactiveStatuses.Contains(r.Status.ToLower())));
            if (hasInboundRequests) return true;

            var hasOutboundRequests = await _context.OutboundRequests.AnyAsync(r =>
                (r.RequestedBy == userId || r.ApprovedBy == userId) &&
                (r.Status == null || !InactiveStatuses.Contains(r.Status.ToLower())));
            if (hasOutboundRequests) return true;

            return await _context.StockCountsTickets.AnyAsync(t =>
                (t.AssignedTo == userId || t.PerformedBy == userId) &&
                (t.Status == null || !InactiveStatuses.Contains(t.Status.ToLower())));
        }

        public async Task<User> CreateUserAsync(int companyId, string fullName, string email, string? phone, string password, string roleName)
        {
            if (roleName != "Manager" && roleName != "Staff")
                throw new InvalidOperationException("Only Manager or Staff role can be assigned. Company Administrator cannot be created via this endpoint.");

            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (existingUser != null)
                throw new InvalidOperationException("Email is already registered.");

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role == null)
                throw new InvalidOperationException($"Role '{roleName}' not found. Please seed roles in the database.");

            var user = new User
            {
                CompanyId = companyId,
                FullName = fullName,
                Email = email,
                Phone = phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                RoleId = role.Id,
                Status = "Active",
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                UpdatedAt = null
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<Role?> GetRoleByIdAsync(int roleId)
        {
            return await _context.Roles.FindAsync(roleId);
        }

        public async Task<User?> GetUserByIdWithRoleAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.Role)
                .Include(u => u.WarehouseAssignments)
                    .ThenInclude(a => a.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<Role?> GetRoleByNameAsync(string name)
        {
            return await _context.Roles.FirstOrDefaultAsync(r => r.Name == name);
        }
        private string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }
        private async Task<int> GenerateUniqueRandomIdAsync()
        {
            const int min = 1000; // small but non-trivial
            const int max = 9999;
            const int maxAttempts = 200;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int candidate = Random.Shared.Next(min, max + 1);
                bool exists = await _context.Users.AnyAsync(u => u.Id == candidate);
                if (!exists)
                    return candidate;
            }

            throw new InvalidOperationException($"Unable to generate a unique user id after {maxAttempts} attempts.");
        }

        private async Task<int> GenerateUniqueRandomRefreshTokenIdAsync()
        {
            const int min = 1000000;
            const int max = int.MaxValue - 1;
            const int maxAttempts = 200;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int candidate = Random.Shared.Next(min, max);
                bool exists = await _context.RefreshTokens.AnyAsync(r => r.Id == candidate);
                if (!exists) return candidate;
            }
            throw new InvalidOperationException($"Unable to generate a unique refresh token id after {maxAttempts} attempts.");
        }

        public async Task<RefreshToken> CreateRefreshTokenAsync(int userId, string token, DateTime expiresAt)
        {
            var id = await GenerateUniqueRandomRefreshTokenIdAsync();
            var rt = new RefreshToken
            {
                Id = id,
                UserId = userId,
                Token = token,
                ExpiredAt = DateTime.SpecifyKind(expiresAt.ToUniversalTime(), DateTimeKind.Unspecified),
                IsRevoked = false,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };
            _context.RefreshTokens.Add(rt);
            await _context.SaveChangesAsync();
            return rt;
        }

        public async Task<RefreshToken?> GetRefreshTokenAsync(string token)
        {
            return await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == token);
        }

        public async Task RevokeRefreshTokenAsync(string token)
        {
            var rt = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == token);
            if (rt == null) return;
            rt.IsRevoked = true;
            await _context.SaveChangesAsync();
        }

        public async Task RevokeAllRefreshTokensForUserAsync(int userId)
        {
            var tokens = await _context.RefreshTokens.Where(r => r.UserId == userId && (r.IsRevoked == false || r.IsRevoked == null)).ToListAsync();
            if (tokens.Count == 0) return;
            foreach (var t in tokens) t.IsRevoked = true;
            await _context.SaveChangesAsync();
        }

        public async Task<User> UpdateProfileAsync(User user)
        {

            var existedUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == user.Id);

            if (existedUser == null)
            {
                throw new InvalidOperationException("User not found.");
            }

            if (await _context.Users.AnyAsync(u => u.Email == user.Email && u.Id != user.Id))
            {
                throw new Exception("Email already exists.");
            }

            existedUser.CompanyId = user.CompanyId;
            existedUser.FullName = user.FullName;
            existedUser.Email = user.Email;
            existedUser.Phone = user.Phone;
            if (!string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                existedUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
            }
            existedUser.Avatar = user.Avatar;
            existedUser.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _context.Users.Update(existedUser);
            await _context.SaveChangesAsync();

            return existedUser;
        }

    }
}
