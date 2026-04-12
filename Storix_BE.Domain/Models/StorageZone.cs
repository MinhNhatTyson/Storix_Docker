using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class StorageZone
{
    public int Id { get; set; }

    public int? WarehouseId { get; set; }

    public string? Code { get; set; }

    public string? Image { get; set; }

    public DateTime? CreatedAt { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public string? IdCode { get; set; }

    public double? Length { get; set; }

    public double? XCoordinate { get; set; }

    public double? YCoordinate { get; set; }

    public bool? IsEsd { get; set; }

    public bool? IsMsd { get; set; }

    public bool? IsCold { get; set; }

    public bool? IsVulnerable { get; set; }

    public bool? IsHighValue { get; set; }
    public virtual ICollection<Shelf> Shelves { get; set; } = new List<Shelf>();
    public virtual Warehouse? Warehouse { get; set; }
    public virtual ICollection<InventoryCountsTicket> InventoryCountsTickets { get; set; } = new List<InventoryCountsTicket>();
}
