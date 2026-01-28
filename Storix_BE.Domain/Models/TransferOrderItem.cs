using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class TransferOrderItem
{
    public int Id { get; set; }

    public int? TransferOrderId { get; set; }

    public int? ProductId { get; set; }

    public int? Quantity { get; set; }

    public virtual Product? Product { get; set; }

    public virtual TransferOrder? TransferOrder { get; set; }
}
