using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class SupplierRepository : ISupplierRepository
    {
        private readonly StorixDbContext _context;

        public SupplierRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<List<Supplier>> GetAllSuppliersAsync(int companyId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");

            return await _context.Suppliers
                .AsNoTracking()
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Id)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<Supplier?> GetByIdAsync(int id, int companyId)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid supplier id.");
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");

            return await _context.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId)
                .ConfigureAwait(false);
        }

        public async Task<Supplier> CreateAsync(Supplier supplier)
        {
            if (supplier == null) throw new ArgumentNullException(nameof(supplier));
            if (supplier.CompanyId == null || supplier.CompanyId <= 0)
                throw new InvalidOperationException("CompanyId is required.");

            var company = await _context.Companies.FindAsync(supplier.CompanyId.Value).ConfigureAwait(false);
            if (company == null)
                throw new InvalidOperationException($"Company with id {supplier.CompanyId.Value} not found.");

            var name = supplier.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Supplier name is required.");

            // Prevent duplicate supplier name within the same company
            var exists = await _context.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.CompanyId == supplier.CompanyId && s.Name != null && s.Name.ToLower() == name.ToLower())
                .ConfigureAwait(false);

            if (exists != null)
                throw new InvalidOperationException($"Supplier with name '{name}' already exists for this company.");

            supplier.Name = name;
            supplier.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            // Reload detached entity for return (with no tracking)
            return await _context.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == supplier.Id)
                .ConfigureAwait(false) ?? supplier;
        }

        public async Task<Supplier?> UpdateAsync(Supplier supplier)
        {
            if (supplier == null) throw new ArgumentNullException(nameof(supplier));
            if (supplier.Id <= 0) throw new InvalidOperationException("Invalid supplier id.");

            var existing = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == supplier.Id).ConfigureAwait(false);
            if (existing == null)
                throw new InvalidOperationException($"Supplier with id {supplier.Id} not found.");

            // If company change requested, ensure target company exists
            if (supplier.CompanyId.HasValue && supplier.CompanyId.Value != existing.CompanyId)
            {
                var targetCompany = await _context.Companies.FindAsync(supplier.CompanyId.Value).ConfigureAwait(false);
                if (targetCompany == null)
                    throw new InvalidOperationException($"Company with id {supplier.CompanyId.Value} not found.");
                existing.CompanyId = supplier.CompanyId;
            }

            // Validate and prevent name collisions within the same company
            var name = supplier.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                var collision = await _context.Suppliers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.CompanyId == existing.CompanyId && s.Id != existing.Id && s.Name != null && s.Name.ToLower() == name.ToLower())
                    .ConfigureAwait(false);

                if (collision != null)
                    throw new InvalidOperationException($"Another supplier with name '{name}' already exists for this company.");

                existing.Name = name;
            }

            // Patch other fields
            if (supplier.ContactPerson != null) existing.ContactPerson = supplier.ContactPerson;
            if (supplier.Email != null) existing.Email = supplier.Email;
            if (supplier.Phone != null) existing.Phone = supplier.Phone;
            if (supplier.Address != null) existing.Address = supplier.Address;
            if (supplier.Status != null) existing.Status = supplier.Status;

            _context.Suppliers.Update(existing);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            return existing;
        }

        public async Task<bool> DeleteAsync(int id, int companyId)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid supplier id.");
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");

            var supplier = await _context.Suppliers
                .Include(s => s.InboundOrders)
                .Include(s => s.InboundRequests)
                .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId)
                .ConfigureAwait(false);

            if (supplier == null) return false;

            // Prevent deletion if referenced by other entities
            if ((supplier.InboundOrders != null && supplier.InboundOrders.Any()) ||
                (supplier.InboundRequests != null && supplier.InboundRequests.Any()))
            {
                throw new InvalidOperationException("Cannot remove supplier because it is referenced by inbound orders or inbound requests.");
            }

            _context.Suppliers.Remove(supplier);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }

        public async Task<int?> GetCompanyIdByUserIdAsync(int userId)
        {
            if (userId <= 0) return null;
            var companyId = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.CompanyId)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
            return companyId;
        }
    }
}
