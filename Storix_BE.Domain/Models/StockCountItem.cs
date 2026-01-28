using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class StockCountItem
{
    public int Id { get; set; }

    public int? StockCountId { get; set; }

    public int? ProductId { get; set; }

    public int? SystemQuantity { get; set; }

    public int? CountedQuantity { get; set; }

    public int? Discrepancy { get; set; }

    public bool? Status { get; set; }

    public string? Description { get; set; }

    public virtual Product? Product { get; set; }

    public virtual StockCountsTicket? StockCount { get; set; }
}
