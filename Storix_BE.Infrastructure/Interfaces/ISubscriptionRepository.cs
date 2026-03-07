using Storix_BE.Domain.Models;

namespace Storix_BE.Repository.Interfaces
{
    public interface ISubscriptionRepository
    {
        /// <summary>Lấy subscription ACTIVE hiện tại của company (bao gồm TRIAL và PRO plans)</summary>
        Task<Subscription?> GetActiveByCompanyAsync(int companyId);

        /// <summary>Lấy toàn bộ lịch sử subscription của company, mới nhất trước</summary>
        Task<List<Subscription>> GetHistoryByCompanyAsync(int companyId);

        /// <summary>Lấy tất cả subscriptions có status ACTIVE và end_date đã qua (cho background job)</summary>
        Task<List<Subscription>> GetExpiredActiveSubscriptionsAsync(DateTime now);

        Task<Subscription> CreateAsync(Subscription subscription);
        Task<Subscription> UpdateAsync(Subscription subscription);
        Task SaveChangesAsync();
    }
}
