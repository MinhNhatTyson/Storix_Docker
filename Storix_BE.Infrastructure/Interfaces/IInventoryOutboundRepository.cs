using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IInventoryOutboundRepository
    {
        Task<OutboundRequest> CreateOutboundRequestAsync(OutboundRequest request);

        Task<IReadOnlyList<(int ProductId, int AvailableQuantity)>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds);

        Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status);

        Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note);

        Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<OutboundOrderItem> items);

        Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status);

        Task<OutboundOrder> ConfirmOutboundOrderAsync(int outboundOrderId, int performedBy);
    }
}
