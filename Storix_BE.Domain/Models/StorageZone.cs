using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class StorageZone
{
    public int Id { get; set; }

    public int? WarehouseId { get; set; }

    public string? Code { get; set; }

    public int? TypeId { get; set; }

    public string? Image { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Shelf> Shelves { get; set; } = new List<Shelf>();

    public virtual ProductType? Type { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
}
