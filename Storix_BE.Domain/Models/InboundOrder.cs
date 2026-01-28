using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class InboundOrder
{
    public int Id { get; set; }

    public int? InboundRequestId { get; set; }

    public int? WarehouseId { get; set; }

    public int? SupplierId { get; set; }

    public int? CreatedBy { get; set; }

    public string? ReferenceCode { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<InboundOrderItem> InboundOrderItems { get; set; } = new List<InboundOrderItem>();

    public virtual InboundRequest? InboundRequest { get; set; }

    public virtual Supplier? Supplier { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
}
