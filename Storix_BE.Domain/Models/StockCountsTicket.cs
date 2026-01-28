using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class StockCountsTicket
{
    public int Id { get; set; }

    public int? WarehouseId { get; set; }

    public int? PerformedBy { get; set; }

    public int? AssignedTo { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ExecutedDay { get; set; }

    public DateTime? FinishedDay { get; set; }

    public string? Description { get; set; }

    public virtual User? PerformedByNavigation { get; set; }

    public virtual ICollection<StockCountItem> StockCountItems { get; set; } = new List<StockCountItem>();

    public virtual Warehouse? Warehouse { get; set; }
}
