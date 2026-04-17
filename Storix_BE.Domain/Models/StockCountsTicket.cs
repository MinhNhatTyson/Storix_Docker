using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class InventoryCountsTicket
{
    public int Id { get; set; }

    public int? WarehouseId { get; set; }

    public int? PerformedBy { get; set; }

    public int? AssignedTo { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ExecutedDay { get; set; }

    public DateTime? FinishedDay { get; set; }

    public string? Description { get; set; }

    public string? ScopeType { get; set; }

    public DateTime? PlannedAt { get; set; }

    public int? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public virtual User? ApprovedByNavigation { get; set; }

    public virtual User? AssignedToNavigation { get; set; }

    public virtual User? PerformedByNavigation { get; set; }

    public virtual Warehouse? Warehouse { get; set; }

    public virtual ICollection<InventoryCountItem> InventoryCountItems { get; set; } = new List<InventoryCountItem>();

    public virtual ICollection<StorageZone> StorageZones { get; set; } = new List<StorageZone>();
}