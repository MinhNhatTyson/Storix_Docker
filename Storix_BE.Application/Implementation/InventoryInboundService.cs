using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class InventoryInboundService : IInventoryInboundService
    {
        private readonly IInventoryInboundRepository _repo;

        public InventoryInboundService(IInventoryInboundRepository repo)
        {
            _repo = repo;
        }

        public async Task<InboundRequest> CreateInboundRequestAsync(CreateInboundRequestRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Items == null || !request.Items.Any())
                throw new InvalidOperationException("Request must contain at least one product item.");

            // Validate items
            var invalid = request.Items.FirstOrDefault(i => i.ProductId <= 0 || i.ExpectedQuantity <= 0);
            if (invalid != null)
                throw new InvalidOperationException("Each item must have a positive ProductId and ExpectedQuantity.");

            var inboundRequest = new InboundRequest
            {
                WarehouseId = request.WarehouseId,
                SupplierId = request.SupplierId,
                RequestedBy = request.RequestedBy,
            };

            foreach (var item in request.Items)
            {
                inboundRequest.InboundOrderItems.Add(new InboundOrderItem
                {
                    ProductId = item.ProductId,
                    ExpectedQuantity = item.ExpectedQuantity
                });
            }

            var createdId = await _repo.CreateInventoryInboundTicketRequest(inboundRequest);
            return createdId;
        }

        public async Task<InboundRequest> UpdateInboundRequestStatusAsync(int ticketRequestId, int approverId, string status)
        {
            if (ticketRequestId <= 0) throw new ArgumentException("Invalid ticket id.", nameof(ticketRequestId));
            if (approverId <= 0) throw new ArgumentException("Invalid approver id.", nameof(approverId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var id = await _repo.UpdateInventoryInboundTicketRequestStatus(ticketRequestId, approverId, status);
            return id;
        }
    }
}
