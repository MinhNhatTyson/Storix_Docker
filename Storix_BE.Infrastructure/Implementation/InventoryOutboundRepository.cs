using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class InventoryOutboundRepository : IInventoryOutboundRepository
    {
        private readonly StorixDbContext _context;

        public InventoryOutboundRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<OutboundRequest> CreateOutboundRequestAsync(OutboundRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!request.RequestedBy.HasValue || request.RequestedBy <= 0)
                throw new InvalidOperationException("RequestedBy is required for outbound requests.");

            await EnsureManagerRequesterAsync(request.RequestedBy.Value).ConfigureAwait(false);

            if (request.OutboundOrderItems == null || !request.OutboundOrderItems.Any())
                throw new InvalidOperationException("OutboundRequest must contain at least one OutboundOrderItem.");

            var invalidItem = request.OutboundOrderItems.FirstOrDefault(i => i.ProductId == null || i.Quantity == null || i.Quantity <= 0);
            if (invalidItem != null)
                throw new InvalidOperationException("All OutboundOrderItems must specify a ProductId and Quantity > 0.");

            var productIds = request.OutboundOrderItems.Select(i => i.ProductId!.Value).Distinct().ToList();
            var existingProductIds = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var missing = productIds.Except(existingProductIds).ToList();
            if (missing.Any())
                throw new InvalidOperationException($"Products not found: {string.Join(',', missing)}");

            if (request.WarehouseId.HasValue)
            {
                var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId.Value).ConfigureAwait(false);
                if (warehouse == null)
                    throw new InvalidOperationException($"Warehouse with id {request.WarehouseId.Value} not found.");
                if (IsInactiveStatus(warehouse.Status))
                    throw new InvalidOperationException($"Warehouse with id {request.WarehouseId.Value} is inactive.");
            }
            else
            {
                throw new InvalidOperationException("WarehouseId is required for outbound requests.");
            }

            await EnsureStockAvailabilityAsync(request.WarehouseId.Value, request.OutboundOrderItems)
                .ConfigureAwait(false);

            request.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            if (string.IsNullOrWhiteSpace(request.Status))
                request.Status = "Pending";

            foreach (var item in request.OutboundOrderItems)
            {
                item.OutboundRequest = request;
            }

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.OutboundRequests.Add(request);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return request;
        }

        public async Task<IReadOnlyList<(int ProductId, int AvailableQuantity)>> GetInventoryAvailabilityAsync(int warehouseId, IEnumerable<int> productIds)
        {
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouseId.", nameof(warehouseId));
            if (productIds == null) throw new ArgumentNullException(nameof(productIds));

            var distinctIds = productIds.Where(id => id > 0).Distinct().ToList();
            if (!distinctIds.Any()) throw new ArgumentException("ProductIds is required.", nameof(productIds));

            var inventory = await _context.Inventories
                .Where(i => i.WarehouseId == warehouseId && i.ProductId.HasValue && distinctIds.Contains(i.ProductId.Value))
                .Select(i => new { ProductId = i.ProductId!.Value, Quantity = i.Quantity ?? 0 })
                .ToListAsync()
                .ConfigureAwait(false);

            var quantityByProduct = inventory.ToDictionary(i => i.ProductId, i => i.Quantity);

            var result = distinctIds
                .Select(id => (ProductId: id, AvailableQuantity: quantityByProduct.TryGetValue(id, out var qty) ? qty : 0))
                .ToList();

            return result;
        }

        public async Task<OutboundRequest> UpdateOutboundRequestStatusAsync(int requestId, int approverId, string status)
        {
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            await EnsureCompanyAdministratorApproverAsync(approverId).ConfigureAwait(false);

            var outbound = await _context.OutboundRequests
                .FirstOrDefaultAsync(r => r.Id == requestId)
                .ConfigureAwait(false);

            if (outbound == null)
                throw new InvalidOperationException($"OutboundRequest with id {requestId} not found.");

            outbound.Status = status;
            outbound.ApprovedBy = approverId;
            outbound.ApprovedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return outbound;
        }

        public async Task<OutboundOrder> CreateOutboundOrderFromRequestAsync(int outboundRequestId, int createdBy, int? staffId, string? note)
        {
            var outboundRequest = await _context.OutboundRequests
                .Include(r => r.OutboundOrderItems)
                .FirstOrDefaultAsync(r => r.Id == outboundRequestId)
                .ConfigureAwait(false);

            if (outboundRequest == null)
                throw new InvalidOperationException($"OutboundRequest with id {outboundRequestId} not found.");

            if (!string.Equals(outboundRequest.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("OutboundRequest must be in 'Approved' status to create an OutboundOrder.");

            if (!outboundRequest.WarehouseId.HasValue)
                throw new InvalidOperationException("OutboundRequest must specify WarehouseId to check inventory availability.");

            await EnsureWarehouseActiveAsync(outboundRequest.WarehouseId.Value).ConfigureAwait(false);

            var productIds = outboundRequest.OutboundOrderItems
                .Where(i => i.ProductId.HasValue)
                .Select(i => i.ProductId!.Value)
                .Distinct()
                .ToList();

            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == outboundRequest.WarehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var reqItem in outboundRequest.OutboundOrderItems)
            {
                if (!reqItem.ProductId.HasValue || !reqItem.Quantity.HasValue)
                    throw new InvalidOperationException("OutboundRequest items must specify ProductId and Quantity.");

                var inventory = inventories.FirstOrDefault(i => i.ProductId == reqItem.ProductId);
                if (inventory == null || (inventory.Quantity ?? 0) < reqItem.Quantity)
                {
                    var available = inventory?.Quantity ?? 0;
                    throw new InvalidOperationException($"Insufficient stock for ProductId {reqItem.ProductId}. Available: {available}, Requested: {reqItem.Quantity}");
                }
            }

            if (staffId.HasValue)
            {
                await EnsureStaffAssignedToWarehouseAsync(outboundRequest.WarehouseId.Value, staffId.Value)
                    .ConfigureAwait(false);
            }

            var outboundOrder = new OutboundOrder
            {
                WarehouseId = outboundRequest.WarehouseId,
                Destination = outboundRequest.Destination,
                CreatedBy = createdBy,
                StaffId = staffId,
                Note = note,
                Status = "Created",
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            foreach (var reqItem in outboundRequest.OutboundOrderItems)
            {
                var orderItem = new OutboundOrderItem
                {
                    ProductId = reqItem.ProductId,
                    Quantity = reqItem.Quantity,
                    OutboundRequestId = outboundRequest.Id
                };
                outboundOrder.OutboundOrderItems.Add(orderItem);
            }

            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                _context.OutboundOrders.Add(outboundOrder);
                await _context.SaveChangesAsync().ConfigureAwait(false);

                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return outboundOrder;
        }

        public async Task<OutboundOrder> UpdateOutboundOrderItemsAsync(int outboundOrderId, IEnumerable<OutboundOrderItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var order = await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.StaffId.HasValue)
                throw new InvalidOperationException("OutboundOrder must assign StaffId before staff can update items.");

            await EnsureStaffAssignedToWarehouseAsync(order.WarehouseId ?? 0, order.StaffId.Value)
                .ConfigureAwait(false);

            var currentStatus = NormalizeStatus(order.Status ?? "Created");
            if (!IsItemUpdateAllowedStatus(currentStatus))
                throw new InvalidOperationException($"Items can only be updated during QualityCheck/LoadTemporary/IssueReported. Current status: '{currentStatus}'.");

            foreach (var incoming in items)
            {
                if (incoming.ProductId == null || incoming.ProductId <= 0)
                    throw new InvalidOperationException("Each item must have a valid ProductId.");
                if (incoming.Quantity == null || incoming.Quantity <= 0)
                    throw new InvalidOperationException("Each item must have Quantity > 0.");
            }

            foreach (var incoming in items)
            {
                if (incoming.Id > 0)
                {
                    var existing = order.OutboundOrderItems.FirstOrDefault(x => x.Id == incoming.Id);
                    if (existing == null)
                        throw new InvalidOperationException($"OutboundOrderItem with id {incoming.Id} not found in order {outboundOrderId}.");

                    existing.ProductId = incoming.ProductId;
                    existing.Quantity = incoming.Quantity;
                }
                else
                {
                    var existingByProduct = order.OutboundOrderItems.FirstOrDefault(x => x.ProductId == incoming.ProductId);
                    if (existingByProduct != null)
                    {
                        existingByProduct.Quantity = incoming.Quantity;
                    }
                    else
                    {
                        var newItem = new OutboundOrderItem
                        {
                            ProductId = incoming.ProductId,
                            Quantity = incoming.Quantity,
                            OutboundOrder = order
                        };
                        order.OutboundOrderItems.Add(newItem);
                    }
                }
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);

            return order;
        }

        public async Task<OutboundOrder> UpdateOutboundOrderStatusAsync(int outboundOrderId, int performedBy, string status)
        {
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var order = await _context.OutboundOrders
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.StaffId.HasValue)
                throw new InvalidOperationException("OutboundOrder must assign StaffId before staff can update status.");

            if (order.StaffId.Value != performedBy)
                throw new InvalidOperationException("Only assigned staff can update outbound order status.");

            await EnsureStaffAssignedToWarehouseAsync(order.WarehouseId ?? 0, performedBy)
                .ConfigureAwait(false);

            if (string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Completed outbound order cannot be updated.");

            var normalized = NormalizeStatus(status);
            if (!IsStaffStatusAllowed(normalized))
                throw new InvalidOperationException("Invalid staff status. Allowed: Picking, QualityCheck, IssueReported, Packing, LoadHandover.");

            var current = NormalizeStatus(order.Status ?? "Created");
            if (!IsStaffTransitionAllowed(current, normalized))
                throw new InvalidOperationException($"Invalid status transition from '{current}' to '{normalized}'.");

            order.Status = normalized;
            await _context.SaveChangesAsync().ConfigureAwait(false);

            return order;
        }

        public async Task<OutboundOrder> ConfirmOutboundOrderAsync(int outboundOrderId, int performedBy)
        {
            var order = await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                .FirstOrDefaultAsync(o => o.Id == outboundOrderId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {outboundOrderId} not found.");

            if (!order.WarehouseId.HasValue)
                throw new InvalidOperationException("OutboundOrder must specify WarehouseId to update inventory.");

            await EnsureWarehouseActiveAsync(order.WarehouseId.Value).ConfigureAwait(false);

            await EnsureManagerPerformerAsync(performedBy).ConfigureAwait(false);

            var productIds = order.OutboundOrderItems
                .Where(i => i.ProductId.HasValue)
                .Select(i => i.ProductId!.Value)
                .Distinct()
                .ToList();

            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == order.WarehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync()
                .ConfigureAwait(false);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await using var tx = await _context.Database.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                foreach (var item in order.OutboundOrderItems)
                {
                    if (!item.ProductId.HasValue || !item.Quantity.HasValue || item.Quantity <= 0)
                        throw new InvalidOperationException("OutboundOrder items must have ProductId and Quantity > 0.");

                    var inventory = inventories.FirstOrDefault(i => i.ProductId == item.ProductId);
                    if (inventory == null || (inventory.Quantity ?? 0) < item.Quantity)
                    {
                        var available = inventory?.Quantity ?? 0;
                        throw new InvalidOperationException($"Insufficient stock for ProductId {item.ProductId}. Available: {available}, Requested: {item.Quantity}");
                    }

                    inventory.Quantity = (inventory.Quantity ?? 0) - item.Quantity;
                    inventory.LastUpdated = now;

                    var transaction = new InventoryTransaction
                    {
                        WarehouseId = order.WarehouseId,
                        ProductId = item.ProductId,
                        TransactionType = "Outbound",
                        QuantityChange = -item.Quantity,
                        ReferenceId = order.Id,
                        PerformedBy = performedBy,
                        CreatedAt = now
                    };
                    _context.InventoryTransactions.Add(transaction);
                }

                order.Status = "Completed";

                await _context.SaveChangesAsync().ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync().ConfigureAwait(false);
                throw;
            }

            return order;
        }

        private static bool IsInactiveStatus(string? status)
        {
            return string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase);
        }

        private async Task EnsureWarehouseActiveAsync(int warehouseId)
        {
            var warehouse = await _context.Warehouses.FindAsync(warehouseId).ConfigureAwait(false);
            if (warehouse == null)
                throw new InvalidOperationException($"Warehouse with id {warehouseId} not found.");
            if (IsInactiveStatus(warehouse.Status))
                throw new InvalidOperationException($"Warehouse with id {warehouseId} is inactive.");
        }

        private async Task EnsureManagerRequesterAsync(int requesterId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == requesterId)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {requesterId} not found.");

            if (!user.RoleId.HasValue || user.RoleId.Value != 3)
                throw new InvalidOperationException("Only Manager can create outbound requests.");
        }

        private async Task EnsureManagerPerformerAsync(int performerId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == performerId)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {performerId} not found.");

            if (!user.RoleId.HasValue || user.RoleId.Value != 3)
                throw new InvalidOperationException("Only Manager can confirm outbound orders.");
        }

        private async Task EnsureCompanyAdministratorApproverAsync(int approverId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == approverId)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {approverId} not found.");

            if (!user.RoleId.HasValue)
                throw new InvalidOperationException($"User with id {approverId} has no role assigned.");

            if (user.RoleId.Value == 1)
                throw new InvalidOperationException("Super Admin cannot approve outbound requests.");

            if (user.RoleId.Value != 2)
                throw new InvalidOperationException("Only Company Administrator can approve outbound requests.");
        }

        public async Task<List<OutboundRequest>> GetAllOutboundRequestsAsync(int companyId, int? warehouseId)
        {
            var query = _context.OutboundRequests
                .Include(r => r.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .Where(r => r.Warehouse != null && r.Warehouse.CompanyId == companyId);

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                query = query.Where(r => r.WarehouseId == warehouseId.Value);
            }

            return await query
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<OutboundRequest> GetOutboundRequestByIdAsync(int companyId, int id)
        {
            var request = await _context.OutboundRequests
                .Include(r => r.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(r => r.Warehouse)
                .Include(r => r.RequestedByNavigation)
                .Include(r => r.ApprovedByNavigation)
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.Warehouse != null &&
                    r.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (request == null)
                throw new InvalidOperationException($"OutboundRequest with id {id} not found.");

            return request;
        }

        public async Task<List<OutboundOrder>> GetAllOutboundOrdersAsync(int companyId, int? warehouseId)
        {
            var query = _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Where(o => o.Warehouse != null && o.Warehouse.CompanyId == companyId);

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                query = query.Where(o => o.WarehouseId == warehouseId.Value);
            }

            return await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<OutboundOrder> GetOutboundOrderByIdAsync(int companyId, int id)
        {
            var order = await _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .FirstOrDefaultAsync(o =>
                    o.Id == id &&
                    o.Warehouse != null &&
                    o.Warehouse.CompanyId == companyId)
                .ConfigureAwait(false);

            if (order == null)
                throw new InvalidOperationException($"OutboundOrder with id {id} not found.");

            return order;
        }
        public async Task<List<OutboundOrder>> GetOutboundOrdersByStaffAsync(int companyId, int staffId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (staffId <= 0) throw new ArgumentException("Invalid staff id.", nameof(staffId));

            var query = _context.OutboundOrders
                .Include(o => o.OutboundOrderItems)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .Include(o => o.CreatedByNavigation)
                .Include(o => o.Staff)
                .Where(o => o.StaffId == staffId && o.Warehouse != null && o.Warehouse.CompanyId == companyId)
                .OrderByDescending(o => o.CreatedAt);

            return await query.ToListAsync().ConfigureAwait(false);
        }

        private async Task EnsureStaffAssignedToWarehouseAsync(int warehouseId, int staffId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == staffId)
                .ConfigureAwait(false);

            if (user == null)
                throw new InvalidOperationException($"User with id {staffId} not found.");

            if (!user.RoleId.HasValue || user.RoleId.Value != 4)
                throw new InvalidOperationException("Only Staff (roleId=4) can perform picking/packing.");

            var warehouse = await _context.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == warehouseId)
                .ConfigureAwait(false);

            if (warehouse == null)
                throw new InvalidOperationException($"Warehouse with id {warehouseId} not found.");

            if (!user.CompanyId.HasValue || !warehouse.CompanyId.HasValue || user.CompanyId.Value != warehouse.CompanyId.Value)
                throw new InvalidOperationException("Staff does not belong to the same company as the warehouse.");

            var assigned = await _context.WarehouseAssignments
                .AnyAsync(a => a.WarehouseId == warehouseId && a.UserId == staffId)
                .ConfigureAwait(false);

            if (!assigned)
                throw new InvalidOperationException($"Staff {staffId} is not assigned to warehouse {warehouseId}.");
        }

        private static bool IsStaffStatusAllowed(string status)
        {
            return status is "Picking" or "QualityCheck" or "IssueReported" or "Packing" or "LoadHandover";
        }

        private static bool IsItemUpdateAllowedStatus(string status)
        {
            return status is "QualityCheck" or "IssueReported";
        }

        private static string NormalizeStatus(string status)
        {
            var trimmed = status.Trim();
            return trimmed.Length == 0
                ? trimmed
                : char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
        }

        private static bool IsStaffTransitionAllowed(string current, string next)
        {
            if (string.IsNullOrWhiteSpace(next)) return false;

            if (string.IsNullOrWhiteSpace(current) || current == "Created")
                return next == "Picking";

            return current switch
            {
                "Picking" => next == "QualityCheck",
                "QualityCheck" => next is "IssueReported" or "Packing",
                "IssueReported" => next == "Packing",
                "Packing" => next == "LoadHandover",
                _ => false
            };
        }

        private async Task EnsureStockAvailabilityAsync(int warehouseId, IEnumerable<OutboundOrderItem> items)
        {
            var productIds = items
                .Where(i => i.ProductId.HasValue)
                .Select(i => i.ProductId!.Value)
                .Distinct()
                .ToList();

            var inventories = await _context.Inventories
                .Where(i => i.WarehouseId == warehouseId && i.ProductId.HasValue && productIds.Contains(i.ProductId.Value))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var item in items)
            {
                if (!item.ProductId.HasValue || !item.Quantity.HasValue)
                    throw new InvalidOperationException("OutboundRequest items must specify ProductId and Quantity.");

                var inventory = inventories.FirstOrDefault(i => i.ProductId == item.ProductId);
                if (inventory == null || (inventory.Quantity ?? 0) < item.Quantity)
                {
                    var available = inventory?.Quantity ?? 0;
                    throw new InvalidOperationException($"Insufficient stock for ProductId {item.ProductId}. Available: {available}, Requested: {item.Quantity}");
                }
            }
        }
    }
}
