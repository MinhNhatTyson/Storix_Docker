using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Domain.Exception;
using Storix_BE.Service.Interfaces;
using System.Security.Claims;

namespace Storix_BE.API.Controllers
{
#if false // tạm tắt toàn bộ Payments do deploy DB chưa có đủ bảng payment/subscription
    [ApiController]
    [Route("api/payments")]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;

        public PaymentsController(IPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        /// <summary>Tạo payment record PENDING cho một gói subscription.</summary>
        [HttpPost]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "Request không được null." });

            var callerCompanyId = GetCompanyIdFromToken();
            if (!callerCompanyId.HasValue)
                return Unauthorized(new { message = "Thiếu CompanyId trong token." });

            try
            {
                var payment = await _paymentService.CreatePaymentAsync(request, callerCompanyId.Value);
                return Ok(payment);
            }
            catch (BusinessRuleException ex)
            {
                return MapPaymentException(ex);
            }
        }

        /// <summary>Lấy trạng thái payment mới nhất của company.</summary>
        [HttpGet("status")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetPaymentStatus([FromQuery(Name = "company_id")] int companyId)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "company_id phải là số nguyên dương." });

            var callerCompanyId = GetCompanyIdFromToken();
            if (!callerCompanyId.HasValue)
                return Unauthorized(new { message = "Thiếu CompanyId trong token." });

            try
            {
                var status = await _paymentService.GetPaymentStatusAsync(companyId, callerCompanyId.Value);
                return Ok(status);
            }
            catch (BusinessRuleException ex)
            {
                return MapPaymentException(ex);
            }
        }

        /// <summary>Lấy MoMo ATM payment URL để redirect user đến trang thanh toán.</summary>
        [HttpPost("{id:int}/momo/url")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> CreateMomoAtmPaymentUrl(int id, [FromBody] CreateMomoAtmUrlRequest? request)
        {
            if (id <= 0)
                return BadRequest(new { message = "Payment id không hợp lệ." });

            var callerCompanyId = GetCompanyIdFromToken();
            if (!callerCompanyId.HasValue)
                return Unauthorized(new { message = "Thiếu CompanyId trong token." });

            try
            {
                var result = await _paymentService.CreateMomoAtmPaymentUrlAsync(id, request?.OrderInfo, callerCompanyId.Value);
                return Ok(result);
            }
            catch (BusinessRuleException ex)
            {
                return MapPaymentException(ex);
            }
        }

        /// <summary>
        /// MoMo return URL - CHỈ dùng để redirect user về frontend sau khi thanh toán.
        /// KHÔNG cập nhật database. Trạng thái thanh toán thực sự được xử lý bởi IPN endpoint.
        /// </summary>
        [HttpGet("momo/return")]
        [AllowAnonymous]
        public IActionResult HandleMomoAtmReturn([FromQuery] MomoAtmCallbackRequest request)
        {
            // Không xử lý logic ở đây - chỉ redirect về frontend
            return Ok(new
            {
                message = "Cảm ơn bạn đã thanh toán. Vui lòng chờ xác nhận từ hệ thống.",
                orderId = request.OrderId,
                resultCode = request.ResultCode
            });
        }

        /// <summary>
        /// MoMo IPN webhook endpoint.
        /// Đây là nguồn sự thật duy nhất để cập nhật trạng thái thanh toán.
        /// AllowAnonymous vì MoMo server gọi trực tiếp. Signature được validate bên trong.
        /// </summary>
        [HttpPost("momo/ipn")]
        [AllowAnonymous]
        public async Task<IActionResult> HandleMomoAtmIpn([FromBody] MomoAtmCallbackRequest request)
        {
            try
            {
                var result = await _paymentService.ProcessMomoAtmCallbackAsync(request, true);
                return Ok(new { resultCode = 0, message = "Success", paymentStatus = result.PaymentStatus });
            }
            catch (BusinessRuleException ex)
            {
                return Ok(new { resultCode = 1, message = ex.Message, code = ex.Code });
            }
        }

        private IActionResult MapPaymentException(BusinessRuleException ex)
        {
            var payload = new { code = ex.Code, message = ex.Message };
            return ex.Code switch
            {
                PaymentExceptionCodes.PaymentRequired => StatusCode(StatusCodes.Status402PaymentRequired, payload),
                PaymentExceptionCodes.ViewOnlyAccess => StatusCode(StatusCodes.Status402PaymentRequired, payload),
                PaymentExceptionCodes.PaymentPending => StatusCode(StatusCodes.Status402PaymentRequired, payload),
                PaymentExceptionCodes.PaymentFailed => StatusCode(StatusCodes.Status402PaymentRequired, payload),
                PaymentExceptionCodes.DuplicateSuccessPayment => Conflict(payload),
                PaymentExceptionCodes.CompanyNotFound => NotFound(payload),
                PaymentExceptionCodes.CompanyInactive => Conflict(payload),
                PaymentExceptionCodes.PaymentProviderError => BadRequest(payload),
                PaymentExceptionCodes.InvalidPaymentStatus => BadRequest(payload),
                PaymentExceptionCodes.InvalidPaymentUpdate => Conflict(payload),
                PaymentExceptionCodes.CrossCompanyAccessDenied => StatusCode(StatusCodes.Status403Forbidden, payload),
                PaymentExceptionCodes.PaymentCallbackMismatch => BadRequest(payload),
                PaymentExceptionCodes.PaymentCheckFailed => StatusCode(StatusCodes.Status503ServiceUnavailable, payload),
                _ => BadRequest(payload)
            };
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
