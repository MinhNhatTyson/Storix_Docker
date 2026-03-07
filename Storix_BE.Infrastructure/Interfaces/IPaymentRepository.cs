using Storix_BE.Domain.Models;

namespace Storix_BE.Repository.Interfaces
{
    public interface IPaymentRepository
    {
        Task<Company?> GetCompanyByIdAsync(int companyId);
        Task<CompanyPayment?> GetByIdAsync(int paymentId);
        Task<CompanyPayment?> GetLatestByCompanyAsync(int companyId);
        Task<CompanyPayment?> GetLatestPendingByCompanyAsync(int companyId);
        Task<CompanyPayment?> GetSuccessfulByCompanyAsync(int companyId);
        Task<CompanyPayment?> GetByIdempotencyKeyAsync(string idempotencyKey);
        Task<List<CompanyPayment>> GetByCompanyAsync(int companyId);
        Task<CompanyPayment> CreateAsync(CompanyPayment payment);
        Task<CompanyPayment> UpdateAsync(CompanyPayment payment);
    }
}
