using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;

namespace Storix_BE.Repository.Implementation
{
    public class SubscriptionRepository : ISubscriptionRepository
    {
        private readonly StorixDbContext _context;

        public SubscriptionRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<Subscription?> GetActiveByCompanyAsync(int companyId)
        {
            return await _context.Subscriptions
                .AsNoTracking()
                .Where(s => s.CompanyId == companyId && s.Status == "ACTIVE")
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<Subscription>> GetHistoryByCompanyAsync(int companyId)
        {
            return await _context.Subscriptions
                .AsNoTracking()
                .Where(s => s.CompanyId == companyId)
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<List<Subscription>> GetExpiredActiveSubscriptionsAsync(DateTime now)
        {
            var nowUnspecified = DateTime.SpecifyKind(now, DateTimeKind.Unspecified);
            return await _context.Subscriptions
                .Where(s => s.Status == "ACTIVE" && s.EndDate < nowUnspecified)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<Subscription> CreateAsync(Subscription subscription)
        {
            _context.Subscriptions.Add(subscription);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return subscription;
        }

        public async Task<Subscription> UpdateAsync(Subscription subscription)
        {
            _context.Subscriptions.Update(subscription);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return subscription;
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
