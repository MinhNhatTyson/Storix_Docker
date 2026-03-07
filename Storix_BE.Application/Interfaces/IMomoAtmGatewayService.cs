namespace Storix_BE.Service.Interfaces
{
    public interface IMomoAtmGatewayService
    {
        Task<MomoAtmGatewayCreateResult> CreatePaymentUrlAsync(MomoAtmGatewayCreateRequest request);
        bool ValidateCallbackSignature(MomoAtmCallbackRequest request);
    }

    public sealed record MomoAtmGatewayCreateRequest(
        int PaymentId,
        decimal Amount,
        string OrderInfo
    );

    public sealed record MomoAtmGatewayCreateResult(
        string OrderId,
        string RequestId,
        string PayUrl,
        int ResultCode,
        string Message
    );
}
