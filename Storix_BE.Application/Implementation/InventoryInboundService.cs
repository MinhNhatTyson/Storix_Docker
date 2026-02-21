using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
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

            var invalidLineDiscount = request.Items.FirstOrDefault(i => double.IsNaN(i.LineDiscount) || i.LineDiscount < 0 || i.LineDiscount > 100);
            if (invalidLineDiscount != null)
                throw new InvalidOperationException("Each item LineDiscount be in the interval of 0 - 100 ");

            if (request.OrderDiscount.HasValue && (double.IsNaN(request.OrderDiscount.Value) || request.OrderDiscount.Value < 0 || request.OrderDiscount.Value > 100))
                throw new InvalidOperationException("OrderDiscount must be in the range of 0 - 100");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (request.ExpectedArrivalDate.Value < today)
                throw new InvalidOperationException("ExpectedArrivalDate cannot be in the past.");

            var code = await GenerateUniqueCodeAsync();

            var inboundRequest = new InboundRequest
            {
                WarehouseId = request.WarehouseId,
                SupplierId = request.SupplierId,
                RequestedBy = request.RequestedBy,
                OrderDiscount = request.OrderDiscount,
                Note = request.Note,
                ExpectedDate = request.ExpectedArrivalDate,
                Code = code
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

            double totalPrice = 0.0;
            foreach (var item in request.Items)
            {
                var qty = item.ExpectedQuantity;
                var price = item.Price;
                var lineDiscount = item.LineDiscount;

                var effectiveUnitPrice = price - (price * (lineDiscount / 100));
                if (effectiveUnitPrice < 0) effectiveUnitPrice = 0;

                totalPrice += effectiveUnitPrice * qty;
            }

            inboundRequest.TotalPrice = totalPrice;

            if (request.OrderDiscount.HasValue)
            {
                var final = totalPrice - (totalPrice * (request.OrderDiscount.Value / 100));
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

        private async Task<string> GenerateUniqueCodeAsync()
        {
            const string prefix = "PO";
            const int digits = 6;

            var attempt = 0;
            var counter = 1;
            while (attempt < 1_000_000)
            {
                var candidate = prefix + counter.ToString($"D{digits}");
                var exists = await _repo.InboundRequestCodeExistsAsync(candidate).ConfigureAwait(false);
                if (!exists) return candidate;

                counter++;
                attempt++;
            }

            throw new InvalidOperationException("Unable to generate a unique inbound request code.");
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
                item.Price,
                item.Discount,
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
                r.Code,
                r.Note,
                r.ExpectedDate,
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
            var inboundRequest = o.InboundRequest;

            return new InboundOrderDto(
                o.Id,
                o.InboundRequestId,
                o.WarehouseId,
                o.SupplierId,
                o.CreatedBy,
                o.StaffId,
                o.ReferenceCode,
                o.Status,
                inboundRequest?.TotalPrice,
                inboundRequest?.OrderDiscount,
                inboundRequest?.FinalPrice,
                o.CreatedAt,
                inboundRequest?.Note,
                inboundRequest?.ExpectedDate,
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
        public async Task<List<InboundOrderDto>> GetInboundOrdersByStaffAsync(int companyId, int staffId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (staffId <= 0) throw new ArgumentException("Invalid staff id.", nameof(staffId));

            var items = await _repo.GetInboundOrdersByStaffAsync(companyId, staffId);
            return items.Select(MapInboundOrderToDto).ToList();
        }
        public async Task<InboundRequestExportDto> GetInboundRequestForExportAsync(int inboundRequestId)
        {
            if (inboundRequestId <= 0) throw new ArgumentException("Invalid inbound request id.", nameof(inboundRequestId));
            var dto = await _repo.GetInboundRequestForExportAsync(inboundRequestId);
            if (dto == null) throw new InvalidOperationException($"InboundRequest with id {inboundRequestId} not found.");
            return dto;
        }

        public async Task<InboundOrderExportDto> GetInboundOrderForExportAsync(int inboundOrderId)
        {
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inbound order id.", nameof(inboundOrderId));
            var dto = await _repo.GetInboundOrderForExportAsync(inboundOrderId);
            if (dto == null) throw new InvalidOperationException($"InboundOrder with id {inboundOrderId} not found.");
            return dto;
        }

        public byte[] ExportInboundRequestToCsv(InboundRequestExportDto request)
        {
            return _repo.ExportInboundRequestToCsv(request);
        }

        public byte[] ExportInboundRequestToExcel(InboundRequestExportDto request)
        {
            return _repo.ExportInboundRequestToExcel(request);
        }

        public byte[] ExportInboundOrderToCsv(InboundOrderExportDto order)
        {
            return _repo.ExportInboundOrderToCsv(order);
        }

        public byte[] ExportInboundOrderToExcel(InboundOrderExportDto order)
        {
            return _repo.ExportInboundOrderToExcel(order);
        }
    }
}
