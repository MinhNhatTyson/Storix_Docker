using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class Product
{
    public int Id { get; set; }

    public int? CompanyId { get; set; }

    public string? Sku { get; set; }

    public string? Name { get; set; }

    public int? TypeId { get; set; }

    public string? Category { get; set; }

    public string? Unit { get; set; }

    public double? Weight { get; set; }

    public string? Description { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Company? Company { get; set; }

    public virtual ICollection<InboundOrderItem> InboundOrderItems { get; set; } = new List<InboundOrderItem>();

    public virtual ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();

    public virtual ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    public virtual ICollection<OutboundOrderItem> OutboundOrderItems { get; set; } = new List<OutboundOrderItem>();

    public virtual ICollection<StockCountItem> StockCountItems { get; set; } = new List<StockCountItem>();

    public virtual ICollection<StorageForecast> StorageForecasts { get; set; } = new List<StorageForecast>();

    public virtual ICollection<TransferOrderItem> TransferOrderItems { get; set; } = new List<TransferOrderItem>();

    public virtual ProductType? Type { get; set; }
}
