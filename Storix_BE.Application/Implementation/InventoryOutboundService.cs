using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static Storix_BE.Service.Interfaces.IInventoryOutboundService;

namespace Storix_BE.Service.Implementation
{
    public class InventoryOutboundService : IInventoryOutboundService
    {
        private readonly IInventoryOutboundRepository _repo;
        private readonly INotificationService _notificationService;
        private readonly IUserRepository _userRepo;

        public InventoryOutboundService(IInventoryOutboundRepository repo, INotificationService notificationService, IUserRepository userRepo)
        {
            _repo = repo;
            _notificationService = notificationService;
            _userRepo = userRepo;
        }
        public async Task<List<FifoPickingSuggestionDto>> GetFifoPickingSuggestionsAsync(int companyId, int outboundOrderId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (outboundOrderId <= 0)
                throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));

            var order = await _repo.GetOutboundOrderByIdAsync(companyId, outboundOrderId)
                .ConfigureAwait(false);

            if (!order.WarehouseId.HasValue)
                throw new InvalidOperationException(
                    "OutboundOrder must have a valid WarehouseId.");

            var results = new List<FifoPickingSuggestionDto>();

            var itemGroups = order.OutboundOrderItems
                .Where(i => i.ProductId.HasValue && (i.Quantity ?? 0) > 0)
                .GroupBy(i => new { i.ProductId, i.Id })
                .Select(g => new
                {
                    OutboundOrderItemId = g.Key.Id,
                    ProductId = g.Key.ProductId!.Value,
                    ProductName = g.First().Product?.Name,
                    RequiredQuantity = g.Sum(x => x.Quantity ?? 0)
                })
                .ToList();

            foreach (var item in itemGroups)
            {
                var rawSuggestions = await _repo.GetFifoSuggestedLocationsAsync(
                    order.WarehouseId.Value,
                    item.ProductId,
                    item.RequiredQuantity).ConfigureAwait(false);

                var totalSuggested = rawSuggestions.Sum(s => s.SuggestedPickQty);
                var isFullyCoverable = totalSuggested >= item.RequiredQuantity;
                var remainingQuantity = Math.Max(0, item.RequiredQuantity - totalSuggested);

                results.Add(new FifoPickingSuggestionDto(
                    OutboundOrderItemId: item.OutboundOrderItemId,
                    ProductId: item.ProductId,
                    ProductName: item.ProductName,
                    RequiredQuantity: item.RequiredQuantity,
                    IsFullyCoverable: isFullyCoverable,
                    TotalAvailableQuantity: totalSuggested,
                    RemainingQuantity: remainingQuantity,
                    Suggestions: rawSuggestions.Select(s => new FifoBinSuggestionItemDto(
                        BatchId: s.BatchId,
                        InboundDate: s.InboundDate,
                        EffectiveUnitCost: s.EffectiveUnitCost,
                        BinId: s.BinId,
                        BinIdCode: s.BinIdCode,
                        BinCode: s.BinCode,
                        ShelfId: s.ShelfId,
                        ShelfCode: s.ShelfCode,
                        ZoneId: s.ZoneId,
                        AvailableInBin: s.AvailableInBin,
                        SuggestedPickQty: s.SuggestedPickQty
                    )).ToList()
                ));
            }

            return results;
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
                RequestedBy = request.RequestedBy,
                Reason = request.Reason
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

