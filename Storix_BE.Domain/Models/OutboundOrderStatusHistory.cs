using System;

namespace Storix_BE.Domain.Models;

public partial class OutboundOrderStatusHistory
{
    public int Id { get; set; }

    public int OutboundOrderId { get; set; }

    public string? OldStatus { get; set; }

    public string? NewStatus { get; set; }

    public int? ChangedByUserId { get; set; }

    public DateTime? ChangedAt { get; set; }

    public virtual User? ChangedByUser { get; set; }

    public virtual OutboundOrder? OutboundOrder { get; set; }
}

