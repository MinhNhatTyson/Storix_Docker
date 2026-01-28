using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class ShelfLevelBin
{
    public int Id { get; set; }

    public int? LevelId { get; set; }

    public string? Code { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public bool? Status { get; set; }

    public int? InventoryId { get; set; }

    public virtual Inventory? Inventory { get; set; }

    public virtual ShelfLevel? Level { get; set; }
}
