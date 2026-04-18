using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class TransferOrderItem
{
    public int Id { get; set; }

    public int? TransferOrderId { get; set; }

    public int? ProductId { get; set; }

    public int? Quantity { get; set; }

    public int? InboundOrderItemId { get; set; }

    public int? OutboundOrderItemId { get; set; }

    public virtual InboundOrderItem? InboundOrderItem { get; set; }

    public virtual OutboundOrderItem? OutboundOrderItem { get; set; }

    public virtual Product? Product { get; set; }

    public virtual TransferOrder? TransferOrder { get; set; }
}
