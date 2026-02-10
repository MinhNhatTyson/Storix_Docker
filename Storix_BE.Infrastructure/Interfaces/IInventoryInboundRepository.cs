using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IInventoryInboundRepository
    {
        Task<List<InboundRequest>> GetAllInboundRequestsAsync(int companyId);
        Task<List<InboundOrder>> GetAllInboundOrdersAsync(int companyId);
        Task<InboundRequest> GetInboundRequestByIdAsync(int companyId, int id);
        Task<InboundOrder> GetInboundOrderByIdAsync(int companyId, int id);
        Task<InboundRequest> CreateInventoryInboundTicketRequest(InboundRequest request, IEnumerable<ProductPrice>? productPrices = null);
        Task<InboundRequest> UpdateInventoryInboundTicketRequestStatus(int ticketRequestId, int approverId, string status);
        Task<InboundOrder> CreateInboundOrderFromRequestAsync(int inboundRequestId, int createdBy, int? staffId);
        Task<InboundOrder> UpdateInboundOrderItemsAsync(int inboundOrderId, IEnumerable<InboundOrderItem> items);
        Task<bool> InboundRequestCodeExistsAsync(string code);
        Task<List<InboundOrder>> GetInboundOrdersByStaffAsync(int companyId, int staffId);
        Task<InboundRequestExportDto?> GetInboundRequestForExportAsync(int inboundRequestId);
        Task<InboundOrderExportDto?> GetInboundOrderForExportAsync(int inboundOrderId);

        byte[] ExportInboundRequestToCsv(InboundRequestExportDto request);
        byte[] ExportInboundRequestToExcel(InboundRequestExportDto request);

        byte[] ExportInboundOrderToCsv(InboundOrderExportDto order);
        byte[] ExportInboundOrderToExcel(InboundOrderExportDto order);
    }
}
