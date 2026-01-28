using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class Shelf
{
    public int Id { get; set; }

    public int? ZoneId { get; set; }

    public string? Code { get; set; }

    public int? Capacity { get; set; }

    public int? XCoordinate { get; set; }

    public int? YCoordinate { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? Image { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<InventoryLocation> InventoryLocations { get; set; } = new List<InventoryLocation>();

    public virtual ICollection<ShelfLevel> ShelfLevels { get; set; } = new List<ShelfLevel>();

    public virtual ICollection<ShelfNode> ShelfNodes { get; set; } = new List<ShelfNode>();

    public virtual StorageZone? Zone { get; set; }
}
