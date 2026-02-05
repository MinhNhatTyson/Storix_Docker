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

            // Validate prices and discounts on line items
            var invalidPrice = request.Items.FirstOrDefault(i => double.IsNaN(i.Price) || i.Price < 0);
            if (invalidPrice != null)
                throw new InvalidOperationException("Each item must have a non-negative Price.");

            var invalidLineDiscount = request.Items.FirstOrDefault(i => double.IsNaN(i.LineDiscount) || i.LineDiscount < 0 || i.LineDiscount > 1);
            if (invalidLineDiscount != null)
                throw new InvalidOperationException("Each item LineDiscount must be between 0 and 1 (fractional).");

            if (request.OrderDiscount.HasValue && (double.IsNaN(request.OrderDiscount.Value) || request.OrderDiscount.Value < 0 || request.OrderDiscount.Value > 1))
                throw new InvalidOperationException("OrderDiscount must be between 0 and 1 (fractional) when provided.");

            var inboundRequest = new InboundRequest
            {
                WarehouseId = request.WarehouseId,
                SupplierId = request.SupplierId,
                RequestedBy = request.RequestedBy,
                OrderDiscount = request.OrderDiscount,
            };

            foreach (var item in request.Items)
            {
                inboundRequest.InboundOrderItems.Add(new InboundOrderItem
                {
                    ProductId = item.ProductId,
                    ExpectedQuantity = item.ExpectedQuantity,
                    Price = item.Price,
                    Discount = item.LineDiscount
                });
            }

            // Calculate total price from items (apply line-level discounts) and FinalPrice after order discount
            double totalPrice = 0.0;
            foreach (var item in request.Items)
            {
                var qty = item.ExpectedQuantity;
                var price = item.Price;
                var lineDiscount = item.LineDiscount; // expected fractional (e.g. 0.1 = 10%)

                var effectiveUnitPrice = price * (1.0 - lineDiscount);
                if (effectiveUnitPrice < 0) effectiveUnitPrice = 0; // safety

                totalPrice += effectiveUnitPrice * qty;
            }

            inboundRequest.TotalPrice = totalPrice;

            if (request.OrderDiscount.HasValue)
            {
                var final = totalPrice * (1.0 - request.OrderDiscount.Value);
                if (final < 0) final = 0;
                inboundRequest.FinalPrice = final;
            }
            else
            {
                inboundRequest.FinalPrice = totalPrice;
            }

            var nowDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var productPrices = request.Items.Select(i => new ProductPrice
            {
                ProductId = i.ProductId,
                Price = i.Price,
                LineDiscount = i.LineDiscount,
                Date = nowDate
            }).ToList();

            var createdRequest = await _repo.CreateInventoryInboundTicketRequest(inboundRequest, productPrices);
            return createdRequest;
        }

        public async Task<InboundRequest> UpdateInboundRequestStatusAsync(int ticketRequestId, int approverId, string status)
        {
            if (ticketRequestId <= 0) throw new ArgumentException("Invalid ticket id.", nameof(ticketRequestId));
            if (approverId <= 0) throw new ArgumentException("Invalid approver id.", nameof(approverId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var id = await _repo.UpdateInventoryInboundTicketRequestStatus(ticketRequestId, approverId, status);
            return id;
        }

        public async Task<InboundOrder> CreateTicketFromRequestAsync(int inboundRequestId, int createdBy, int? staffId)
        {
            if (inboundRequestId <= 0) throw new ArgumentException("Invalid inboundRequestId.", nameof(inboundRequestId));
            if (createdBy <= 0) throw new ArgumentException("Invalid createdBy.", nameof(createdBy));
            // staffId may be null (optional)

            return await _repo.CreateInboundOrderFromRequestAsync(inboundRequestId, createdBy, staffId);
        }

        public async Task<InboundOrder> UpdateTicketItemsAsync(int inboundOrderId, IEnumerable<UpdateInboundOrderItemRequest> items)
        {
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inboundOrderId.", nameof(inboundOrderId));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (!items.Any()) throw new InvalidOperationException("Items payload cannot be empty.");

            var domainItems = items.Select(i => new InboundOrderItem
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ExpectedQuantity = i.ExpectedQuantity,
                ReceivedQuantity = i.ReceivedQuantity
            }).ToList();

            return await _repo.UpdateInboundOrderItemsAsync(inboundOrderId, domainItems);
        }
        private static SupplierDto? MapSupplier(Supplier? s)
        {
            if (s == null) return null;
            return new SupplierDto(s.Id, s.Name, s.Phone, s.Email);
        }

        private static WarehouseDto? MapWarehouse(Warehouse? w)
        {
            if (w == null) return null;
            return new WarehouseDto(w.Id, w.Name, w.Address, w.Description, w.Width, w.Height, w.Length);
        }

        private static UserDto? MapUser(User? u)
        {
            if (u == null) return null;
            return new UserDto(u.Id, u.FullName, u.Email, u.Phone);
        }

        private static InboundOrderItemDto MapInboundOrderItem(InboundOrderItem item)
        {
            if (item == null) return null!;
            var p = item.Product;
            return new InboundOrderItemDto(
                item.Id,
                item.ProductId,
                p?.Sku,
                p?.Name,
                item.ExpectedQuantity,
                p?.TypeId,
                p?.Description);
        }

        private static InboundRequestDto MapInboundRequestToDto(InboundRequest r)
        {
            var items = (r.InboundOrderItems ?? Enumerable.Empty<InboundOrderItem>()).Select(MapInboundOrderItem).ToList();
            return new InboundRequestDto(
                r.Id,
                r.WarehouseId,
                r.SupplierId,
                r.RequestedBy,
                r.ApprovedBy,
                r.Status,
                r.TotalPrice,
                r.OrderDiscount,
                r.FinalPrice,
                r.CreatedAt,
                r.ApprovedAt,
                items,
                MapSupplier(r.Supplier),
                MapWarehouse(r.Warehouse),
                MapUser(r.RequestedByNavigation),
                MapUser(r.ApprovedByNavigation));
        }

        private static InboundOrderDto MapInboundOrderToDto(InboundOrder o)
        {
            var items = (o.InboundOrderItems ?? Enumerable.Empty<InboundOrderItem>()).Select(MapInboundOrderItem).ToList();
            return new InboundOrderDto(
                o.Id,
                o.InboundRequestId,
                o.WarehouseId,
                o.SupplierId,
                o.CreatedBy,
                o.StaffId,
                o.ReferenceCode,
                o.Status,
                o.CreatedAt,
                items,
                MapSupplier(o.Supplier),
                MapWarehouse(o.Warehouse),
                MapUser(o.CreatedByNavigation),
                MapUser(o.Staff));
        }

        // --- New service methods returning DTOs and scoping by companyId ---
        public async Task<List<InboundRequestDto>> GetAllInboundRequestsAsync(int companyId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            var items = await _repo.GetAllInboundRequestsAsync(companyId);
            return items.Select(MapInboundRequestToDto).ToList();
        }

        public async Task<List<InboundOrderDto>> GetAllInboundOrdersAsync(int companyId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            var items = await _repo.GetAllInboundOrdersAsync(companyId);
            return items.Select(MapInboundOrderToDto).ToList();
        }

        public async Task<InboundRequestDto> GetInboundRequestByIdAsync(int companyId, int id)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (id <= 0) throw new ArgumentException("Invalid inbound request id.", nameof(id));
            var r = await _repo.GetInboundRequestByIdAsync(companyId, id);
            return MapInboundRequestToDto(r);
        }

        public async Task<InboundOrderDto> GetInboundOrderByIdAsync(int companyId, int id)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (id <= 0) throw new ArgumentException("Invalid inbound order id.", nameof(id));
            var o = await _repo.GetInboundOrderByIdAsync(companyId, id);
            return MapInboundOrderToDto(o);
        }


    }
}
