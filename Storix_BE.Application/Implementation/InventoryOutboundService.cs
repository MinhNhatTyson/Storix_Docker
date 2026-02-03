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
    public class InventoryOutboundService : IInventoryOutboundService
    {
        private readonly IInventoryOutboundRepository _repo;

        public InventoryOutboundService(IInventoryOutboundRepository repo)
        {
            _repo = repo;
        }

        public async Task<OutboundRequest> CreateOutboundRequestAsync(CreateOutboundRequestRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.RequestedBy <= 0) throw new ArgumentException("Invalid requestedBy.", nameof(request.RequestedBy));
            if (request.Items == null || !request.Items.Any())
                throw new InvalidOperationException("Request must contain at least one product item.");

            var invalid = request.Items.FirstOrDefault(i => i.ProductId <= 0 || i.Quantity <= 0);
            if (invalid != null)
                throw new InvalidOperationException("Each item must have a positive ProductId and Quantity.");

            var outboundRequest = new OutboundRequest
            {
                WarehouseId = request.WarehouseId,
                Destination = request.Destination,
                RequestedBy = request.RequestedBy
            };

            foreach (var item in request.Items)
            {
                outboundRequest.OutboundOrderItems.Add(new OutboundOrderItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity
                });
            }

            return await _repo.CreateOutboundRequestAsync(outboundRequest);
        }

        public async Task<IReadOnlyList<InventoryAvailabilityResponse>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds)
        {
            var data = await _repo.GetInventoryAvailabilityAsync(warehouseId, productIds);
            return data.Select(x => new InventoryAvailabilityResponse(x.ProductId, x.AvailableQuantity)).ToList();
        }

        public async Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status)
        {
            if (requestId <= 0) throw new ArgumentException("Invalid request id.", nameof(requestId));
            if (approverId <= 0) throw new ArgumentException("Invalid approver id.", nameof(approverId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            return await _repo.UpdateOutboundRequestStatusAsync(requestId, approverId, status);
        }

        public async Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note)
        {
            if (outboundRequestId <= 0) throw new ArgumentException("Invalid outboundRequestId.", nameof(outboundRequestId));
            if (createdBy <= 0) throw new ArgumentException("Invalid createdBy.", nameof(createdBy));

            return await _repo.CreateOutboundOrderFromRequestAsync(outboundRequestId, createdBy, staffId, note);
        }

        public async Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<UpdateOutboundOrderItemRequest> items)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (!items.Any()) throw new InvalidOperationException("Items payload cannot be empty.");

            var domainItems = items.Select(i => new OutboundOrderItem
            {
                Id = i.Id,
                ProductId = i.ProductId,
                Quantity = i.Quantity
            }).ToList();

            return await _repo.UpdateOutboundOrderItemsAsync(outboundOrderId, domainItems);
        }

        public async Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (performedBy <= 0) throw new ArgumentException("Invalid performedBy.", nameof(performedBy));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            return await _repo.UpdateOutboundOrderStatusAsync(outboundOrderId, performedBy, status);
        }

        public async Task<OutboundOrder> ConfirmOutboundOrderAsync(int outboundOrderId, int performedBy)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (performedBy <= 0) throw new ArgumentException("Invalid performedBy.", nameof(performedBy));

            return await _repo.ConfirmOutboundOrderAsync(outboundOrderId, performedBy);
        }
    }
}
