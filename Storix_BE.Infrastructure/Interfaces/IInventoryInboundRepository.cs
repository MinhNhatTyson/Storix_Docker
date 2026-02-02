using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IInventoryInboundRepository
    {
        Task<InboundRequest> CreateInventoryInboundTicketRequest(InboundRequest request);
        Task<InboundRequest> UpdateInventoryInboundTicketRequestStatus(int ticketRequestId, int approverId, string status);
        Task<InboundOrder> CreateInboundOrderFromRequestAsync(int inboundRequestId, int createdBy);
        Task<InboundOrder> UpdateInboundOrderItemsAsync(int inboundOrderId, IEnumerable<InboundOrderItem> items);
    }
}
