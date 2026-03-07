using Storix_BE.Domain.Models;

namespace Storix_BE.Service.Interfaces
{
    public interface ISubscriptionService
    {
        /// <summary>Tạo subscription TRIAL 7 ngày. Gọi ngay sau khi tạo Company mới.</summary>
        Task<Subscription> CreateTrialAsync(int companyId);

        /// <summary>
        /// Lấy subscription ACTIVE hiện tại của company.
        /// Trả về null nếu không có subscription ACTIVE nào.
        /// </summary>
        Task<Subscription?> GetActiveSubscriptionAsync(int companyId);

        /// <summary>
        /// Activate subscription trả phí sau khi IPN webhook xác nhận thanh toán thành công.
        /// Tạo mới Subscription ACTIVE với plan_type tương ứng.
        /// </summary>
        Task<Subscription> ActivateSubscriptionAsync(int companyId, string planType);

        /// <summary>Gọi từ background job: expire tất cả subscriptions quá hạn.</summary>
        Task ExpireSubscriptionsAsync();

        /// <summary>Lấy lịch sử subscription của company.</summary>
        Task<List<Subscription>> GetSubscriptionHistoryAsync(int companyId);
    }

    public static class SubscriptionPlanType
    {
        public const string Trial = "TRIAL";
        public const string ProMonthly = "PRO_MONTHLY";
        public const string ProYearly = "PRO_YEARLY";

        private static readonly HashSet<string> PaidPlans = new(StringComparer.OrdinalIgnoreCase)
        {
            ProMonthly,
            ProYearly
        };

        public static bool IsPaid(string planType) => PaidPlans.Contains(planType);

        public static bool IsValid(string planType) =>
            string.Equals(planType, Trial, StringComparison.OrdinalIgnoreCase) ||
            PaidPlans.Contains(planType);

        public static TimeSpan GetDuration(string planType) => planType.ToUpperInvariant() switch
        {
            ProMonthly => TimeSpan.FromDays(30),
            ProYearly => TimeSpan.FromDays(365),
            Trial => TimeSpan.FromDays(7),
            _ => throw new ArgumentException($"Unknown plan type: {planType}")
        };
    }

    public static class SubscriptionExceptionCodes
    {
        public const string SubscriptionExpired = "SUB-EX-01";
        public const string SubscriptionNotFound = "SUB-EX-02";
        public const string InvalidPlanType = "SUB-EX-03";
        public const string CompanyNotFound = "SUB-EX-04";
        public const string TrialAlreadyExists = "SUB-EX-05";
    }

    public sealed record SubscriptionDto(
        int Id,
        int CompanyId,
        string PlanType,
        string Status,
        DateTime StartDate,
        DateTime EndDate,
        DateTime CreatedAt,
        DateTime? UpdatedAt,
        bool IsExpired
    );

    /// <summary>
    /// Response tối giản cho banner UI trên frontend.
    /// Frontend chỉ cần hiển thị đúng thông tin này, không cần tự tính toán.
    /// </summary>
    public sealed record SubscriptionBannerDto(
        /// <summary>Có hiển thị banner không</summary>
        bool ShowBanner,
        /// <summary>TRIAL | PRO_MONTHLY | PRO_YEARLY | NONE</summary>
        string PlanType,
        /// <summary>ACTIVE | EXPIRED | NO_SUBSCRIPTION</summary>
        string Status,
        /// <summary>Số ngày còn lại (0 nếu đã hết hạn)</summary>
        int RemainingDays,
        /// <summary>Số giờ còn lại trong ngày cuối (để hiển thị chính xác hơn)</summary>
        int RemainingHours,
        /// <summary>Thời điểm hết hạn (ISO 8601)</summary>
        DateTime? EndDate,
        /// <summary>Message hiển thị trực tiếp trên banner</summary>
        string Message
    );
}
