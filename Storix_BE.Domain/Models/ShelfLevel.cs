using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class ShelfLevel
{
    public int Id { get; set; }

    public int? ShelfId { get; set; }

    public string? Code { get; set; }

    public virtual Shelf? Shelf { get; set; }

    public virtual ICollection<ShelfLevelBin> ShelfLevelBins { get; set; } = new List<ShelfLevelBin>();
}
