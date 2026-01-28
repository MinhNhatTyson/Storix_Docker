using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class TransferOrder
{
    public int Id { get; set; }

    public int? SourceWarehouseId { get; set; }

    public int? DestinationWarehouseId { get; set; }

    public int? CreatedBy { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual Warehouse? DestinationWarehouse { get; set; }

    public virtual Warehouse? SourceWarehouse { get; set; }

    public virtual ICollection<TransferOrderItem> TransferOrderItems { get; set; } = new List<TransferOrderItem>();
}
