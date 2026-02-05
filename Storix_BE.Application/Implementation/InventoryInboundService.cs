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

        public async Task<InboundOrder> CreateTicketFromRequestAsync(int inboundRequestId, int createdBy)
        {
            if (inboundRequestId <= 0) throw new ArgumentException("Invalid inboundRequestId.", nameof(inboundRequestId));
            if (createdBy <= 0) throw new ArgumentException("Invalid createdBy.", nameof(createdBy));

            return await _repo.CreateInboundOrderFromRequestAsync(inboundRequestId, createdBy);
        }

        public async Task<InboundOrder> UpdateTicketItemsAsync(int inboundOrderId, IEnumerable<UpdateInboundOrderItemRequest> items)
        {
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inboundOrderId.", nameof(inboundOrderId));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (!items.Any()) throw new InvalidOperationException("Items payload cannot be empty.");

            // Map DTOs to domain InboundOrderItem objects (Id can be 0 for new items)
            var domainItems = items.Select(i => new InboundOrderItem
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ExpectedQuantity = i.ExpectedQuantity,
                ReceivedQuantity = i.ReceivedQuantity
            }).ToList();

            return await _repo.UpdateInboundOrderItemsAsync(inboundOrderId, domainItems);
        }
        public async Task<List<InboundRequest>> GetAllInboundRequestsAsync()
        {
            return await _repo.GetAllInboundRequestsAsync();
        }

        public async Task<List<InboundOrder>> GetAllInboundOrdersAsync()
        {
            return await _repo.GetAllInboundOrdersAsync();
        }
        public async Task<InboundRequest> GetInboundRequestByIdAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid inbound request id.", nameof(id));
            return await _repo.GetInboundRequestByIdAsync(id);
        }

        public async Task<InboundOrder> GetInboundOrderByIdAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid inbound order id.", nameof(id));
            return await _repo.GetInboundOrderByIdAsync(id);
        }
    }
}
