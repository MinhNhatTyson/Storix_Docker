using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IInventoryOutboundRepository
    {
        Task<OutboundRequest> CreateOutboundRequestAsync(OutboundRequest request);

        Task<IReadOnlyList<(int ProductId, int AvailableQuantity)>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds);

        public sealed record WarehouseInventoryBinDto(
            int? BinId,
            string? BinCode,
            string? BinIdCode,
            int? OccupancyPercentage);

        public sealed record WarehouseInventoryLocationDto(
            int? ZoneId,
            string? ZoneCode,
            int? ShelfId,
            string? ShelfCode,
            int Quantity,
            IReadOnlyList<WarehouseInventoryBinDto> Bins);

        public sealed record WarehouseInventoryItemDto(
            int InventoryId,
            int WarehouseId,
            int ProductId,
            string? ProductName,
            string? ProductSku,
            string? ProductImage,
            int Quantity,
            int ReservedQuantity,
            DateTime? LastUpdated,
            DateTime? LastCountedAt,
            IReadOnlyList<WarehouseInventoryLocationDto> Locations);

        Task<IReadOnlyList<WarehouseInventoryItemDto>> GetWarehouseInventoryAsync(int companyId, int warehouseId);

        Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status);

        Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note, string? pricingMethod = "LastPurchasePrice");

        public sealed record InventoryPlacementDto(int OutboundOrderItemId, int ProductId, int Quantity, string BinIdCode);

        Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<OutboundOrderItem> items, IEnumerable<InventoryPlacementDto>? placements = null);

        public sealed record OutboundAvailableShelfDto(
            int ShelfId,
            string? ShelfCode,
            string? ShelfIdCode,
            int? ZoneId,
            int? WarehouseId,
            int AvailableQuantity);

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

        Task<IReadOnlyList<OutboundOrderItemAvailableLocationsDto>> GetOutboundOrderItemAvailableLocationsAsync(int outboundOrderId);

        public sealed record OutboundOrderItemSelectedLocationDto(
            int OutboundOrderItemId,
            int ProductId,
            string BinIdCode,
            int Quantity,
            DateTime? Timestamp);

        Task<IReadOnlyList<OutboundOrderItemSelectedLocationDto>> GetOutboundOrderItemSelectedLocationsAsync(int outboundOrderId);

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

        public sealed record OutboundPathOptimizationDto(
            int OutboundOrderId,
            string PayloadJson,
            int SavedBy,
            DateTime? SavedAt);

        Task<OutboundIssueDto> CreateOutboundIssueAsync(int outboundOrderId, int reportedBy, int outboundOrderItemId, int issueQuantity, string reason, string? note, string? imageUrl);
        Task<OutboundIssueDto> UpdateOutboundIssueAsync(int outboundOrderId, int issueId, int updatedBy, int? outboundOrderItemId, int? issueQuantity, string? reason, string? note, string? imageUrl);
        Task<List<OutboundIssueDto>> GetOutboundIssuesByTicketAsync(int outboundOrderId);

        Task<OutboundPathOptimizationDto> SaveOutboundPathOptimizationAsync(int outboundOrderId, int savedBy, string payloadJson);
        Task<OutboundPathOptimizationDto?> GetOutboundPathOptimizationByTicketAsync(int outboundOrderId);

        Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status);

        Task<OutboundOrder> ConfirmOutboundOrderAsync(
            int outboundOrderId,
            int performedBy,
            IEnumerable<(int ProductId, int BatchId, int Quantity)> allocations,
            IEnumerable<(int ProductId, int ShelfId, int Quantity)>? locationAllocations = null,
            string? note = null);
        Task<List<OutboundRequest>> GetAllOutboundRequestsAsync(int companyId, int? warehouseId);
        Task<List<OutboundRequest>> GetOutboundRequestsByWarehouseIdAsync(int warehouseId);
        Task<OutboundRequest> GetOutboundRequestByIdAsync(int companyId, int id);
        Task<List<OutboundOrder>> GetAllOutboundOrdersAsync(int companyId, int? warehouseId);
        Task<List<OutboundOrder>> GetOutboundOrdersByWarehouseIdAsync(int warehouseId);
        Task<OutboundOrder> GetOutboundOrderByIdAsync(int companyId, int id);
        Task<List<OutboundOrder>> GetOutboundOrdersByStaffAsync(int companyId, int staffId);
        Task<OutboundRequestExportDto?> GetOutboundRequestForExportAsync(int outboundRequestId);
        Task<OutboundOrderExportDto?> GetOutboundOrderForExportAsync(int outboundOrderId);

        byte[] ExportOutboundRequestToCsv(OutboundRequestExportDto request);
        byte[] ExportOutboundRequestToExcel(OutboundRequestExportDto request);

        byte[] ExportOutboundOrderToCsv(OutboundOrderExportDto order);
        byte[] ExportOutboundOrderToExcel(OutboundOrderExportDto order);
        Task<List<FifoBinSuggestionDto>> GetFifoSuggestedLocationsAsync(
    int warehouseId,
    int productId,
    int requiredQuantity);

        public record FifoBinSuggestionDto(
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
    }
}
