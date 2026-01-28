using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class WarehouseAssignment
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int? WarehouseId { get; set; }

    public string? RoleInWarehouse { get; set; }

    public DateTime? AssignedAt { get; set; }

    public virtual User? User { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
}
