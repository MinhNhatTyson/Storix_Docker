using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class User
{
    public int Id { get; set; }

    public int? CompanyId { get; set; }

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? PasswordHash { get; set; }

    public int? RoleId { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();

    public virtual Company? Company { get; set; }

    public virtual ICollection<InboundOrder> InboundOrders { get; set; } = new List<InboundOrder>();

    public virtual ICollection<InboundRequest> InboundRequestApprovedByNavigations { get; set; } = new List<InboundRequest>();

    public virtual ICollection<InboundRequest> InboundRequestRequestedByNavigations { get; set; } = new List<InboundRequest>();

    public virtual ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

    public virtual ICollection<OutboundOrder> OutboundOrders { get; set; } = new List<OutboundOrder>();

    public virtual ICollection<OutboundRequest> OutboundRequestApprovedByNavigations { get; set; } = new List<OutboundRequest>();

    public virtual ICollection<OutboundRequest> OutboundRequestRequestedByNavigations { get; set; } = new List<OutboundRequest>();

    public virtual Role? Role { get; set; }

    public virtual ICollection<StockCountsTicket> StockCountsTickets { get; set; } = new List<StockCountsTicket>();

    public virtual ICollection<TransferOrder> TransferOrders { get; set; } = new List<TransferOrder>();

    public virtual ICollection<WarehouseAssignment> WarehouseAssignments { get; set; } = new List<WarehouseAssignment>();
}
