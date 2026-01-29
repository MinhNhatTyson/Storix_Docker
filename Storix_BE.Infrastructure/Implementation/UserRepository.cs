using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Exception;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class UserRepository : GenericRepository<User>, IUserRepository
    {
        private readonly StorixDbContext _context;
        public UserRepository(StorixDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<User> Login(string email, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email);

            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
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
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<List<User>> GetUsersByCompanyIdAsync(int companyId)
        {
            return await _context.Users
                .Include(u => u.Role)
                .Where(u => u.CompanyId == companyId)
                .OrderBy(u => u.Id)
                .ToListAsync();
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
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<Role?> GetRoleByNameAsync(string name)
        {
            return await _context.Roles.FirstOrDefaultAsync(r => r.Name == name);

        }
    }
}
