using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IInventoryOutboundRepository
    {
        Task<OutboundRequest> CreateOutboundRequestAsync(OutboundRequest request);

        Task<IReadOnlyList<(int ProductId, int AvailableQuantity)>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds);

        Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status);

        Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note, string? pricingMethod = "LastPurchasePrice");

        public sealed record InventoryPlacementDto(int OutboundOrderItemId, int ProductId, int Quantity, string BinIdCode);

        Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<OutboundOrderItem> items, IEnumerable<InventoryPlacementDto>? placements = null);

        Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status);

        Task<OutboundOrder> ConfirmOutboundOrderAsync(
            int outboundOrderId,
            int performedBy,
            IEnumerable<(int ProductId, int BatchId, int Quantity)> allocations,
            IEnumerable<(int ProductId, int ShelfId, int Quantity)>? locationAllocations = null,
            string? note = null);
        Task<List<OutboundRequest>> GetAllOutboundRequestsAsync(int companyId, int? warehouseId);
        Task<OutboundRequest> GetOutboundRequestByIdAsync(int companyId, int id);
        Task<List<OutboundOrder>> GetAllOutboundOrdersAsync(int companyId, int? warehouseId);
        Task<OutboundOrder> GetOutboundOrderByIdAsync(int companyId, int id);
        Task<List<OutboundOrder>> GetOutboundOrdersByStaffAsync(int companyId, int staffId);
        Task<OutboundRequestExportDto?> GetOutboundRequestForExportAsync(int outboundRequestId);
        Task<OutboundOrderExportDto?> GetOutboundOrderForExportAsync(int outboundOrderId);

        byte[] ExportOutboundRequestToCsv(OutboundRequestExportDto request);
        byte[] ExportOutboundRequestToExcel(OutboundRequestExportDto request);

        byte[] ExportOutboundOrderToCsv(OutboundOrderExportDto order);
        byte[] ExportOutboundOrderToExcel(OutboundOrderExportDto order);
    }
}