        public async Task<IReadOnlyList<WarehouseInventoryItemDto>> GetWarehouseInventoryAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));

            var items = await _repo.GetWarehouseInventoryAsync(companyId, warehouseId).ConfigureAwait(false);

            return items.Select(x => new WarehouseInventoryItemDto(
                x.InventoryId,
                x.WarehouseId,
                x.ProductId,
                x.ProductName,
                x.ProductSku,
                x.ProductImage,
                x.Quantity,
                x.ReservedQuantity,
                x.Quantity - x.ReservedQuantity,
                x.LastUpdated,
                x.LastCountedAt,
                x.Locations.Select(l => new WarehouseLocationDto(
                    l.ZoneId,
                    l.ZoneCode,
                    l.ShelfId,
                    l.ShelfCode,
                    l.Quantity,
                    l.Bins.Select(b => new WarehouseBinDto(
                        b.BinId,
                        b.BinCode,
                        b.BinIdCode,
                        b.OccupancyPercentage)).ToList())).ToList())).ToList();
        }

        public async Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status)
        {
            if (requestId <= 0) throw new ArgumentException("Invalid request id.", nameof(requestId));
            if (approverId <= 0) throw new ArgumentException("Invalid approver id.", nameof(approverId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var outbound = await _repo.UpdateOutboundRequestStatusAsync(requestId, approverId, status).ConfigureAwait(false);

            // Notify managers when approved/rejected by admin (similar to inbound flow)
            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // try resolve company via request.Warehouse -> repository may not include navigation; fallback to approver's company
                    int? companyId = null;
                    if (outbound.Warehouse != null) companyId = outbound.Warehouse.CompanyId;
                    if (!companyId.HasValue)
                    {
                        var user = await _userRepo.GetUserByIdWithRoleAsync(approverId).ConfigureAwait(false);
                        companyId = user?.CompanyId;
                    }

                    if (companyId.HasValue && companyId.Value > 0)
                    {
                        var title = $"Outbound request {status.ToLowerInvariant()}";
                        var message = $"Outbound request #{outbound.Id} has been {status.ToLowerInvariant()}.";
                        await _notificationService.SendNotificationToManagersAsync(
                            companyId.Value,
                            title,
                            message,
                            type: "OutboundRequest",
                            category: "Outbound",
                            referenceType: "OutboundRequest",
                            referenceId: outbound.Id,
                            createdByUserId: approverId
                        ).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to notify managers for outbound request {outbound.Id} status change: {ex.Message}");
                }
            }

            return outbound;
        }

        public async Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note, string? pricingMethod = "LastPurchasePrice")
        {
            if (outboundRequestId <= 0) throw new ArgumentException("Invalid outboundRequestId.", nameof(outboundRequestId));
            if (createdBy <= 0) throw new ArgumentException("Invalid createdBy.", nameof(createdBy));

            var method = string.IsNullOrWhiteSpace(pricingMethod) ? "LastPurchasePrice" : pricingMethod.Trim();
            if (!string.Equals(method, "LastPurchasePrice", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(method, "SpecificIdentification", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid pricing method. Allowed: LastPurchasePrice, SpecificIdentification.", nameof(pricingMethod));
            }

            var outboundOrder = await _repo.CreateOutboundOrderFromRequestAsync(outboundRequestId, createdBy, staffId, note, pricingMethod).ConfigureAwait(false);

            // notify assigned staff (if manager created the ticket and staff assigned)
            if (staffId.HasValue && staffId.Value > 0)
            {
                try
                {
                    var title = "New outbound ticket assigned";
                    var message = $"Outbound ticket #{outboundOrder.Id} has been created and assigned to you.";
                    await _notificationService.SendNotificationToUserAsync(
                        staffId.Value,
                        title,
                        message,
                        type: "OutboundOrder",
                        category: "Outbound",
                        referenceType: "OutboundOrder",
                        referenceId: outboundOrder.Id,
                        createdByUserId: createdBy
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // best-effort: do not fail ticket creation due to notification issues
                    Console.WriteLine($"Failed to send notification to staff {staffId}: {ex.Message}");
                }
            }

            return outboundOrder;
        }

        public async Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<UpdateOutboundOrderItemRequest> items)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (!items.Any()) throw new InvalidOperationException("Items payload cannot be empty.");

            var domainItems = items.Select(i =>
            {
                var expected = i.ExpectedQuantity;
                var received = i.ReceivedQuantity;
                var quantity = received ?? expected;

                return new OutboundOrderItem
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    ExpectedQuantity = expected,
                    ReceivedQuantity = received,
                    Quantity = quantity
                };
            }).ToList();

            var placements = items
                .Where(i => i.Locations != null)
                .SelectMany(i => i.Locations!.Select(loc => new IInventoryOutboundRepository.InventoryPlacementDto(
                    i.Id,
                    i.ProductId,
                    loc.Quantity,
                    loc.BinId
                )))
                .ToList();

            return await _repo.UpdateOutboundOrderItemsAsync(outboundOrderId, domainItems, placements).ConfigureAwait(false);
        }

        public async Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (performedBy <= 0) throw new ArgumentException("Invalid performedBy.", nameof(performedBy));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            return await _repo.UpdateOutboundOrderStatusAsync(outboundOrderId, performedBy, status);
        }

        public async Task<List<FifoBatchAllocationDto>> GetFifoBatchAllocationsByItemAsync(int companyId, int outboundOrderId, int outboundOrderItemId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (outboundOrderItemId <= 0) throw new ArgumentException("Invalid outboundOrderItemId.", nameof(outboundOrderItemId));

            var order = await _repo.GetOutboundOrderByIdAsync(companyId, outboundOrderId).ConfigureAwait(false);
            var item = order.OutboundOrderItems.FirstOrDefault(x => x.Id == outboundOrderItemId)
                ?? throw new InvalidOperationException($"OutboundOrderItem with id {outboundOrderItemId} not found.");

            if (!item.ProductId.HasValue)
                return new List<FifoBatchAllocationDto>();

            var suggestions = await _repo.GetFifoSuggestedLocationsAsync(
                order.WarehouseId!.Value,
                item.ProductId.Value,
                item.Quantity ?? item.ReceivedQuantity ?? item.ExpectedQuantity ?? 0).ConfigureAwait(false);

            var remaining = item.Quantity ?? item.ReceivedQuantity ?? item.ExpectedQuantity ?? 0;
            var result = new List<FifoBatchAllocationDto>();

            foreach (var s in suggestions)
            {
                if (remaining <= 0) break;
                var pickQty = Math.Min(remaining, s.SuggestedPickQty);
                remaining -= pickQty;

                result.Add(new FifoBatchAllocationDto(
                    OutboundOrderItemId: item.Id,
                    ProductId: item.ProductId.Value,
                    BatchId: s.BatchId,
                    InboundDate: s.InboundDate,
                    RemainingQuantity: s.AvailableInBin,
                    BatchRemainingAfterPick: Math.Max(0, s.AvailableInBin - pickQty),
                    EffectiveUnitCost: s.EffectiveUnitCost,
                    BinId: s.BinId,
                    BinIdCode: s.BinIdCode,
                    BinCode: s.BinCode,
                    ShelfId: s.ShelfId,
                    ShelfCode: s.ShelfCode,
                    ZoneId: s.ZoneId,
                    AvailableInBin: s.AvailableInBin,
                    SuggestedPickQty: pickQty));
            }

            return result;
        }


        private static OutboundWarehouseDto? MapWarehouse(Warehouse? w)
        {
            if (w == null) return null;
            return new OutboundWarehouseDto(w.Id, w.Name);
        }

        private static OutboundUserDto? MapUser(User? u)
        {
            if (u == null) return null;
            return new OutboundUserDto(u.Id, u.FullName, u.Email, u.Phone);
        }

        private static OutboundOrderItemAvailableLocationDetailsDto EmptyAvailableLocations(int requiredQuantity = 0)
            => new(
                requiredQuantity,
                Array.Empty<OutboundAvailableShelfDto>(),
                Array.Empty<OutboundAvailableBinDto>());

        private static OutboundOrderItemDto MapOutboundOrderItem(
            OutboundOrderItem item,
            OutboundOrderItemAvailableLocationDetailsDto? availableLocations = null,
            IReadOnlyList<OutboundOrderItemSelectedLocationDto>? selectedPickLocations = null,
            IReadOnlyList<FifoBinSuggestionItemDto>? fifoPickingSuggestion = null)
        {
            var p = item.Product;
            var displayPrice = item.CostPrice ?? item.Price;
            // Backward compatible: many UIs bind to Price; if sale Price is null, return the computed cost.
            var priceForUi = item.Price ?? item.CostPrice;
            var requiredQuantity = item.Quantity ?? item.ReceivedQuantity ?? item.ExpectedQuantity ?? 0;
            return new OutboundOrderItemDto(
                item.Id,
                item.ProductId,
                p?.Name,
                p?.Sku,
                item.ExpectedQuantity,
                item.ReceivedQuantity,
                item.Quantity,
                priceForUi,
                item.CostPrice,
                item.PricingMethod,
                displayPrice,
                p?.Image,
                p?.Description,
                availableLocations ?? EmptyAvailableLocations(requiredQuantity),
                selectedPickLocations ?? Array.Empty<OutboundOrderItemSelectedLocationDto>(),
                fifoPickingSuggestion ?? Array.Empty<FifoBinSuggestionItemDto>());
        }

        private static OutboundRequestDto MapOutboundRequestToDto(OutboundRequest r)
        {
            var items = (r.OutboundOrderItems ?? Enumerable.Empty<OutboundOrderItem>())
                .Select(item => MapOutboundOrderItem(item))
                .ToList();
            return new OutboundRequestDto(
                r.Id,
                r.WarehouseId,
                r.RequestedBy,
                r.ApprovedBy,
                r.Destination,
                r.Reason,
                r.ReferenceCode,
                r.Status,
                r.TotalPrice,
                r.CreatedAt,
                r.ApprovedAt,
                items,
                MapWarehouse(r.Warehouse),
                MapUser(r.RequestedByNavigation),
                MapUser(r.ApprovedByNavigation));
        }

        private static OutboundOrderDto MapOutboundOrderToDto(
            OutboundOrder o,
            IReadOnlyDictionary<int, OutboundOrderItemAvailableLocationDetailsDto>? availableLocationsByItemId = null,
            IReadOnlyDictionary<int, IReadOnlyList<OutboundOrderItemSelectedLocationDto>>? selectedPickLocationsByItemId = null,
            IReadOnlyDictionary<int, IReadOnlyList<FifoBinSuggestionItemDto>>? fifoPickingSuggestionsByItemId = null)
        {
            var items = (o.OutboundOrderItems ?? Enumerable.Empty<OutboundOrderItem>())
                .Select(item =>
                {
                    var availableLocations = availableLocationsByItemId != null
                        && availableLocationsByItemId.TryGetValue(item.Id, out var available)
                            ? available
                            : null;

                    var selectedPickLocations = selectedPickLocationsByItemId != null
                        && selectedPickLocationsByItemId.TryGetValue(item.Id, out var selected)
                            ? selected
                            : null;

                    var fifoPickingSuggestion = fifoPickingSuggestionsByItemId != null
                        && fifoPickingSuggestionsByItemId.TryGetValue(item.Id, out var fifo)
                            ? fifo
                            : null;

                    return MapOutboundOrderItem(
                        item,
                        availableLocations,
                        selectedPickLocations,
                        fifoPickingSuggestion);
                })
                .ToList();

            return new OutboundOrderDto(
                o.Id,
                o.WarehouseId,
                o.CreatedBy,
                o.StaffId,
                o.Destination,
                o.Status,
                o.Note,
                o.CreatedAt,
                items,
                MapWarehouse(o.Warehouse),
                MapUser(o.CreatedByNavigation),
                MapUser(o.Staff));
        }

        public async Task<List<OutboundRequestDto>> GetAllOutboundRequestsAsync(int companyId, int? warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            var items = await _repo.GetAllOutboundRequestsAsync(companyId, warehouseId);
            return items.Select(MapOutboundRequestToDto).ToList();
        }

        public async Task<List<OutboundRequestDto>> GetOutboundRequestsByWarehouseIdAsync(int warehouseId)
        {
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));
            var items = await _repo.GetOutboundRequestsByWarehouseIdAsync(warehouseId);
            return items.Select(MapOutboundRequestToDto).ToList();
        }

        public async Task<OutboundRequestDto> GetOutboundRequestByIdAsync(int companyId, int id)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (id <= 0) throw new ArgumentException("Invalid outbound request id.", nameof(id));
            var request = await _repo.GetOutboundRequestByIdAsync(companyId, id);
            return MapOutboundRequestToDto(request);
        }

        public async Task<List<OutboundOrderDto>> GetAllOutboundOrdersAsync(int companyId, int? warehouseId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            var items = await _repo.GetAllOutboundOrdersAsync(companyId, warehouseId);
            return items.Select(item => MapOutboundOrderToDto(item)).ToList();
        }

        public async Task<List<OutboundOrderDto>> GetOutboundOrdersByWarehouseIdAsync(int warehouseId)
        {
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse id.", nameof(warehouseId));
            var items = await _repo.GetOutboundOrdersByWarehouseIdAsync(warehouseId);
            return items.Select(item => MapOutboundOrderToDto(item)).ToList();
        }

        public async Task<OutboundOrderDto> GetOutboundOrderByIdAsync(int companyId, int id)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (id <= 0) throw new ArgumentException("Invalid outbound order id.", nameof(id));
            var order = await _repo.GetOutboundOrderByIdAsync(companyId, id).ConfigureAwait(false);

            var selectedLocations = await GetOutboundOrderItemSelectedLocationsAsync(id).ConfigureAwait(false);

            var availableLocations = order.WarehouseId.HasValue && order.WarehouseId.Value > 0
                ? await GetOutboundOrderItemAvailableLocationsAsync(id).ConfigureAwait(false)
                : Array.Empty<OutboundOrderItemAvailableLocationsDto>();

            var fifoSuggestions = order.WarehouseId.HasValue && order.WarehouseId.Value > 0
                ? await GetFifoPickingSuggestionsAsync(companyId, id).ConfigureAwait(false)
                : new List<FifoPickingSuggestionDto>();

            var availableLocationsByItemId = availableLocations.ToDictionary(
                x => x.OutboundOrderItemId,
                x => new OutboundOrderItemAvailableLocationDetailsDto(
                    x.RequiredQuantity,
                    x.AvailableShelves,
                    x.AvailableBins));

            var selectedPickLocationsByItemId = selectedLocations
                .GroupBy(x => x.OutboundOrderItemId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<OutboundOrderItemSelectedLocationDto>)g.ToList());

            var fifoPickingSuggestionsByItemId = fifoSuggestions.ToDictionary(
                x => x.OutboundOrderItemId,
                x => (IReadOnlyList<FifoBinSuggestionItemDto>)x.Suggestions);

            return MapOutboundOrderToDto(
                order,
                availableLocationsByItemId,
                selectedPickLocationsByItemId,
                fifoPickingSuggestionsByItemId);
        }
        public async Task<List<OutboundOrderDto>> GetOutboundOrdersByStaffAsync(int companyId, int staffId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (staffId <= 0) throw new ArgumentException("Invalid staff id.", nameof(staffId));

            var items = await _repo.GetOutboundOrdersByStaffAsync(companyId, staffId);
            return items.Select(item => MapOutboundOrderToDto(item)).ToList();
        }

        public async Task<List<OutboundHistoryProductResponseDto>> GetOutboundHistoryAsync(int companyId, IEnumerable<int> productIds, int? warehouseId, DateTime from, DateTime to)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (productIds == null) throw new ArgumentNullException(nameof(productIds));
            if (to < from) throw new ArgumentException("DateTo must be >= DateFrom.");

            var data = await _repo.GetOutboundHistoryAsync(companyId, productIds, warehouseId, from, to).ConfigureAwait(false);
            return data.Select(x => new OutboundHistoryProductResponseDto(
                x.ProductId,
                x.ProductName,
                x.CurrentStock,
                x.OutboundInfo.Select(p => new OutboundHistoryPointResponseDto(
                    p.Date,
                    p.Quantity)).ToList()
            )).ToList();
        }

        public async Task<IReadOnlyList<OutboundOrderItemAvailableLocationsDto>> GetOutboundOrderItemAvailableLocationsAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            var data = await _repo.GetOutboundOrderItemAvailableLocationsAsync(outboundOrderId).ConfigureAwait(false);

            return data.Select(x => new OutboundOrderItemAvailableLocationsDto(
                x.OutboundOrderItemId,
                x.ProductId,
                x.ProductName,
                x.RequiredQuantity,
                x.AvailableShelves.Select(s => new OutboundAvailableShelfDto(
                    s.ShelfId,
                    s.ShelfCode,
                    s.ShelfIdCode,
                    s.ZoneId,
                    s.WarehouseId,
                    s.AvailableQuantity)).ToList(),
                x.AvailableBins.Select(b => new OutboundAvailableBinDto(
                    b.BinId,
                    b.BinCode,
                    b.BinIdCode,
                    b.LevelId,
                    b.ShelfId,
                    b.InventoryId,
                    b.OccupancyPercentage,
                    b.Width,
                    b.Height,
                    b.Length)).ToList()
            )).ToList();
        }

        public async Task<IReadOnlyList<OutboundOrderItemSelectedLocationDto>> GetOutboundOrderItemSelectedLocationsAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            var data = await _repo.GetOutboundOrderItemSelectedLocationsAsync(outboundOrderId).ConfigureAwait(false);

            return data.Select(x => new OutboundOrderItemSelectedLocationDto(
                x.OutboundOrderItemId,
                x.ProductId,
                x.ProductName,
                x.ProductSku,
                x.ZoneId,
                x.ZoneCode,
                x.ShelfId,
                x.ShelfCode,
                x.BinId,
                x.BinCode,
                x.BinIdCode,
                x.BatchId,
                x.InboundDate,
                x.BatchUnitCost,
                x.OutboundItemPrice,
                x.OutboundItemCostPrice,
                x.PricingMethod,
                x.Quantity,
                x.Timestamp)).ToList();
        }

        public async Task<OutboundIssueDto> CreateOutboundIssueAsync(int outboundOrderId, CreateOutboundIssueRequest request)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.ReportedBy <= 0) throw new ArgumentException("Invalid reportedBy.", nameof(request.ReportedBy));
            if (request.OutboundOrderItemId <= 0) throw new ArgumentException("Invalid outboundOrderItemId.", nameof(request.OutboundOrderItemId));
            if (request.IssueQuantity <= 0) throw new ArgumentException("IssueQuantity must be > 0.", nameof(request.IssueQuantity));
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Reason is required.", nameof(request.Reason));

            var dto = await _repo.CreateOutboundIssueAsync(
                outboundOrderId,
                request.ReportedBy,
                request.OutboundOrderItemId,
                request.IssueQuantity,
                request.Reason,
                request.Note,
                request.ImageUrl).ConfigureAwait(false);

            return new OutboundIssueDto(
                dto.IssueId,
                dto.OutboundOrderId,
                dto.OutboundOrderItemId,
                dto.ProductId,
                dto.IssueQuantity,
                dto.Reason,
                dto.Note,
                dto.ImageUrl,
                dto.ReportedBy,
                dto.ReportedAt,
                dto.UpdatedBy,
                dto.UpdatedAt);
        }

        public async Task<OutboundIssueDto> UpdateOutboundIssueAsync(int outboundOrderId, int issueId, UpdateOutboundIssueRequest request)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (issueId <= 0) throw new ArgumentException("Invalid issueId.", nameof(issueId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.UpdatedBy <= 0) throw new ArgumentException("Invalid updatedBy.", nameof(request.UpdatedBy));

            var dto = await _repo.UpdateOutboundIssueAsync(
                outboundOrderId,
                issueId,
                request.UpdatedBy,
                request.OutboundOrderItemId,
                request.IssueQuantity,
                request.Reason,
                request.Note,
                request.ImageUrl).ConfigureAwait(false);

            return new OutboundIssueDto(
                dto.IssueId,
                dto.OutboundOrderId,
                dto.OutboundOrderItemId,
                dto.ProductId,
                dto.IssueQuantity,
                dto.Reason,
                dto.Note,
                dto.ImageUrl,
                dto.ReportedBy,
                dto.ReportedAt,
                dto.UpdatedBy,
                dto.UpdatedAt);
        }

        public async Task<List<OutboundIssueDto>> GetOutboundIssuesByTicketAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            var items = await _repo.GetOutboundIssuesByTicketAsync(outboundOrderId).ConfigureAwait(false);
            return items.Select(dto => new OutboundIssueDto(
                dto.IssueId,
                dto.OutboundOrderId,
                dto.OutboundOrderItemId,
                dto.ProductId,
                dto.IssueQuantity,
                dto.Reason,
                dto.Note,
                dto.ImageUrl,
                dto.ReportedBy,
                dto.ReportedAt,
                dto.UpdatedBy,
                dto.UpdatedAt)).ToList();
        }

        public async Task<OutboundPathOptimizationDto> SaveOutboundPathOptimizationAsync(int outboundOrderId, CreateOutboundPathOptimizationRequest request)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.SavedBy <= 0) throw new ArgumentException("Invalid savedBy.", nameof(request.SavedBy));

            if (request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                throw new ArgumentException("Payload is required.", nameof(request.Payload));

            if (request.Payload.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Payload must be a JSON array.", nameof(request.Payload));

            var payloadArray = request.Payload;
            if (payloadArray.GetArrayLength() == 0)
                throw new ArgumentException("Payload array cannot be empty.", nameof(request.Payload));

            foreach (var block in payloadArray.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Each payload item must be a JSON object.", nameof(request.Payload));

                if (!block.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Each payload item must contain object 'summary'.", nameof(request.Payload));

                if (summary.TryGetProperty("totalDistance", out var totalDistance)
                    && totalDistance.ValueKind == JsonValueKind.Number
                    && totalDistance.TryGetDouble(out var distance)
                    && distance < 0)
                {
                    throw new ArgumentException("summary.totalDistance cannot be negative.", nameof(request.Payload));
                }
            }

            var payloadJson = request.Payload.GetRawText();
            if (payloadJson.Length > 262_144)
                throw new ArgumentException("Payload is too large. Max allowed is 256KB.", nameof(request.Payload));

            var saved = await _repo.SaveOutboundPathOptimizationAsync(outboundOrderId, request.SavedBy, payloadJson).ConfigureAwait(false);

            return new OutboundPathOptimizationDto(
                saved.OutboundOrderId,
                JsonSerializer.Deserialize<JsonElement>(saved.PayloadJson),
                saved.SavedBy,
                saved.SavedAt);
        }

        public async Task<OutboundPathOptimizationDto?> GetOutboundPathOptimizationByTicketAsync(int outboundOrderId)
        {
            if (outboundOrderId <= 0) throw new ArgumentException("Invalid outboundOrderId.", nameof(outboundOrderId));

            var data = await _repo.GetOutboundPathOptimizationByTicketAsync(outboundOrderId).ConfigureAwait(false);
            if (data == null) return null;

            return new OutboundPathOptimizationDto(
                data.OutboundOrderId,
                JsonSerializer.Deserialize<JsonElement>(data.PayloadJson),
                data.SavedBy,
                data.SavedAt);
        }
    }
}
