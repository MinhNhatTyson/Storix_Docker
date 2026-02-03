using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface ISupplierService
    {
        Task<List<Supplier>> GetByCompanyAsync(int companyId);
        Task<Supplier?> GetByIdAsync(int id, int companyId);
        Task<Supplier> CreateAsync(CreateSupplierRequest request);
        Task<Supplier?> UpdateAsync(int id, UpdateSupplierRequest request);
        Task<bool> DeleteAsync(int id, int companyId);

        // helper to resolve company id from a user id (used by controllers that accept userId)
        Task<int> GetCompanyIdByUserIdAsync(int userId);
    }

    public sealed record CreateSupplierRequest(
        int CompanyId,
        string Name,
        string? ContactPerson,
        string? Email,
        string? Phone,
        string? Address);

    public sealed record UpdateSupplierRequest(
        int CompanyId,
        string? Name,
        string? ContactPerson,
        string? Email,
        string? Phone,
        string? Address,
        string? Status);
}


