using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class NavNode
{
    public int Id { get; set; }

    public int? XCoordinate { get; set; }

    public int? YCoordinate { get; set; }

    public string? Type { get; set; }

    public int? WarehouseId { get; set; }

    public virtual ICollection<NavEdge> NavEdgeNodeFromNavigations { get; set; } = new List<NavEdge>();

    public virtual ICollection<NavEdge> NavEdgeNodeToNavigations { get; set; } = new List<NavEdge>();

    public virtual ICollection<ShelfNode> ShelfNodes { get; set; } = new List<ShelfNode>();

    public virtual Warehouse? Warehouse { get; set; }
}
