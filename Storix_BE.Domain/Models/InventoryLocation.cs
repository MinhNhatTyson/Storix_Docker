using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class InventoryLocation
{
    public int Id { get; set; }

    public int? InventoryId { get; set; }

    public int? ShelfId { get; set; }

    public int? Quantity { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Inventory? Inventory { get; set; }

    public virtual Shelf? Shelf { get; set; }
}
