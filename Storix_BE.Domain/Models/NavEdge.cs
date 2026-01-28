using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class NavEdge
{
    public int Id { get; set; }

    public int? NodeFrom { get; set; }

    public int? NodeTo { get; set; }

    public double? Distance { get; set; }

    public int? WarehouseId { get; set; }

    public virtual NavNode? NodeFromNavigation { get; set; }

    public virtual NavNode? NodeToNavigation { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
}
