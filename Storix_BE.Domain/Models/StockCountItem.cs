using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class InventoryCountItem
{
    public int Id { get; set; }

    public int? InventoryCountId { get; set; }

    public int? ProductId { get; set; }

    public int? SystemQuantity { get; set; }

    public int? CountedQuantity { get; set; }

    public int? Discrepancy { get; set; }

    public bool? Status { get; set; }

    public string? Description { get; set; }

    public int? LocationId { get; set; }

    public int? RecountedQuantity { get; set; }

    public int? FinalQuantity { get; set; }

    public int? CountedBy { get; set; }

    public int? RecountedBy { get; set; }

    public DateTime? CountedAt { get; set; }

    public DateTime? RecountedAt { get; set; }

    public virtual Product? Product { get; set; }

    public virtual InventoryCountsTicket? InventoryCount { get; set; }
    public virtual InventoryLocation? Location { get; set; }
}
