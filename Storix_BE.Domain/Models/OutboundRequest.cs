using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class OutboundRequest
{
    public int Id { get; set; }

    public int? WarehouseId { get; set; }

    public int? RequestedBy { get; set; }

    public int? ApprovedBy { get; set; }

    public string? Destination { get; set; }

    public string? Status { get; set; }
    public double? TotalPrice { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public virtual User? ApprovedByNavigation { get; set; }

    public virtual ICollection<OutboundOrderItem> OutboundOrderItems { get; set; } = new List<OutboundOrderItem>();

    public virtual User? RequestedByNavigation { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
}
