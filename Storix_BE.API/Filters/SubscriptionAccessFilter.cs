using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Storix_BE.Domain.Models;
using Storix_BE.Service.Interfaces;
using System.Security.Claims;

namespace Storix_BE.API.Filters
{
    /// <summary>
    /// Filter kiểm tra subscription ACTIVE trước khi cho phép request.
    /// Trả về 402 Payment Required nếu subscription đã hết hạn hoặc không tồn tại.
    /// Áp dụng lên controller bằng [ServiceFilter(typeof(SubscriptionAccessFilter))].
    /// </summary>
    public class SubscriptionAccessFilter : IAsyncResourceFilter
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<SubscriptionAccessFilter> _logger;

        public SubscriptionAccessFilter(
            ISubscriptionService subscriptionService,
            ILogger<SubscriptionAccessFilter> logger)
        {
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        public async Task OnResourceExecutionAsync(
            ResourceExecutingContext context,
            ResourceExecutionDelegate next)
        {
            var companyId = ExtractCompanyId(context.HttpContext);

            // Bỏ qua nếu không có CompanyId (ví dụ: anonymous endpoints)
            if (!companyId.HasValue)
            {
                await next();
                return;
            }

            Subscription? subscription = null;
            try
            {
                subscription = await _subscriptionService.GetActiveSubscriptionAsync(companyId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SUB-FILTER-ERR Lỗi khi kiểm tra subscription cho companyId={CompanyId}", companyId.Value);
                // Fail-safe: block access nếu không thể kiểm tra subscription
                context.Result = new ObjectResult(new
                {
                    code = "SUBSCRIPTION_CHECK_FAILED",
                    message = "Không thể xác minh subscription. Vui lòng thử lại."
                })
                { StatusCode = StatusCodes.Status503ServiceUnavailable };
                return;
            }

            if (subscription == null)
            {
                context.Result = new ObjectResult(new
                {
                    code = "NO_SUBSCRIPTION",
                    message = "Bạn chưa có gói dịch vụ. Vui lòng đăng ký để sử dụng tính năng này."
                })
                { StatusCode = StatusCodes.Status402PaymentRequired };
                return;
            }

            // Double-check thời gian hết hạn (subscription có thể ACTIVE nhưng EndDate đã qua trước khi job chạy)
            if (subscription.EndDate < DateTime.UtcNow)
            {
                _logger.LogInformation(
                    "SUB-FILTER-01 Subscription id={SubId} của company {CompanyId} đã quá hạn lúc {EndDate}",
                    subscription.Id, companyId.Value, subscription.EndDate);

                context.Result = new ObjectResult(new
                {
                    code = "SUBSCRIPTION_EXPIRED",
                    message = "Subscription của bạn đã hết hạn. Vui lòng gia hạn để tiếp tục sử dụng.",
                    expiredAt = subscription.EndDate,
                    planType = subscription.PlanType
                })
                { StatusCode = StatusCodes.Status402PaymentRequired };
                return;
            }

            await next();
        }

        private static int? ExtractCompanyId(HttpContext httpContext)
        {
            var companyIdStr = httpContext.User.FindFirst("CompanyId")?.Value;
            if (string.IsNullOrWhiteSpace(companyIdStr))
                companyIdStr = httpContext.User.FindFirst(ClaimTypes.GroupSid)?.Value;

            return int.TryParse(companyIdStr, out var id) && id > 0 ? id : null;
        }
    }
}
