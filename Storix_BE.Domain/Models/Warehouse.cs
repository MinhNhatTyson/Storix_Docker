using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class Warehouse
{
    public int Id { get; set; }

    public int? CompanyId { get; set; }

    public string? Name { get; set; }

    public string? Address { get; set; }

    public string? Description { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public int? Length { get; set; }

    public int? GridSize { get; set; }

    public string? Status { get; set; }

    public string? Image { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Company? Company { get; set; }

    public virtual ICollection<InboundOrder> InboundOrders { get; set; } = new List<InboundOrder>();

    public virtual ICollection<InboundRequest> InboundRequests { get; set; } = new List<InboundRequest>();

    public virtual ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();

    public virtual ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    public virtual ICollection<NavEdge> NavEdges { get; set; } = new List<NavEdge>();

    public virtual ICollection<NavNode> NavNodes { get; set; } = new List<NavNode>();

    public virtual ICollection<OutboundOrder> OutboundOrders { get; set; } = new List<OutboundOrder>();

    public virtual ICollection<OutboundRequest> OutboundRequests { get; set; } = new List<OutboundRequest>();

    public virtual ICollection<StockCountsTicket> StockCountsTickets { get; set; } = new List<StockCountsTicket>();

    public virtual ICollection<StorageForecast> StorageForecasts { get; set; } = new List<StorageForecast>();

    public virtual ICollection<StorageZone> StorageZones { get; set; } = new List<StorageZone>();

    public virtual ICollection<TransferOrder> TransferOrderDestinationWarehouses { get; set; } = new List<TransferOrder>();

    public virtual ICollection<TransferOrder> TransferOrderSourceWarehouses { get; set; } = new List<TransferOrder>();

    public virtual ICollection<WarehouseAssignment> WarehouseAssignments { get; set; } = new List<WarehouseAssignment>();
}
