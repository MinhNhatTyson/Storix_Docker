using System;

namespace Storix_BE.Domain.Models;

public partial class CompanyPayment
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string PaymentStatus { get; set; } = null!;

    public decimal Amount { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Gói đăng ký muốn mua: PRO_MONTHLY | PRO_YEARLY</summary>
    public string? PlanType { get; set; }

    /// <summary>FK → subscriptions.id. Được gán sau khi payment SUCCESS và subscription được tạo.</summary>
    public int? SubscriptionId { get; set; }

    /// <summary>= MoMo orderId. Unique constraint chống duplicate IPN.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>transId từ MoMo callback, lưu để đối chiếu.</summary>
    public string? MomoTransId { get; set; }

    public virtual Company Company { get; set; } = null!;

    public virtual Subscription? Subscription { get; set; }
}