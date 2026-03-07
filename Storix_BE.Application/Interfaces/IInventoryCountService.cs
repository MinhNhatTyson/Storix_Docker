using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IInventoryCountService
    {
        Task<IReadOnlyList<InventoryCountInventoryProductDto>> ListInventoryProductsAsync(int companyId, int warehouseId, IEnumerable<int>? productIds = null);

        Task<InventoryCountTicketDetailDto> CreateTicketAsync(int companyId, int createdByUserId, CreateInventoryCountTicketRequest request);

        Task<List<InventoryCountTicketListItemDto>> ListTicketsAsync(int companyId, int? warehouseId, string? status);

        Task<InventoryCountTicketDetailDto> GetTicketByIdAsync(int companyId, int ticketId);

        Task<InventoryCountItemDto> UpdateCountedQuantityAsync(int companyId, int callerUserId, int callerRoleId, int itemId, UpdateInventoryCountItemRequest request);

        Task<RunInventoryCountResultDto> RunAsync(int companyId, int createdByUserId, int ticketId);

        Task ApproveAsync(int companyId, int performedByUserId, int ticketId);
    }

    public sealed record InventoryCountInventoryProductDto(
        int ProductId,
        string? Sku,
        string? Name,
        int Quantity);

    public sealed record CreateInventoryCountTicketRequest(
        int WarehouseId,
        string? Name,
        string? Type,
        string? Description,
        IEnumerable<int>? ProductIds,
        int? AssignedTo = null);

    public sealed record InventoryCountTicketListItemDto(
        int Id,
        int? WarehouseId,
        string? Name,
        string? Type,
        string? Status,
        DateTime? CreatedAt,
        DateTime? ExecutedDay,
        DateTime? FinishedDay,
        int ItemCount);

    public sealed record InventoryCountItemDto(
        int Id,
        int? ProductId,
        string? Sku,
        string? ProductName,
        int? SystemQuantity,
        int? CountedQuantity,
        int? Discrepancy,
        bool? Status,
        string? Description);

    public sealed record InventoryCountTicketDetailDto(
        int Id,
        int? WarehouseId,
        string? Name,
        string? Type,
        string? Status,
        DateTime? CreatedAt,
        DateTime? ExecutedDay,
        DateTime? FinishedDay,
        string? Description,
        IReadOnlyList<InventoryCountItemDto> Items);

    public sealed record UpdateInventoryCountItemRequest(
        int CountedQuantity,
        string? Description,
        bool? Status);

    public sealed record RunInventoryCountResultDto(
        int ReportId,
        JsonElement Summary,
        JsonElement Data);
}

