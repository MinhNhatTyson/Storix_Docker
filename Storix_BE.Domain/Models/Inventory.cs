using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class Inventory
{
    public int Id { get; set; }

    public int? WarehouseId { get; set; }

    public int? ProductId { get; set; }

    public int? Quantity { get; set; }

    public int? ReservedQuantity { get; set; }

    public DateTime? LastUpdated { get; set; }

    public virtual ICollection<InventoryLocation> InventoryLocations { get; set; } = new List<InventoryLocation>();

    public virtual Product? Product { get; set; }

    public virtual ICollection<ShelfLevelBin> ShelfLevelBins { get; set; } = new List<ShelfLevelBin>();

    public virtual Warehouse? Warehouse { get; set; }
}
