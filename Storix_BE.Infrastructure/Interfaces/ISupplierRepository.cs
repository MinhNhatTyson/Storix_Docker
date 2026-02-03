using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface ISupplierRepository
    {
        Task<List<Supplier>> GetAllSuppliersAsync(int companyId);
        Task<Supplier?> GetByIdAsync(int id, int companyId);
        Task<Supplier> CreateAsync(Supplier supplier);
        Task<Supplier?> UpdateAsync(Supplier supplier);
        Task<bool> DeleteAsync(int id, int companyId);
        Task<int?> GetCompanyIdByUserIdAsync(int userId);
    }
}
