using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IInventoryInboundService
    {
        Task<InboundRequest> CreateInboundRequestAsync(CreateInboundRequestRequest request);

        Task<InboundRequest> UpdateInboundRequestStatusAsync(int ticketRequestId, int approverId, string status);
        Task<InboundOrder> CreateTicketFromRequestAsync(int inboundRequestId, int createdBy);

        Task<InboundOrder> UpdateTicketItemsAsync(int inboundOrderId, IEnumerable<UpdateInboundOrderItemRequest> items);
        Task<List<InboundRequest>> GetAllInboundRequestsAsync();
        Task<List<InboundOrder>> GetAllInboundOrdersAsync();
        Task<InboundRequest> GetInboundRequestByIdAsync(int id);
        Task<InboundOrder> GetInboundOrderByIdAsync(int id);
    }
    public sealed record CreateInboundOrderItemRequest(int ProductId, int ExpectedQuantity);

    public sealed record CreateInboundRequestRequest(
        int? WarehouseId,
        int? SupplierId,
        int RequestedBy,
        IEnumerable<CreateInboundOrderItemRequest> Items);

    public sealed record UpdateInboundRequestStatusRequest(int ApproverId, string Status);
    public sealed record UpdateInboundOrderItemRequest(int Id, int ProductId, int? ExpectedQuantity, int? ReceivedQuantity);
}
