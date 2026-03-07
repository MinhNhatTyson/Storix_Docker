using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;

namespace Storix_BE.Repository.Implementation
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly StorixDbContext _context;

        public PaymentRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<Company?> GetCompanyByIdAsync(int companyId)
        {
            return await _context.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId)
                .ConfigureAwait(false);
        }

        public async Task<CompanyPayment?> GetByIdAsync(int paymentId)
        {
            return await _context.CompanyPayments
                .FirstOrDefaultAsync(p => p.Id == paymentId)
                .ConfigureAwait(false);
        }

        public async Task<CompanyPayment?> GetLatestByCompanyAsync(int companyId)
        {
            return await _context.CompanyPayments
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }

        public async Task<CompanyPayment?> GetLatestPendingByCompanyAsync(int companyId)
        {
            return await _context.CompanyPayments
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.PaymentStatus == "PENDING")
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }

        public async Task<CompanyPayment?> GetSuccessfulByCompanyAsync(int companyId)
        {
            return await _context.CompanyPayments
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.PaymentStatus == "SUCCESS")
                .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }

        public async Task<CompanyPayment?> GetByIdempotencyKeyAsync(string idempotencyKey)
        {
            return await _context.CompanyPayments
                .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey)
                .ConfigureAwait(false);
        }

        public async Task<List<CompanyPayment>> GetByCompanyAsync(int companyId)
        {
            return await _context.CompanyPayments
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId)
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<CompanyPayment> CreateAsync(CompanyPayment payment)
        {
            _context.CompanyPayments.Add(payment);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return payment;
        }

        public async Task<CompanyPayment> UpdateAsync(CompanyPayment payment)
        {
            _context.CompanyPayments.Update(payment);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return payment;
        }
    }
}
