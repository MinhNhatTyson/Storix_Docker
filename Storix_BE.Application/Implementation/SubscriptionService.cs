using Microsoft.Extensions.Logging;
using Storix_BE.Domain.Exception;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.Service.Implementation
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ISubscriptionRepository _subscriptionRepo;
        private readonly IPaymentRepository _paymentRepo;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(
            ISubscriptionRepository subscriptionRepo,
            IPaymentRepository paymentRepo,
            ILogger<SubscriptionService> logger)
        {
            _subscriptionRepo = subscriptionRepo;
            _paymentRepo = paymentRepo;
            _logger = logger;
        }

        public async Task<Subscription> CreateTrialAsync(int companyId)
        {
            if (companyId <= 0)
                throw new BusinessRuleException(SubscriptionExceptionCodes.CompanyNotFound, "CompanyId không hợp lệ.");

            var company = await _paymentRepo.GetCompanyByIdAsync(companyId).ConfigureAwait(false);
            if (company == null)
                throw new BusinessRuleException(SubscriptionExceptionCodes.CompanyNotFound, $"Không tìm thấy company {companyId}.");

            var existing = await _subscriptionRepo.GetActiveByCompanyAsync(companyId).ConfigureAwait(false);
            if (existing != null)
            {
                _logger.LogWarning("SUB-WARN-01 Company {CompanyId} đã có subscription ACTIVE (id={SubId}). Bỏ qua tạo trial.", companyId, existing.Id);
                return existing;
            }

            var now = UtcUnspecified(DateTime.UtcNow);
            var trial = new Subscription
            {
                CompanyId = companyId,
                PlanType = SubscriptionPlanType.Trial,
                Status = "ACTIVE",
                StartDate = now,
                EndDate = now.AddDays(7),
                CreatedAt = now,
                UpdatedAt = null
            };

            var created = await _subscriptionRepo.CreateAsync(trial).ConfigureAwait(false);
            _logger.LogInformation("SUB-INFO-01 Tạo TRIAL subscription cho company {CompanyId}, hết hạn {EndDate}", companyId, created.EndDate);
            return created;
        }

        public async Task<Subscription?> GetActiveSubscriptionAsync(int companyId)
        {
            if (companyId <= 0) return null;
            return await _subscriptionRepo.GetActiveByCompanyAsync(companyId).ConfigureAwait(false);
        }

        public async Task<Subscription> ActivateSubscriptionAsync(int companyId, string planType)
        {
            if (companyId <= 0)
                throw new BusinessRuleException(SubscriptionExceptionCodes.CompanyNotFound, "CompanyId không hợp lệ.");

            var normalizedPlan = planType?.Trim().ToUpperInvariant()
                ?? throw new BusinessRuleException(SubscriptionExceptionCodes.InvalidPlanType, "Plan type không được để trống.");

            if (!SubscriptionPlanType.IsPaid(normalizedPlan))
                throw new BusinessRuleException(SubscriptionExceptionCodes.InvalidPlanType, $"Plan type '{planType}' không hợp lệ. Chỉ chấp nhận PRO_MONTHLY hoặc PRO_YEARLY.");

            var company = await _paymentRepo.GetCompanyByIdAsync(companyId).ConfigureAwait(false);
            if (company == null)
                throw new BusinessRuleException(SubscriptionExceptionCodes.CompanyNotFound, $"Không tìm thấy company {companyId}.");

            var now = UtcUnspecified(DateTime.UtcNow);
            var duration = SubscriptionPlanType.GetDuration(normalizedPlan);

            var newSubscription = new Subscription
            {
                CompanyId = companyId,
                PlanType = normalizedPlan,
                Status = "ACTIVE",
                StartDate = now,
                EndDate = now.Add(duration),
                CreatedAt = now,
                UpdatedAt = null
            };

            var created = await _subscriptionRepo.CreateAsync(newSubscription).ConfigureAwait(false);
            _logger.LogInformation(
                "SUB-INFO-02 Activated {PlanType} subscription (id={SubId}) cho company {CompanyId}, hết hạn {EndDate}",
                normalizedPlan, created.Id, companyId, created.EndDate);
            return created;
        }

        public async Task ExpireSubscriptionsAsync()
        {
            var now = DateTime.UtcNow;
            var expired = await _subscriptionRepo.GetExpiredActiveSubscriptionsAsync(now).ConfigureAwait(false);

            if (expired.Count == 0) return;

            foreach (var sub in expired)
            {
                sub.Status = "EXPIRED";
                sub.UpdatedAt = UtcUnspecified(now);
            }

            await _subscriptionRepo.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogInformation("SUB-INFO-03 Expired {Count} subscription(s) tại {Now}", expired.Count, now);
        }

        public async Task<List<Subscription>> GetSubscriptionHistoryAsync(int companyId)
        {
            if (companyId <= 0)
                throw new BusinessRuleException(SubscriptionExceptionCodes.CompanyNotFound, "CompanyId không hợp lệ.");

            return await _subscriptionRepo.GetHistoryByCompanyAsync(companyId).ConfigureAwait(false);
        }

        private static DateTime UtcUnspecified(DateTime utcNow) =>
            DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified);
    }
}
