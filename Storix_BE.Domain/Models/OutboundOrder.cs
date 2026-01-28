using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class OutboundOrder
{
    public int Id { get; set; }

    public int? WarehouseId { get; set; }

    public int? CreatedBy { get; set; }

    public string? Destination { get; set; }

    public int? StaffId { get; set; }

    public string? Status { get; set; }

    public string? Note { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<OutboundOrderItem> OutboundOrderItems { get; set; } = new List<OutboundOrderItem>();

    public virtual Warehouse? Warehouse { get; set; }
}
