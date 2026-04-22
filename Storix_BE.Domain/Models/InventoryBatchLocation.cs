using System;

namespace Storix_BE.Domain.Models;

public partial class InventoryBatchLocation
{
    public int Id { get; set; }

    public int BatchId { get; set; }

    public int BinId { get; set; }

    public int Quantity { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual InventoryBatch Batch { get; set; } = null!;

    public virtual ShelfLevelBin Bin { get; set; } = null!;
}