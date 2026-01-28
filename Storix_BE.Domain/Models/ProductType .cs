using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class ProductType
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    public virtual ICollection<StorageZone> StorageZones { get; set; } = new List<StorageZone>();
}

