using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class OutboundOrderItem
{
    public int Id { get; set; }

    public int? OutboundRequestId { get; set; }

    public int? OutboundOrderId { get; set; }

    public int? ProductId { get; set; }

    public int? Quantity { get; set; }
    public double? Price { get; set; }

    public virtual OutboundOrder? OutboundOrder { get; set; }

    public virtual OutboundRequest? OutboundRequest { get; set; }

    public virtual Product? Product { get; set; }
}
