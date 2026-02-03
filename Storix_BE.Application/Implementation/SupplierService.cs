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
    public class SupplierService : ISupplierService
    {
        private readonly ISupplierRepository _repo;

        public SupplierService(ISupplierRepository repo)
        {
            _repo = repo;
        }

        public async Task<List<Supplier>> GetByCompanyAsync(int companyId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            return await _repo.GetAllSuppliersAsync(companyId);
        }

        public async Task<Supplier?> GetByIdAsync(int id, int companyId)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid supplier id.");
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            return await _repo.GetByIdAsync(id, companyId);
        }

        public async Task<Supplier> CreateAsync(CreateSupplierRequest request)
        {
            if (request == null) throw new InvalidOperationException("Request cannot be null.");
            if (request.CompanyId <= 0) throw new InvalidOperationException("CompanyId must be a positive integer.");
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Supplier name is required.");

            // Basic email validation (optional)
            if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.Contains("@"))
                throw new InvalidOperationException("Supplier email is invalid.");

            var supplier = new Supplier
            {
                CompanyId = request.CompanyId,
                Name = name,
                ContactPerson = request.ContactPerson,
                Email = request.Email,
                Phone = request.Phone,
                Address = request.Address,
                Status = "Active"
            };

            return await _repo.CreateAsync(supplier);
        }

        public async Task<Supplier?> UpdateAsync(int id, UpdateSupplierRequest request)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid supplier id.");
            if (request == null) throw new InvalidOperationException("Request cannot be null.");
            if (request.CompanyId <= 0) throw new InvalidOperationException("CompanyId must be a positive integer.");

            var existing = await _repo.GetByIdAsync(id, request.CompanyId);
            if (existing == null) return null;

            // Prepare update model (partial patch semantics preserved)
            var toUpdate = new Supplier
            {
                Id = id,
                CompanyId = request.CompanyId,
                Name = request.Name ?? existing.Name,
                ContactPerson = request.ContactPerson ?? existing.ContactPerson,
                Email = request.Email ?? existing.Email,
                Phone = request.Phone ?? existing.Phone,
                Address = request.Address ?? existing.Address,
                Status = request.Status ?? existing.Status
            };

            return await _repo.UpdateAsync(toUpdate);
        }

        public async Task<bool> DeleteAsync(int id, int companyId)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid supplier id.");
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");

            var existing = await _repo.GetByIdAsync(id, companyId);
            if (existing == null) return false;

            return await _repo.DeleteAsync(id, companyId);
        }

        public async Task<int> GetCompanyIdByUserIdAsync(int userId)
        {
            if (userId <= 0) throw new InvalidOperationException("Invalid user id.");
            var companyId = await _repo.GetCompanyIdByUserIdAsync(userId);
            if (companyId == null || companyId <= 0)
                throw new InvalidOperationException("User not found or not assigned to a company.");
            return companyId.Value;
        }
    }
}
