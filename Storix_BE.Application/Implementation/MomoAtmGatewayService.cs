using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storix_BE.Domain.Exception;
using Storix_BE.Service.Configuration;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.Service.Implementation
{
    public class MomoAtmGatewayService : IMomoAtmGatewayService
    {
        private readonly HttpClient _httpClient;
        private readonly MomoGatewayOptions _momoOptions;
        private readonly PaymentRuntimeOptions _paymentOptions;
        private readonly ILogger<MomoAtmGatewayService> _logger;

        public MomoAtmGatewayService(
            HttpClient httpClient,
            IOptions<MomoGatewayOptions> momoOptions,
            IOptions<PaymentRuntimeOptions> paymentOptions,
            ILogger<MomoAtmGatewayService> logger)
        {
            _httpClient = httpClient;
            _momoOptions = momoOptions.Value;
            _paymentOptions = paymentOptions.Value;
            _logger = logger;
        }

        public async Task<MomoAtmGatewayCreateResult> CreatePaymentUrlAsync(MomoAtmGatewayCreateRequest request)
        {
            ValidateOptions();

            var orderId = $"PAY-{request.PaymentId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var requestId = Guid.NewGuid().ToString("N");
            var amount = Convert.ToInt64(decimal.Round(request.Amount, 0, MidpointRounding.AwayFromZero)).ToString();
            var requestType = "payWithATM";
            var extraData = string.Empty;
            var redirectUrl = CombineUrl(_paymentOptions.BaseUrl, _paymentOptions.ReturnUrl);
            var ipnUrl = CombineUrl(_paymentOptions.BaseUrl, _paymentOptions.NotifyUrl);

            var rawSignature =
                $"accessKey={_momoOptions.AccessKey}" +
                $"&amount={amount}" +
                $"&extraData={extraData}" +
                $"&ipnUrl={ipnUrl}" +
                $"&orderId={orderId}" +
                $"&orderInfo={request.OrderInfo}" +
                $"&partnerCode={_momoOptions.PartnerCode}" +
                $"&redirectUrl={redirectUrl}" +
                $"&requestId={requestId}" +
                $"&requestType={requestType}";

            var signature = HmacSha256(rawSignature, _momoOptions.SecretKey);
            var payload = new MomoCreatePaymentPayload(
                _momoOptions.PartnerCode,
                _momoOptions.AccessKey,
                requestId,
                amount,
                orderId,
                request.OrderInfo,
                redirectUrl,
                ipnUrl,
                extraData,
                requestType,
                signature,
                "vi"
            );

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync(_momoOptions.PaymentUrl, payload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PAY-EX-10 MoMo provider call failed");
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, "Cannot connect to MoMo provider.");
            }

            MomoCreatePaymentResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<MomoCreatePaymentResponse>().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PAY-EX-10 MoMo provider response parse failed");
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, "MoMo provider response is invalid.");
            }

            if (!response.IsSuccessStatusCode || body == null)
            {
                _logger.LogError("PAY-EX-10 MoMo provider returned HTTP {StatusCode}", (int)response.StatusCode);
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, "MoMo provider returned an invalid response.");
            }

            if (body.ResultCode != 0 || string.IsNullOrWhiteSpace(body.PayUrl))
            {
                _logger.LogWarning("PAY-EX-10 MoMo provider rejected request: {ResultCode} - {Message}", body.ResultCode, body.Message);
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, body.Message ?? "MoMo rejected payment creation.");
            }

            return new MomoAtmGatewayCreateResult(
                body.OrderId ?? orderId,
                body.RequestId ?? requestId,
                body.PayUrl!,
                body.ResultCode,
                body.Message ?? "Success"
            );
        }

        public bool ValidateCallbackSignature(MomoAtmCallbackRequest request)
        {
            ValidateOptions();

            if (string.IsNullOrWhiteSpace(request.Signature))
            {
                return false;
            }

            var rawSignature =
                $"accessKey={_momoOptions.AccessKey}" +
                $"&amount={request.Amount ?? string.Empty}" +
                $"&extraData={request.ExtraData ?? string.Empty}" +
                $"&message={request.Message ?? string.Empty}" +
                $"&orderId={request.OrderId ?? string.Empty}" +
                $"&orderInfo={request.OrderInfo ?? string.Empty}" +
                $"&orderType={request.OrderType ?? string.Empty}" +
                $"&partnerCode={request.PartnerCode ?? string.Empty}" +
                $"&payType={request.PayType ?? string.Empty}" +
                $"&requestId={request.RequestId ?? string.Empty}" +
                $"&responseTime={request.ResponseTime ?? string.Empty}" +
                $"&resultCode={request.ResultCode ?? string.Empty}" +
                $"&transId={request.TransId ?? string.Empty}";

            var expectedSignature = HmacSha256(rawSignature, _momoOptions.SecretKey);
            var signatureMatch = string.Equals(expectedSignature, request.Signature, StringComparison.OrdinalIgnoreCase);
            return signatureMatch;
        }

        private void ValidateOptions()
        {
            if (string.IsNullOrWhiteSpace(_momoOptions.PartnerCode) ||
                string.IsNullOrWhiteSpace(_momoOptions.AccessKey) ||
                string.IsNullOrWhiteSpace(_momoOptions.SecretKey) ||
                string.IsNullOrWhiteSpace(_momoOptions.PaymentUrl) ||
                string.IsNullOrWhiteSpace(_paymentOptions.BaseUrl) ||
                string.IsNullOrWhiteSpace(_paymentOptions.ReturnUrl) ||
                string.IsNullOrWhiteSpace(_paymentOptions.NotifyUrl))
            {
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, "MoMo ATM configuration is missing.");
            }
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            var normalizedBase = baseUrl.TrimEnd('/');
            var normalizedPath = path.StartsWith('/') ? path : "/" + path;
            return normalizedBase + normalizedPath;
        }

        private static string HmacSha256(string rawData, string secretKey)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var rawBytes = Encoding.UTF8.GetBytes(rawData);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(rawBytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private sealed record MomoCreatePaymentPayload(
            [property: JsonPropertyName("partnerCode")] string PartnerCode,
            [property: JsonPropertyName("accessKey")] string AccessKey,
            [property: JsonPropertyName("requestId")] string RequestId,
            [property: JsonPropertyName("amount")] string Amount,
            [property: JsonPropertyName("orderId")] string OrderId,
            [property: JsonPropertyName("orderInfo")] string OrderInfo,
            [property: JsonPropertyName("redirectUrl")] string RedirectUrl,
            [property: JsonPropertyName("ipnUrl")] string IpnUrl,
            [property: JsonPropertyName("extraData")] string ExtraData,
            [property: JsonPropertyName("requestType")] string RequestType,
            [property: JsonPropertyName("signature")] string Signature,
            [property: JsonPropertyName("lang")] string Lang
        );

        private sealed record MomoCreatePaymentResponse
        {
            [JsonPropertyName("resultCode")]
            public int ResultCode { get; init; }

            [JsonPropertyName("message")]
            public string? Message { get; init; }

            [JsonPropertyName("payUrl")]
            public string? PayUrl { get; init; }

            [JsonPropertyName("orderId")]
            public string? OrderId { get; init; }

            [JsonPropertyName("requestId")]
            public string? RequestId { get; init; }
        }
    }
}
