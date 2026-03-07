using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Domain.Exception;
using Storix_BE.Service.Interfaces;
using System.Security.Claims;

namespace Storix_BE.API.Controllers
{
#if false // tạm tắt toàn bộ Subscriptions do deploy DB chưa có bảng subscriptions
    [ApiController]
    [Route("api/subscriptions")]
    [Authorize]
    public class SubscriptionsController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;

        public SubscriptionsController(ISubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        /// <summary>Lấy subscription ACTIVE hiện tại của company.</summary>
        [HttpGet("current")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetCurrentSubscription()
        {
            var companyId = GetCompanyIdFromToken();
            if (!companyId.HasValue)
                return Unauthorized(new { message = "Thiếu CompanyId trong token." });

            try
            {
                var subscription = await _subscriptionService.GetActiveSubscriptionAsync(companyId.Value);
                if (subscription == null)
                {
                    return Ok(new
                    {
                        companyId = companyId.Value,
                        status = "NO_SUBSCRIPTION",
                        message = "Không có subscription nào đang hoạt động. Vui lòng đăng ký gói dịch vụ."
                    });
                }

                return Ok(ToDto(subscription));
            }
            catch (BusinessRuleException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
        }

        /// <summary>
        /// Endpoint dành riêng cho banner UI trên frontend.
        /// Trả về số ngày còn lại, message, và flag hiển thị banner — không cần frontend tự tính.
        /// </summary>
        [HttpGet("banner")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetSubscriptionBanner()
        {
            var companyId = GetCompanyIdFromToken();
            if (!companyId.HasValue)
                return Unauthorized(new { message = "Thiếu CompanyId trong token." });

            try
            {
                var subscription = await _subscriptionService.GetActiveSubscriptionAsync(companyId.Value);
                return Ok(ToBannerDto(subscription));
            }
            catch (BusinessRuleException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
        }

        /// <summary>Lấy lịch sử tất cả subscriptions của company.</summary>
        [HttpGet("history")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> GetSubscriptionHistory()
        {
            var companyId = GetCompanyIdFromToken();
            if (!companyId.HasValue)
                return Unauthorized(new { message = "Thiếu CompanyId trong token." });

            try
            {
                var history = await _subscriptionService.GetSubscriptionHistoryAsync(companyId.Value);
                return Ok(history.Select(ToDto));
            }
            catch (BusinessRuleException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
        }

        private static SubscriptionDto ToDto(Domain.Models.Subscription s) =>
            new(
                s.Id,
                s.CompanyId,
                s.PlanType,
                s.Status,
                s.StartDate,
                s.EndDate,
                s.CreatedAt,
                s.UpdatedAt,
                s.Status == "EXPIRED" || s.EndDate < DateTime.UtcNow
            );

        private static SubscriptionBannerDto ToBannerDto(Domain.Models.Subscription? subscription)
        {
            if (subscription == null)
            {
                return new SubscriptionBannerDto(
                    ShowBanner: true,
                    PlanType: "NONE",
                    Status: "NO_SUBSCRIPTION",
                    RemainingDays: 0,
                    RemainingHours: 0,
                    EndDate: null,
                    Message: "Bạn chưa có gói dịch vụ. Kích hoạt gói để sử dụng đầy đủ tính năng."
                );
            }

            var now = DateTime.UtcNow;
            // EndDate được lưu dạng Unspecified nên treat như UTC
            var endDateUtc = DateTime.SpecifyKind(subscription.EndDate, DateTimeKind.Utc);
            var remaining = endDateUtc - now;

            var isExpired = remaining.TotalSeconds <= 0;
            var remainingDays = isExpired ? 0 : (int)Math.Ceiling(remaining.TotalDays);
            var remainingHours = isExpired ? 0 : (int)remaining.TotalHours % 24;

            if (isExpired)
            {
                return new SubscriptionBannerDto(
                    ShowBanner: true,
                    PlanType: subscription.PlanType,
                    Status: "EXPIRED",
                    RemainingDays: 0,
                    RemainingHours: 0,
                    EndDate: subscription.EndDate,
                    Message: "Gói dịch vụ của bạn đã hết hạn. Vui lòng gia hạn để tiếp tục sử dụng."
                );
            }

            var isTrial = string.Equals(subscription.PlanType, "TRIAL", StringComparison.OrdinalIgnoreCase);
            string message;

            if (isTrial)
            {
                message = remainingDays == 1
                    ? $"Bạn còn {remainingDays} ngày dùng thử miễn phí, kích hoạt gói để tiếp tục sử dụng dịch vụ."
                    : $"Bạn còn {remainingDays} ngày dùng thử miễn phí, kích hoạt gói để tiếp tục sử dụng dịch vụ.";
            }
            else
            {
                var planLabel = subscription.PlanType == "PRO_MONTHLY" ? "PRO tháng" : "PRO năm";
                message = $"Gói {planLabel} còn hiệu lực {remainingDays} ngày.";
            }

            // Chỉ hiện banner khi là trial hoặc còn < 7 ngày với paid plan
            var showBanner = isTrial || remainingDays <= 7;

            return new SubscriptionBannerDto(
                ShowBanner: showBanner,
                PlanType: subscription.PlanType,
                Status: "ACTIVE",
                RemainingDays: remainingDays,
                RemainingHours: remainingHours,
                EndDate: subscription.EndDate,
                Message: message
            );
        }

        private int? GetCompanyIdFromToken()
        {
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            if (string.IsNullOrWhiteSpace(companyIdStr))
                companyIdStr = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return int.TryParse(companyIdStr, out var companyId) ? companyId : null;
        }
    }
#endif
}
