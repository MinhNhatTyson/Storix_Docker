namespace Storix_BE.Service.Interfaces
{
    public interface IPaymentService
    {
        Task<PaymentDto> CreatePaymentAsync(CreatePaymentRequest request, int callerCompanyId);
        Task<PaymentStatusResult> GetPaymentStatusAsync(int companyId, int callerCompanyId);
        Task<MomoAtmPaymentUrlResult> CreateMomoAtmPaymentUrlAsync(int paymentId, string? orderInfo, int callerCompanyId);
        Task<MomoAtmCallbackProcessResult> ProcessMomoAtmCallbackAsync(MomoAtmCallbackRequest request, bool isIpn);
    }

    public sealed record CreatePaymentRequest(
        int CompanyId,
        decimal Amount,
        string PaymentMethod,
        string PlanType
    );

    public sealed record PaymentDto(
        int Id,
        int CompanyId,
        string PaymentStatus,
        decimal Amount,
        string PaymentMethod,
        string? PlanType,
        int? SubscriptionId,
        DateTime? PaidAt,
        DateTime CreatedAt,
        DateTime? UpdatedAt
    );

    public sealed record PaymentStatusResult(
        int CompanyId,
        bool IsUnlocked,
        string PaymentStatus,
        int? PaymentId,
        decimal? Amount,
        string? PaymentMethod,
        string? PlanType,
        DateTime? PaidAt
    );

    public sealed record CreateMomoAtmUrlRequest(string? OrderInfo);

    public sealed record MomoAtmPaymentUrlResult(
        int PaymentId,
        string PaymentStatus,
        string RequestId,
        string OrderId,
        string PayUrl
    );

    public sealed class MomoAtmCallbackRequest
    {
        public string? PartnerCode { get; set; }
        public string? OrderId { get; set; }
        public string? RequestId { get; set; }
        public string? Amount { get; set; }
        public string? OrderInfo { get; set; }
        public string? OrderType { get; set; }
        public string? TransId { get; set; }
        public string? ResultCode { get; set; }
        public string? Message { get; set; }
        public string? PayType { get; set; }
        public string? ResponseTime { get; set; }
        public string? ExtraData { get; set; }
        public string? Signature { get; set; }
    }

    public sealed record MomoAtmCallbackProcessResult(
        int PaymentId,
        int CompanyId,
        string PaymentStatus,
        bool IsUnlocked,
        int ProviderResultCode,
        string ProviderMessage
    );
}
