using Storix_BE.Domain.Models;
using Storix_BE.Domain.Exception;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Storix_BE.Service.Implementation
{
    public class PaymentService : IPaymentService
    {
        private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "MANUAL",
            "MOMO",
            "VNPAY"
        };

        private readonly IPaymentRepository _paymentRepository;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IMomoAtmGatewayService _momoAtmGatewayService;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            IPaymentRepository paymentRepository,
            ISubscriptionService subscriptionService,
            IMomoAtmGatewayService momoAtmGatewayService,
            ILogger<PaymentService> logger)
        {
            _paymentRepository = paymentRepository;
            _subscriptionService = subscriptionService;
            _momoAtmGatewayService = momoAtmGatewayService;
            _logger = logger;
        }

        public async Task<PaymentDto> CreatePaymentAsync(CreatePaymentRequest request, int callerCompanyId)
        {
            if (request == null)
                throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, "Request không được null.");

            if (request.CompanyId <= 0)
                throw new BusinessRuleException(PaymentExceptionCodes.CompanyNotFound, "CompanyId phải là số nguyên dương.");

            EnsureSameCompany(callerCompanyId, request.CompanyId);

            if (request.Amount <= 0)
                throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, "Amount phải lớn hơn 0.");

            var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);

            var normalizedPlan = request.PlanType?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedPlan) || !SubscriptionPlanType.IsPaid(normalizedPlan))
                throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, "PlanType không hợp lệ. Chỉ chấp nhận PRO_MONTHLY hoặc PRO_YEARLY.");

            var company = await ValidateCompanyAsync(request.CompanyId).ConfigureAwait(false);
            EnsureCompanyIsActive(company);

            // Nếu đã có pending payment cho plan này → trả về idempotent
            var latestPending = await _paymentRepository.GetLatestPendingByCompanyAsync(request.CompanyId).ConfigureAwait(false);
            if (latestPending != null && string.Equals(latestPending.PlanType, normalizedPlan, StringComparison.OrdinalIgnoreCase))
            {
                return ToPaymentDto(latestPending);
            }

            var now = UtcUnspecified(DateTime.UtcNow);
            var payment = new CompanyPayment
            {
                CompanyId = request.CompanyId,
                PaymentStatus = "PENDING",
                Amount = request.Amount,
                PaymentMethod = paymentMethod,
                PlanType = normalizedPlan,
                CreatedAt = now,
                UpdatedAt = now
            };

            var created = await _paymentRepository.CreateAsync(payment).ConfigureAwait(false);
            return ToPaymentDto(created);
        }

        public async Task<PaymentStatusResult> GetPaymentStatusAsync(int companyId, int callerCompanyId)
        {
            if (companyId <= 0)
                throw new BusinessRuleException(PaymentExceptionCodes.CompanyNotFound, "CompanyId phải là số nguyên dương.");

            EnsureSameCompany(callerCompanyId, companyId);
            await ValidateCompanyAsync(companyId).ConfigureAwait(false);

            var successfulPayment = await _paymentRepository.GetSuccessfulByCompanyAsync(companyId).ConfigureAwait(false);
            if (successfulPayment != null)
                return ToStatusResult(companyId, successfulPayment, true, "SUCCESS");

            var latest = await _paymentRepository.GetLatestByCompanyAsync(companyId).ConfigureAwait(false);
            if (latest == null)
                return new PaymentStatusResult(companyId, false, "NOT_PAID", null, null, null, null, null);

            var normalizedStatus = NormalizePaymentStatus(latest.PaymentStatus);
            return ToStatusResult(companyId, latest, false, normalizedStatus);
        }

        public async Task<MomoAtmPaymentUrlResult> CreateMomoAtmPaymentUrlAsync(int paymentId, string? orderInfo, int callerCompanyId)
        {
            if (paymentId <= 0)
                throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, "Payment id phải là số nguyên dương.");

            var payment = await _paymentRepository.GetByIdAsync(paymentId).ConfigureAwait(false);
            if (payment == null)
                throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, $"Không tìm thấy payment id={paymentId}.");

            EnsureSameCompany(callerCompanyId, payment.CompanyId);

            var company = await ValidateCompanyAsync(payment.CompanyId).ConfigureAwait(false);
            EnsureCompanyIsActive(company);

            var method = NormalizePaymentMethod(payment.PaymentMethod);
            if (!string.Equals(method, "MOMO", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, "Payment này không được cấu hình cho MoMo.");

            var status = NormalizePaymentStatus(payment.PaymentStatus);
            if (status == "SUCCESS")
                throw new BusinessRuleException(PaymentExceptionCodes.DuplicateSuccessPayment, "Payment đã hoàn tất thành công.");

            if (status == "FAILED")
                throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, "Payment đã thất bại. Vui lòng tạo payment mới.");

            if (status != "PENDING")
                throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentStatus, $"Payment status '{payment.PaymentStatus}' không hợp lệ.");

            if (payment.Amount < 1000 || payment.Amount > 50000000)
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, "Số tiền MoMo ATM phải từ 1.000 đến 50.000.000 VND.");

            var request = new MomoAtmGatewayCreateRequest(
                payment.Id,
                payment.Amount,
                string.IsNullOrWhiteSpace(orderInfo)
                    ? $"Đăng ký gói {payment.PlanType ?? "PRO"} - Company {payment.CompanyId}"
                    : orderInfo.Trim()
            );

            MomoAtmGatewayCreateResult gatewayResult;
            try
            {
                gatewayResult = await _momoAtmGatewayService.CreatePaymentUrlAsync(request).ConfigureAwait(false);
            }
            catch (BusinessRuleException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PAY-EX-10 MoMo ATM tạo URL thất bại: paymentId={PaymentId}", paymentId);
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, "Không thể tạo MoMo ATM payment URL.");
            }

            // Lưu orderId của MoMo làm idempotency key
            payment.IdempotencyKey = gatewayResult.OrderId;
            payment.UpdatedAt = UtcUnspecified(DateTime.UtcNow);
            await _paymentRepository.UpdateAsync(payment).ConfigureAwait(false);

            return new MomoAtmPaymentUrlResult(
                payment.Id,
                status,
                gatewayResult.RequestId,
                gatewayResult.OrderId,
                gatewayResult.PayUrl
            );
        }

        public async Task<MomoAtmCallbackProcessResult> ProcessMomoAtmCallbackAsync(MomoAtmCallbackRequest request, bool isIpn)
        {
            if (request == null)
                throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, "Callback request không được null.");

            // Bước 1: Xác thực chữ ký HMAC-SHA256 ngay lập tức
            if (!_momoAtmGatewayService.ValidateCallbackSignature(request))
            {
                _logger.LogWarning("PAY-EX-11 Chữ ký MoMo {Source} không hợp lệ. orderId={OrderId}", isIpn ? "IPN" : "Callback", request.OrderId);
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentCallbackMismatch, "Chữ ký callback không hợp lệ.");
            }

            // Bước 2: Parse paymentId từ orderId
            if (!TryExtractPaymentId(request.OrderId, out var paymentId))
            {
                _logger.LogWarning("PAY-EX-11 Không thể parse payment id từ orderId {OrderId}", request.OrderId);
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentCallbackMismatch, "orderId trong callback không hợp lệ.");
            }

            // Bước 3: Load payment record
            var payment = await _paymentRepository.GetByIdAsync(paymentId).ConfigureAwait(false);
            if (payment == null)
            {
                _logger.LogWarning("PAY-EX-11 Không tìm thấy payment cho orderId {OrderId}", request.OrderId);
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentCallbackMismatch, "Không tìm thấy payment cho callback.");
            }

            if (!string.Equals(payment.PaymentMethod, "MOMO", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentCallbackMismatch, "Callback không khớp với payment method.");

            // Bước 4: Validate amount
            if (!TryParseAmount(request.Amount, out var callbackAmount) || callbackAmount != payment.Amount)
            {
                _logger.LogWarning(
                    "PAY-EX-11 Amount không khớp: paymentId={PaymentId}, expected={Expected}, actual={Actual}",
                    payment.Id, payment.Amount, request.Amount);
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentCallbackMismatch, "Amount trong callback không khớp.");
            }

            if (!int.TryParse(request.ResultCode, out var resultCode))
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentCallbackMismatch, "resultCode trong callback không hợp lệ.");

            var currentStatus = NormalizePaymentStatus(payment.PaymentStatus);

            // Bước 5: Idempotency check - nếu đã SUCCESS và orderId khớp, trả về luôn
            if (currentStatus == "SUCCESS" &&
                !string.IsNullOrWhiteSpace(payment.IdempotencyKey) &&
                string.Equals(payment.IdempotencyKey, request.OrderId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("PAY-INFO-01 Idempotent response cho paymentId={PaymentId}, đã SUCCESS rồi.", payment.Id);
                return new MomoAtmCallbackProcessResult(
                    payment.Id, payment.CompanyId, currentStatus, true, resultCode,
                    request.Message ?? "Success (idempotent)");
            }

            if (resultCode == 0)
            {
                if (currentStatus == "FAILED")
                    throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, "Không thể chuyển từ FAILED sang SUCCESS.");

                if (currentStatus == "PENDING")
                {
                    var now = UtcUnspecified(DateTime.UtcNow);
                    payment.PaymentStatus = "SUCCESS";
                    payment.PaidAt = now;
                    payment.UpdatedAt = now;
                    payment.MomoTransId = request.TransId;

                    await _paymentRepository.UpdateAsync(payment).ConfigureAwait(false);
                    currentStatus = "SUCCESS";

                    // Bước 6: Activate subscription sau khi thanh toán thành công
                    var planType = payment.PlanType ?? SubscriptionPlanType.ProMonthly;
                    try
                    {
                        var subscription = await _subscriptionService.ActivateSubscriptionAsync(payment.CompanyId, planType).ConfigureAwait(false);

                        // Liên kết payment với subscription vừa tạo
                        payment.SubscriptionId = subscription.Id;
                        payment.UpdatedAt = UtcUnspecified(DateTime.UtcNow);
                        await _paymentRepository.UpdateAsync(payment).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "PAY-EX-14 Activate subscription thất bại sau khi payment SUCCESS: paymentId={PaymentId}, companyId={CompanyId}",
                            payment.Id, payment.CompanyId);
                        // Payment đã SUCCESS, không rollback. Subscription sẽ được xử lý bởi retry mechanism.
                    }
                }
            }
            else
            {
                if (currentStatus == "SUCCESS")
                    throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentUpdate, "Không thể chuyển từ SUCCESS sang FAILED.");

                if (currentStatus == "PENDING")
                {
                    payment.PaymentStatus = "FAILED";
                    payment.UpdatedAt = UtcUnspecified(DateTime.UtcNow);
                    await _paymentRepository.UpdateAsync(payment).ConfigureAwait(false);
                    currentStatus = "FAILED";
                }
            }

            return new MomoAtmCallbackProcessResult(
                payment.Id,
                payment.CompanyId,
                currentStatus,
                string.Equals(currentStatus, "SUCCESS", StringComparison.Ordinal),
                resultCode,
                request.Message ?? (resultCode == 0 ? "Success" : "Failed")
            );
        }

        private static string NormalizePaymentMethod(string? paymentMethod)
        {
            var normalized = paymentMethod?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || !AllowedMethods.Contains(normalized))
                throw new BusinessRuleException(PaymentExceptionCodes.PaymentProviderError, "PaymentMethod phải là: MANUAL, MOMO, hoặc VNPAY.");
            return normalized;
        }

        private static string NormalizePaymentStatus(string? paymentStatus)
        {
            var normalized = paymentStatus?.Trim().ToUpperInvariant();
            return normalized switch
            {
                "SUCCESS" => "SUCCESS",
                "FAILED" => "FAILED",
                "PENDING" => "PENDING",
                _ => throw new BusinessRuleException(PaymentExceptionCodes.InvalidPaymentStatus, $"Payment status '{paymentStatus}' không hợp lệ.")
            };
        }

        private async Task<Company> ValidateCompanyAsync(int companyId)
        {
            var company = await _paymentRepository.GetCompanyByIdAsync(companyId).ConfigureAwait(false);
            if (company == null)
                throw new BusinessRuleException(PaymentExceptionCodes.CompanyNotFound, $"Không tìm thấy company id={companyId}.");
            return company;
        }

        private static void EnsureCompanyIsActive(Company company)
        {
            if (string.Equals(company.Status, "DEACTIVATED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(company.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException(PaymentExceptionCodes.CompanyInactive, "Company đã bị vô hiệu hóa, không thể xử lý thanh toán.");
        }

        private static DateTime UtcUnspecified(DateTime utcNow) =>
            DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified);

        private static PaymentDto ToPaymentDto(CompanyPayment payment) =>
            new(
                payment.Id,
                payment.CompanyId,
                NormalizePaymentStatus(payment.PaymentStatus),
                payment.Amount,
                payment.PaymentMethod,
                payment.PlanType,
                payment.SubscriptionId,
                payment.PaidAt,
                payment.CreatedAt,
                payment.UpdatedAt
            );

        private static PaymentStatusResult ToStatusResult(int companyId, CompanyPayment payment, bool isUnlocked, string paymentStatus) =>
            new(
                companyId,
                isUnlocked,
                paymentStatus,
                payment.Id,
                payment.Amount,
                payment.PaymentMethod,
                payment.PlanType,
                payment.PaidAt
            );

        private static bool TryExtractPaymentId(string? orderId, out int paymentId)
        {
            paymentId = 0;
            if (string.IsNullOrWhiteSpace(orderId)) return false;

            var parts = orderId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 3 || !string.Equals(parts[0], "PAY", StringComparison.OrdinalIgnoreCase))
                return false;

            return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out paymentId) && paymentId > 0;
        }

        private static bool TryParseAmount(string? amountRaw, out decimal amount)
        {
            amount = 0;
            if (string.IsNullOrWhiteSpace(amountRaw)) return false;
            return decimal.TryParse(amountRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }

        private static void EnsureSameCompany(int callerCompanyId, int targetCompanyId)
        {
            if (callerCompanyId <= 0)
                throw new BusinessRuleException(PaymentExceptionCodes.CompanyNotFound, "CompanyId trong token không hợp lệ.");

            if (targetCompanyId <= 0)
                throw new BusinessRuleException(PaymentExceptionCodes.CompanyNotFound, "CompanyId phải là số nguyên dương.");

            if (callerCompanyId != targetCompanyId)
                throw new BusinessRuleException(
                    PaymentExceptionCodes.CrossCompanyAccessDenied,
                    "Bạn không có quyền thao tác payment của công ty khác.");
        }
    }
}
