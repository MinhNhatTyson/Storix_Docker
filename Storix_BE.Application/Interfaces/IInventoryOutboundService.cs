using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IInventoryOutboundService
    {
        Task<OutboundRequest> CreateOutboundRequestAsync(CreateOutboundRequestRequest request);

        Task<IReadOnlyList<InventoryAvailabilityResponse>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds);

        Task<IReadOnlyList<WarehouseInventoryItemDto>> GetWarehouseInventoryAsync(int companyId, int warehouseId);

        Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status);

        Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note, string? pricingMethod = "LastPurchasePrice");

        Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<UpdateOutboundOrderItemRequest> items);

        Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status);

        Task<List<OutboundRequestDto>> GetAllOutboundRequestsAsync(int companyId, int? warehouseId);
        Task<List<OutboundRequestDto>> GetOutboundRequestsByWarehouseIdAsync(int warehouseId);
        Task<OutboundRequestDto> GetOutboundRequestByIdAsync(int companyId, int id);
        Task<List<OutboundOrderDto>> GetAllOutboundOrdersAsync(int companyId, int? warehouseId);
        Task<List<OutboundOrderDto>> GetOutboundOrdersByWarehouseIdAsync(int warehouseId);
        Task<OutboundOrderDto> GetOutboundOrderByIdAsync(int companyId, int id);
        Task<List<OutboundOrderDto>> GetOutboundOrdersByStaffAsync(int companyId, int staffId);
        Task<List<OutboundHistoryProductResponseDto>> GetOutboundHistoryAsync(int companyId, IEnumerable<int> productIds, int? warehouseId, DateTime from, DateTime to);

        Task<IReadOnlyList<OutboundOrderItemAvailableLocationsDto>> GetOutboundOrderItemAvailableLocationsAsync(int outboundOrderId);
        Task<IReadOnlyList<OutboundOrderItemSelectedLocationDto>> GetOutboundOrderItemSelectedLocationsAsync(int outboundOrderId);

        Task<OutboundIssueDto> CreateOutboundIssueAsync(int outboundOrderId, CreateOutboundIssueRequest request);
        Task<OutboundIssueDto> UpdateOutboundIssueAsync(int outboundOrderId, int issueId, UpdateOutboundIssueRequest request);
        Task<List<OutboundIssueDto>> GetOutboundIssuesByTicketAsync(int outboundOrderId);

        Task<OutboundPathOptimizationDto> SaveOutboundPathOptimizationAsync(int outboundOrderId, CreateOutboundPathOptimizationRequest request);
        Task<OutboundPathOptimizationDto?> GetOutboundPathOptimizationByTicketAsync(int outboundOrderId);
        Task<List<FifoPickingSuggestionDto>> GetFifoPickingSuggestionsAsync(int companyId, int outboundOrderId);
        Task<List<FifoBatchAllocationDto>> GetFifoBatchAllocationsByItemAsync(int companyId, int outboundOrderId, int outboundOrderItemId);

        public record FifoPickingSuggestionDto(
            int OutboundOrderItemId,
            int ProductId,
            string? ProductName,
            int RequiredQuantity,
            bool IsFullyCoverable,
            int TotalAvailableQuantity,
            int RemainingQuantity,
            List<FifoBinSuggestionItemDto> Suggestions
        );

        public record FifoBatchAllocationDto(
            int OutboundOrderItemId,
            int ProductId,
            int BatchId,
            DateTime InboundDate,
            int RemainingQuantity,
            int BatchRemainingAfterPick,
            decimal EffectiveUnitCost,
            int BinId,
            string? BinIdCode,
            string? BinCode,
            int? ShelfId,
            string? ShelfCode,
            int? ZoneId,
            int AvailableInBin,
            int SuggestedPickQty
        );

    }
    public sealed record FifoBinSuggestionItemDto(
            int BatchId,
            DateTime InboundDate,
            decimal EffectiveUnitCost,
            int BinId,
            string? BinIdCode,
            string? BinCode,
            int? ShelfId,
            string? ShelfCode,
            int? ZoneId,
            int AvailableInBin,
            int SuggestedPickQty
        );
    public sealed record CreateOutboundOrderItemRequest(int ProductId, int Quantity);

    public sealed record CreateOutboundRequestRequest(
        int? WarehouseId,
        string? Destination,
        int RequestedBy,
        IEnumerable<CreateOutboundOrderItemRequest> Items,
        string? Reason = null);

    public sealed record UpdateOutboundRequestStatusRequest(int ApproverId, string Status);

    public sealed record UpdateOutboundLocationAssignmentRequest(string BinId, int Quantity);

    // Keep payload shape aligned with inbound edit-items:
    // id, productId, expectedQuantity, receivedQuantity, locations[]
    public sealed record UpdateOutboundOrderItemRequest(
        int Id,
        int ProductId,
        int? ExpectedQuantity,
        int? ReceivedQuantity,
        IEnumerable<UpdateOutboundLocationAssignmentRequest>? Locations);

    public sealed record CreateOutboundOrderFromRequestRequest(int CreatedBy, int? StaffId, string? Note, string? PricingMethod = "LastPurchasePrice");

    public sealed record UpdateOutboundOrderStatusRequest(int PerformedBy, string Status);

    public sealed record InventoryAvailabilityResponse(int ProductId, int AvailableQuantity);

    public sealed record WarehouseBinDto(
        int? BinId,
        string? BinCode,
        string? BinIdCode,
        int? OccupancyPercentage);

    // Quantity is tracked at the shelf/location level via inventory_locations.
    // Bins are nested metadata/occupancy only and do not represent quantity-on-hand.
    public sealed record WarehouseLocationDto(
        int? ZoneId,
        string? ZoneCode,
        int? ShelfId,
        string? ShelfCode,
        int Quantity,
        IReadOnlyList<WarehouseBinDto> Bins);

    public sealed record WarehouseInventoryItemDto(
        int InventoryId,
        int WarehouseId,
        int ProductId,
        string? ProductName,
        string? ProductSku,
        string? ProductImage,
        int Quantity,
        int ReservedQuantity,
        int AvailableQuantity,
        DateTime? LastUpdated,
        DateTime? LastCountedAt,
        IReadOnlyList<WarehouseLocationDto> Locations);

    public sealed record OutboundWarehouseDto(int Id, string? Name);

    public sealed record OutboundUserDto(int Id, string? FullName, string? Email, string? Phone);

    public sealed record OutboundOrderItemDto(
        int Id,
        int? ProductId,
        string? ProductName,
        string? ProductSku,
        int? ExpectedQuantity,
        int? ReceivedQuantity,
        int? Quantity,
        double? Price,
        double? CostPrice,
        string? PricingMethod,
        double? DisplayPrice,
        string? ProductImage,
        string? ProductDescription,
        OutboundOrderItemAvailableLocationDetailsDto AvailableLocations,
        IReadOnlyList<OutboundOrderItemSelectedLocationDto> SelectedPickLocations,
        IReadOnlyList<FifoBinSuggestionItemDto> FifoPickingSuggestion);

    public sealed record OutboundAvailableShelfDto(
        int ShelfId,
        string? ShelfCode,
        string? ShelfIdCode,
        int? ZoneId,
        int? WarehouseId,
        int AvailableQuantity);

    // Bin payload is metadata + occupancy only. It does not represent quantity-on-hand.
    public sealed record OutboundAvailableBinDto(
        int BinId,
        string? BinCode,
        string? BinIdCode,
        int? LevelId,
        int? ShelfId,
        int? InventoryId,
        int? OccupancyPercentage,
        double? Width,
        double? Height,
        double? Length);

    public sealed record OutboundOrderItemAvailableLocationsDto(
        int OutboundOrderItemId,
        int ProductId,
        string? ProductName,
        int RequiredQuantity,
        IReadOnlyList<OutboundAvailableShelfDto> AvailableShelves,
        IReadOnlyList<OutboundAvailableBinDto> AvailableBins);

    public sealed record OutboundOrderItemAvailableLocationDetailsDto(
        int RequiredQuantity,
        IReadOnlyList<OutboundAvailableShelfDto> AvailableShelves,
        IReadOnlyList<OutboundAvailableBinDto> AvailableBins);

    public sealed record OutboundOrderItemSelectedLocationDto(
        int OutboundOrderItemId,
        int ProductId,
        string? ProductName,
        string? ProductSku,
        int? ZoneId,
        string? ZoneCode,
        int? ShelfId,
        string? ShelfCode,
        int? BinId,
        string? BinCode,
        string BinIdCode,
        int? BatchId,
        DateTime? InboundDate,
        decimal? BatchUnitCost,
        double? OutboundItemPrice,
        double? OutboundItemCostPrice,
        string? PricingMethod,
        int Quantity,
        DateTime? Timestamp);

    public sealed record CreateOutboundIssueRequest(
        int ReportedBy,
        int OutboundOrderItemId,
        int IssueQuantity,
        string Reason,
        string? Note,
        string? ImageUrl);

    public sealed record UpdateOutboundIssueRequest(
        int UpdatedBy,
        int? OutboundOrderItemId,
        int? IssueQuantity,
        string? Reason,
        string? Note,
        string? ImageUrl);

    public sealed record OutboundIssueDto(
        int IssueId,
        int OutboundOrderId,
        int OutboundOrderItemId,
        int ProductId,
        int IssueQuantity,
        string Reason,
        string? Note,
        string? ImageUrl,
        int ReportedBy,
        DateTime? ReportedAt,
        int? UpdatedBy,
        DateTime? UpdatedAt);

    public sealed record CreateOutboundPathOptimizationRequest(
        int SavedBy,
        JsonElement Payload);

    public sealed record OutboundPathOptimizationDto(
        int OutboundOrderId,
        JsonElement Payload,
        int SavedBy,
        DateTime? SavedAt);

    public sealed record OutboundRequestDto(
        int Id,
        int? WarehouseId,
        int? RequestedBy,
        int? ApprovedBy,
        string? Destination,
        string? Reason,
        string? ReferenceCode,
        string? Status,
        double? TotalPrice,
        DateTime? CreatedAt,
        DateTime? ApprovedAt,
        IEnumerable<OutboundOrderItemDto> Items,
        OutboundWarehouseDto? Warehouse,
        OutboundUserDto? RequestedByUser,
        OutboundUserDto? ApprovedByUser);

    public sealed record OutboundOrderDto(
        int Id,
        int? WarehouseId,
        int? CreatedBy,
        int? StaffId,
        string? Destination,
        string? Status,
        string? Note,
        DateTime? CreatedAt,
        IEnumerable<OutboundOrderItemDto> Items,
        OutboundWarehouseDto? Warehouse,
        OutboundUserDto? CreatedByUser,
        OutboundUserDto? StaffUser);

    public sealed record OutboundHistoryPointResponseDto(DateOnly Date, int Quantity);
    public sealed record OutboundHistoryProductResponseDto(int ProductId, string? ProductName, int CurrentStock, IReadOnlyList<OutboundHistoryPointResponseDto> OutboundInfo);
}
