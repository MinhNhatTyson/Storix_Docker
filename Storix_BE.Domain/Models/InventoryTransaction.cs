using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class InventoryTransaction
{
    public int Id { get; set; }

    public int? WarehouseId { get; set; }

    public int? ProductId { get; set; }

    public string? TransactionType { get; set; }

    public int? QuantityChange { get; set; }

    public int? ReferenceId { get; set; }

    public int? PerformedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User? PerformedByNavigation { get; set; }

    public virtual Product? Product { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
}
