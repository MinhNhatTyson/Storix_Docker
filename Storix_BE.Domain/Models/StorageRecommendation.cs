using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class StorageRecommendation
{
    public int Id { get; set; }

    public int? InboundProductId { get; set; }

    public string? StorageRecommendation1 { get; set; }

    public string? Reason { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual InboundOrderItem? InboundProduct { get; set; }
}
