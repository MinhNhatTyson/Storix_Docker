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

    public int? ExpectedQuantity { get; set; }

    public int? ReceivedQuantity { get; set; }

    /// <summary>Giá bán (sale price)</summary>
    public double? Price { get; set; }

    /// <summary>Phương pháp tính giá vốn: LastPurchasePrice | SpecificIdentification</summary>
    public string? PricingMethod { get; set; }

    /// <summary>Giá vốn tại thời điểm xuất kho (cost price)</summary>
    public double? CostPrice { get; set; }

    public virtual OutboundOrder? OutboundOrder { get; set; }

    public virtual OutboundRequest? OutboundRequest { get; set; }

    public virtual Product? Product { get; set; }
}
