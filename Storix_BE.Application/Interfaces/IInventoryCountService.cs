using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IInventoryCountService
    {
        Task<InventoryCountsTicket> CreateStockCountTicketAsync(CreateStockCountTicketRequest request);
        Task<InventoryCountsTicket> UpdateStockCountTicketStatusAsync(int ticketId, int approverId, string status);
        Task<InventoryCountsTicket> UpdateStockCountItemsAsync(int ticketId, UpdateStockCountItemsRequest request);
        Task<List<InventoryCountsTicket>> GetStockCountTicketsByCompanyAsync(int companyId);
        Task<InventoryCountsTicket> GetStockCountTicketByIdAsync(int companyId, int id);
        Task<List<InventoryCountsTicket>> GetStockCountTicketsByStaffAsync(int companyId, int staffId);
        Task<List<InventoryCountsTicket>> GetStockCountTicketsByWarehouseAsync(int companyId, int warehouseId);
    }

    public sealed record CreateInventoryCountItemRequest(int ProductId);
    public sealed record CreateStockCountTicketRequest(
        int? WarehouseId,
        int PerformedBy,
        int? AssignedTo,
        string? Name,
        string? Description,
        string? ScopeType,
        IEnumerable<int>? StorageZoneIds,
        DateTime? PlannedAt,
        IEnumerable<CreateInventoryCountItemRequest> Items);

    public sealed record UpdateInventoryCountItemRequest(int StockCountItemId, int? ProductId, int? CountedQuantity, string? BinId);
    public sealed record UpdateStockCountItemsRequest(int PerformedBy, IEnumerable<UpdateInventoryCountItemRequest> Items);
    public sealed record UpdateStockCountTicketStatusRequest(int ApproverId, string Status);

}

