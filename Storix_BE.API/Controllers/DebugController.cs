using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storix_BE.Domain.Context;
using Storix_BE.Service.Configuration;
using Storix_BE.Service.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly StorixDbContext _context;
        private readonly IPaymentService? _paymentService;
        private readonly MomoGatewayOptions? _momoOptions;
        private readonly IWebHostEnvironment _env;

        public DebugController(
            StorixDbContext context,
            IWebHostEnvironment env,
            IPaymentService? paymentService = null,
            IOptions<MomoGatewayOptions>? momoOptions = null)
        {
            _context = context;
            _env = env;
            _paymentService = paymentService;
            _momoOptions = momoOptions?.Value;
        }

        /// <summary>
        /// Kiểm tra kết nối tới database.
        /// </summary>
        [HttpGet("db-health")]
        public async Task<IActionResult> DbHealth()
        {
            var canConnect = await _context.Database.CanConnectAsync();
            return Ok(new { canConnect });
        }

        /// <summary>
        /// Sửa lại sequence cho cột id của bảng users để tránh lỗi duplicate key (users_pkey).
        /// Chỉ cần gọi 1 lần khi khởi tạo dữ liệu mẫu.
        /// </summary>
        [HttpPost("fix-users-sequence")]
        public async Task<IActionResult> FixUsersSequence()
        {
            const string sql = @"
DO $$
DECLARE
  seq_name text;
BEGIN
  SELECT pg_get_serial_sequence('public.users', 'id') INTO seq_name;

  IF seq_name IS NOT NULL THEN
    EXECUTE format(
      'SELECT setval(%L, (SELECT COALESCE(MAX(id), 0) + 1 FROM public.users), false);',
      seq_name
    );
  END IF;
END $$;";

            await _context.Database.ExecuteSqlRawAsync(sql);

            return Ok(new { message = "Users id sequence has been realigned to MAX(id)+1." });
        }

        /// <summary>
        /// [DEV ONLY] Simulate MoMo IPN callback cho một payment PENDING.
        /// Dùng khi test local mà không có public URL — tự sinh signature hợp lệ và gọi IPN endpoint.
        /// CHỈ hoạt động khi ASPNETCORE_ENVIRONMENT = Development.
        /// </summary>
        [HttpPost("simulate-momo-ipn/{paymentId:int}")]
        public async Task<IActionResult> SimulateMomoIpn(int paymentId, [FromQuery] int resultCode = 0)
        {
            if (!_env.IsDevelopment())
                return NotFound();

            if (_momoOptions == null || _paymentService == null)
                return StatusCode(500, new { message = "MoMo options hoặc PaymentService chưa được inject." });

            var payment = await _context.CompanyPayments.FindAsync(paymentId);
            if (payment == null)
                return NotFound(new { message = $"Không tìm thấy payment id={paymentId}." });

            if (string.IsNullOrWhiteSpace(payment.IdempotencyKey))
                return BadRequest(new { message = "Payment này chưa có orderId (idempotency_key). Hãy gọi /momo/url trước." });

            var orderId = payment.IdempotencyKey;
            var requestId = Guid.NewGuid().ToString("N");
            var amount = ((long)Math.Round(payment.Amount, 0)).ToString();
            var orderInfo = $"Simulate IPN for payment {paymentId}";
            var orderType = "momo_wallet";
            var transId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var message = resultCode == 0 ? "Successful." : "Failed.";
            var payType = "napas";
            var responseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var extraData = "";

            var rawSig =
                $"accessKey={_momoOptions.AccessKey}" +
                $"&amount={amount}" +
                $"&extraData={extraData}" +
                $"&message={message}" +
                $"&orderId={orderId}" +
                $"&orderInfo={orderInfo}" +
                $"&orderType={orderType}" +
                $"&partnerCode={_momoOptions.PartnerCode}" +
                $"&payType={payType}" +
                $"&requestId={requestId}" +
                $"&responseTime={responseTime}" +
                $"&resultCode={resultCode}" +
                $"&transId={transId}";

            var keyBytes = Encoding.UTF8.GetBytes(_momoOptions.SecretKey);
            var rawBytes = Encoding.UTF8.GetBytes(rawSig);
            using var hmac = new HMACSHA256(keyBytes);
            var signature = Convert.ToHexString(hmac.ComputeHash(rawBytes)).ToLowerInvariant();

            var callbackRequest = new MomoAtmCallbackRequest
            {
                PartnerCode = _momoOptions.PartnerCode,
                OrderId = orderId,
                RequestId = requestId,
                Amount = amount,
                OrderInfo = orderInfo,
                OrderType = orderType,
                TransId = transId,
                ResultCode = resultCode.ToString(),
                Message = message,
                PayType = payType,
                ResponseTime = responseTime,
                ExtraData = extraData,
                Signature = signature
            };

            var result = await _paymentService.ProcessMomoAtmCallbackAsync(callbackRequest, true);

            return Ok(new
            {
                message = "IPN simulated successfully.",
                paymentId = result.PaymentId,
                companyId = result.CompanyId,
                paymentStatus = result.PaymentStatus,
                isUnlocked = result.IsUnlocked,
                providerResultCode = result.ProviderResultCode
            });
        }

    }
}

