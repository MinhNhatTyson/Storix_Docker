using System;

namespace Storix_BE.Domain.Models;

public partial class InventoryBatch
{
    public int Id { get; set; }

    public int InboundOrderItemId { get; set; }

    public int InboundOrderId { get; set; }

    public int ProductId { get; set; }

    public int WarehouseId { get; set; }

    public int ReceivedQuantity { get; set; }

    public int RemainingQuantity { get; set; }

    public decimal UnitCost { get; set; }

    public decimal LineDiscount { get; set; }

    /// <summary>
    /// Computed by the database: unit_cost * (1 - line_discount / 100).
    /// Read-only from the application side.
    /// </summary>
    public decimal EffectiveUnitCost { get; set; }

    /// <summary>
    /// Copied from InboundOrder.CreatedAt at insert time.
    /// Used as the FIFO ordering anchor — oldest date = picked first.
    /// </summary>
    public DateTime InboundDate { get; set; }

    /// <summary>
    /// Flips to true once RemainingQuantity reaches 0.
    /// The partial FIFO index excludes exhausted batches for fast scans.
    /// </summary>
    public bool IsExhausted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual InboundOrderItem InboundOrderItem { get; set; } = null!;

    public virtual InboundOrder InboundOrder { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;

    public virtual Warehouse Warehouse { get; set; } = null!;
    public virtual ICollection<InventoryBatchLocation> BatchLocations { get; set; }
    = new List<InventoryBatchLocation>();
}