using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class InboundOrderItem
{
    public int Id { get; set; }

    public int? InboundRequestId { get; set; }

    public int? InboundOrderId { get; set; }

    public int? ProductId { get; set; }

    public int? ExpectedQuantity { get; set; }

    public int? ReceivedQuantity { get; set; }

    public virtual InboundOrder? InboundOrder { get; set; }

    public virtual InboundRequest? InboundRequest { get; set; }

    public virtual Product? Product { get; set; }

    public virtual ICollection<StorageRecommendation> StorageRecommendations { get; set; } = new List<StorageRecommendation>();
}
