using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class ShelfNode
{
    public int Id { get; set; }

    public int? ShelfId { get; set; }

    public int? NodeId { get; set; }

    public virtual NavNode? Node { get; set; }

    public virtual Shelf? Shelf { get; set; }
}
