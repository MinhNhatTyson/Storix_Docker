using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace Storix_BE.Service.Implementation
{
    public class InventoryInboundService : IInventoryInboundService
    {
        private readonly IInventoryInboundRepository _repo;
        private readonly INotificationService _notificationService;
        private readonly IActivityLogRepository _activityLogRepo;

        public InventoryInboundService(
            IInventoryInboundRepository repo,
            INotificationService notificationService,
            IActivityLogRepository activityLogRepo)
        {
            _repo = repo;
            _notificationService = notificationService;
            _activityLogRepo = activityLogRepo;
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

            if (string.IsNullOrWhiteSpace(request.Note))
                throw new InvalidOperationException("Note is required.");

            if (!request.ExpectedArrivalDate.HasValue)
                throw new InvalidOperationException("ExpectedArrivalDate is required.");
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
            // add activity log entry
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = request.RequestedBy,
                Action = "Create Inbound Request",
                Entity = "InboundRequest",
                EntityId = inboundRequest.Id,
                Timestamp = now
            }).ConfigureAwait(false);
            return createdRequest;
        }
        public async Task<InboundRequest> ImportInboundRequestAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("Excel file is empty.");

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            // HEADER (Row 2)
            var header = worksheet.Row(2);

            // Fix: Use List<CreateInboundOrderItemRequest> for Items so we can add items
            var items = new List<CreateInboundOrderItemRequest>();

            var request = new CreateInboundRequestRequest(
                header.Cell(1).GetValue<int>(),
                header.Cell(2).GetValue<int>(),
                header.Cell(3).GetValue<int>(),
                header.Cell(4).GetValue<string>(),
                DateOnly.FromDateTime(header.Cell(5).GetDateTime()),
                header.Cell(6).GetValue<double>(),
                items
            );

            // ITEMS
            int itemHeaderRow = 4;
            int row = itemHeaderRow + 1;

            while (!worksheet.Row(row).IsEmpty())
            {
                var item = new CreateInboundOrderItemRequest(
                    worksheet.Row(row).Cell(1).GetValue<int>(),
                    worksheet.Row(row).Cell(2).GetValue<int>(),
                    worksheet.Row(row).Cell(3).GetValue<double>(),
                    worksheet.Row(row).Cell(4).GetValue<double>()
                );

                items.Add(item); // Fix: Add to the List, not to IEnumerable

                row++;
            }

            // IMPORTANT: reuse your existing logic
            return await CreateInboundRequestAsync(request);
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

            var inbound = await _repo.UpdateInventoryInboundTicketRequestStatus(ticketRequestId, approverId, status);
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            // log approval/change (status may be "Approved", "Rejected", etc.)
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = approverId,
                Action = $"{status} Inbound Request",
                Entity = "InboundRequest",
                EntityId = inbound.Id,
                Timestamp = now
            }).ConfigureAwait(false);
            // Send notification to managers when approved
            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                var companyId = inbound.RequestedByNavigation?.CompanyId ?? inbound.RequestedByNavigation?.CompanyId;
                if (companyId.HasValue && companyId.Value > 0)
                {
                    var title = "Inbound request approved";
                    var message = $"Inbound request '{inbound.Code}' has been approved.";
                    await _notificationService.SendNotificationToManagersAsync(
                        companyId.Value,
                        title,
                        message,
                        type: "InboundRequest",
                        category: "Inbound",
                        referenceType: "InboundRequest",
                        referenceId: inbound.Id,
                        createdByUserId: approverId
                    ).ConfigureAwait(false);
                }
            }
            // Send notification to managers when approved
            if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                var companyId = inbound.RequestedByNavigation?.CompanyId ?? inbound.RequestedByNavigation?.CompanyId;
                if (companyId.HasValue && companyId.Value > 0)
                {
                    var title = "Inbound request rejected";
                    var message = $"Inbound request '{inbound.Code}' has been rejected.";
                    await _notificationService.SendNotificationToManagersAsync(
                        companyId.Value,
                        title,
                        message,
                        type: "InboundRequest",
                        category: "Inbound",
                        referenceType: "InboundRequest",
                        referenceId: inbound.Id,
                        createdByUserId: approverId
                    ).ConfigureAwait(false);
                }
            }

            return inbound;
        }

        public async Task<InboundOrder> CreateTicketFromRequestAsync(int inboundRequestId, int createdBy, int? staffId)
        {
            if (inboundRequestId <= 0) throw new ArgumentException("Invalid inboundRequestId.", nameof(inboundRequestId));
            if (createdBy <= 0) throw new ArgumentException("Invalid createdBy.", nameof(createdBy));
            // staffId may be null (optional)

            var ticket = await _repo.CreateInboundOrderFromRequestAsync(inboundRequestId, createdBy, staffId);
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = createdBy,
                Action = "Create Inbound Order",
                Entity = "InboundOrder",
                EntityId = ticket.Id,
                Timestamp = now
            }).ConfigureAwait(false);
            if (staffId.HasValue && staffId.Value > 0)
            {
                try
                {
                    var title = "New inbound ticket assigned";
                    var message = $"Inbound ticket #{ticket.Id} has been created and assigned to you.";
                    await _notificationService.SendNotificationToUserAsync(
                        staffId.Value,
                        title,
                        message,
                        type: "InboundOrder",
                        category: "Inbound",
                        referenceType: "InboundOrder",
                        referenceId: ticket.Id,
                        createdByUserId: createdBy
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send notification to staff {staffId}: {ex.Message}");
                }
            }

            return ticket;
        }

        public async Task<InboundOrder> UpdateTicketItemsAsync(int inboundOrderId, IEnumerable<UpdateInboundOrderItemRequest> items)
        {
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inboundOrderId.", nameof(inboundOrderId));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (!items.Any()) throw new InvalidOperationException("Items payload cannot be empty.");

            // Map domain inbound order items (existing shape)
            var domainItems = items.Select(i => new InboundOrderItem
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ExpectedQuantity = i.ExpectedQuantity,
                ReceivedQuantity = i.ReceivedQuantity
            }).ToList();

            // Map location placements to repository DTOs
            var placements = items
                .Where(i => i.Locations != null)
                .SelectMany(i => i.Locations!.Select(loc => new IInventoryInboundRepository.InventoryPlacementDto(
                    i.Id,
                    i.ProductId,
                    loc.Quantity,
                    loc.BinId
                )))
                .ToList();

            var updated = await _repo.UpdateInboundOrderItemsAsync(inboundOrderId, domainItems, placements).ConfigureAwait(false);

            // Notify managers when staff updated/finished inbound ticket (best-effort)
            try
            {
                var companyId = updated.Warehouse?.CompanyId ?? updated.InboundRequest?.RequestedByNavigation?.CompanyId;
                if (companyId.HasValue && companyId.Value > 0)
                {
                    var title = "Inbound ticket updated by staff";
                    var message = $"Inbound ticket #{updated.Id} has new updates from staff.";
                    await _notificationService.SendNotificationToManagersAsync(
                        companyId.Value,
                        title,
                        message,
                        type: "InboundOrder",
                        category: "Inbound",
                        referenceType: "InboundOrder",
                        referenceId: updated.Id,
                        createdByUserId: updated.StaffId
                    ).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to notify managers for inbound order {inboundOrderId}: {ex.Message}");
            }

            return updated;
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

        private static InboundOrderItemDto MapInboundOrderItem(InboundOrderItem item, IEnumerable<InboundOrderItemPlacementDto>? placements = null)
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
                p?.Description,
                p?.Image,
                placements ?? Enumerable.Empty<InboundOrderItemPlacementDto>());
        }

        private static InboundRequestDto MapInboundRequestToDto(InboundRequest r)
        {
            var items = (r.InboundOrderItems ?? Enumerable.Empty<InboundOrderItem>())
                .Select(i => MapInboundOrderItem(i))
                .ToList();
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
            // Build placements map if FIFO batches present
            var placementsByItem = new Dictionary<int, List<InboundOrderItemPlacementDto>>();

            if (o.InventoryBatches != null && o.InventoryBatches.Any())
            {
                foreach (var batch in o.InventoryBatches)
                {
                    // Batch should reference InboundOrderItemId
                    var inboundItemId = batch.InboundOrderItemId;
                    if (inboundItemId <= 0) continue;

                    foreach (var bl in (batch.BatchLocations ?? Enumerable.Empty<InventoryBatchLocation>()))
                    {
                        var bin = bl.Bin;
                        var shelf = bin?.Level?.Shelf;
                        var zone = shelf?.Zone;

                        var placement = new InboundOrderItemPlacementDto(
                            BatchId: batch.Id,
                            BinId: bl.BinId,
                            BinCode: bin?.Code,
                            BinIdCode: bin?.IdCode,
                            ShelfId: shelf?.Id,
                            ShelfCode: shelf?.Code,
                            ZoneId: zone?.Id,
                            ZoneCode: zone?.Code,
                            Quantity: bl.Quantity,
                            InboundDate: batch.InboundDate,
                            BatchUnitCost: batch.EffectiveUnitCost);

                        if (!placementsByItem.TryGetValue(inboundItemId, out var list))
                        {
                            list = new List<InboundOrderItemPlacementDto>();
                            placementsByItem[inboundItemId] = list;
                        }

                        list.Add(placement);
                    }
                }
            }

            // Attach placements per item when mapping items
            var items = (o.InboundOrderItems ?? Enumerable.Empty<InboundOrderItem>())
                .Select(item =>
                {
                    placementsByItem.TryGetValue(item.Id, out var pls);
                    return MapInboundOrderItem(item, pls);
                })
                .ToList();

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
        public async Task<List<InboundRequestDto>> GetInboundRequestsByWarehouseAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            var items = await _repo.GetInboundRequestsByWarehouseAsync(companyId, warehouseId).ConfigureAwait(false);
            return items.Select(MapInboundRequestToDto).ToList();
        }

        public async Task<List<InboundOrderDto>> GetInboundOrdersByWarehouseAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            var items = await _repo.GetInboundOrdersByWarehouseAsync(companyId, warehouseId).ConfigureAwait(false);
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
        public async Task AddStorageRecommendationsAsync(IInventoryInboundService.AddStorageRecommendationsRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var items = request.StorageRecommendations?.ToList();
            if (items == null || !items.Any()) throw new InvalidOperationException("storageRecommendations payload is required and cannot be empty.");

            // Basic validation
            foreach (var it in items)
            {
                if (it.InboundProductId <= 0) throw new ArgumentException("Each storage recommendation item must contain a valid inboundProductId.");
                if (it.Recommendations == null || !it.Recommendations.Any()) throw new ArgumentException("At least one Recommendation is required for each storage recommendation item.");
                foreach (var rec in it.Recommendations)
                {
                    if (rec == null) throw new ArgumentException("Recommendation entry cannot be null.");
                    if (string.IsNullOrWhiteSpace(rec.BinId)) throw new ArgumentException("Recommendation.BinId (ShelfLevelBin.IdCode) is required.");
                    // Quantity is optional (int?), validate if provided and negative
                    if (rec.Quantity.HasValue && rec.Quantity.Value < 0) throw new ArgumentException("Recommendation.Quantity cannot be negative.");
                }
            }

            // Map to repository DTOs (flatten: one repo dto per recommendation)
            var repoDtos = new List<IInventoryInboundRepository.StorageRecommendationCreateDto>();
            foreach (var item in items)
            {
                foreach (var rec in item.Recommendations)
                {
                    var recDto = new IInventoryInboundRepository.RecommendationCreateDto(
                        rec.BinId,
                        rec.Path,
                        rec.DistanceInfo,
                        rec.Quantity
                    );

                    repoDtos.Add(new IInventoryInboundRepository.StorageRecommendationCreateDto(
                        item.InboundProductId,
                        recDto,
                        item.Reason
                    ));
                }
            }

            await _repo.AddStorageRecommendationsAsync(repoDtos).ConfigureAwait(false);
        }

        // Modified mapping when returning recommendations to include Recommendation.Quantity
        public async Task<List<InboundItemRecommendationsDto>> GetStorageRecommendationsByInboundOrderIdAsync(int inboundOrderId)
        {
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inbound order id.", nameof(inboundOrderId));

            var items = await _repo.GetInboundOrderItemsWithRecommendationsAsync(inboundOrderId).ConfigureAwait(false);

            var result = items.Select(item =>
            {
                var recs = (item.StorageRecommendations ?? Enumerable.Empty<StorageRecommendation>())
                    .Select(sr =>
                    {
                        var r = sr.Recommendation;
                        var bin = r?.Bin;
                        return new StorageRecommendationDto(
                            sr.Id,
                            sr.RecommendationId,
                            r?.BinId,
                            bin?.IdCode,
                            r?.Path,
                            r?.DistanceInfo,
                            r?.Quantity,
                            sr.Reason,
                            sr.CreatedAt);
                    }).ToList();

                return new InboundItemRecommendationsDto(
                    item.Id,
                    item.ProductId,
                    item.Product?.Sku,
                    item.Product?.Name,
                    recs);
            }).ToList();

            return result;
        }
        public async Task<InboundOrder> AssignStaffToInboundOrderAsync(int companyId, int inboundOrderId, int managerUserId, int staffUserId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inboundOrderId.", nameof(inboundOrderId));
            if (managerUserId <= 0) throw new ArgumentException("Invalid managerUserId.", nameof(managerUserId));
            if (staffUserId <= 0) throw new ArgumentException("Invalid staffUserId.", nameof(staffUserId));

            var updated = await _repo.AssignStaffToInboundOrderAsync(companyId, inboundOrderId, managerUserId, staffUserId)
                .ConfigureAwait(false);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = managerUserId,
                Action = "Assign staff to inbound order",
                Entity = "InboundOrder",
                EntityId = updated.Id,
                Timestamp = now
            }).ConfigureAwait(false);

            try
            {
                var title = "Assigned to inbound ticket";
                var message = $"You were assigned to inbound ticket #{updated.Id}.";
                await _notificationService.SendNotificationToUserAsync(
                    staffUserId,
                    title,
                    message,
                    type: "InboundOrder",
                    category: "Inbound",
                    referenceType: "InboundOrder",
                    referenceId: updated.Id,
                    createdByUserId: managerUserId
                ).ConfigureAwait(false);
            }
            catch
            {
                // best-effort notify; swallow exceptions
            }

            return updated;
        }
    }
}
