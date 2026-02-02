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
    }
    public sealed record CreateInboundOrderItemRequest(int ProductId, int ExpectedQuantity);

    public sealed record CreateInboundRequestRequest(
        int? WarehouseId,
        int? SupplierId,
        int RequestedBy,
        IEnumerable<CreateInboundOrderItemRequest> Items);

    public sealed record UpdateInboundRequestStatusRequest(int ApproverId, string Status);
}
