using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class InventoryCountService : IInventoryCountService
    {
        private readonly IInventoryCountRepository _repo;
        private readonly IActivityLogRepository _activityLogRepo;

        public InventoryCountService(IInventoryCountRepository repo, IActivityLogRepository activityLogRepo)
        {
            _repo = repo;
            _activityLogRepo = activityLogRepo;
        }

        public async Task<InventoryCountsTicket> CreateStockCountTicketAsync(CreateStockCountTicketRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.PerformedBy <= 0) throw new ArgumentException("Invalid performedBy.", nameof(request.PerformedBy));
            if (request.Items == null || !request.Items.Any())
                throw new InvalidOperationException("Ticket must contain at least one item to count.");
            if (request.ScopeId.HasValue && request.ScopeId <= 0) throw new ArgumentException("Invalid scope id.", nameof(request.ScopeId));

            var ticket = new InventoryCountsTicket();

            if (request.ScopeId == null)
            {
                ticket = new InventoryCountsTicket
                {
                    WarehouseId = request.WarehouseId,
                    PerformedBy = request.PerformedBy,
                    AssignedTo = request.AssignedTo,
                    Name = request.Name,
                    Description = request.Description,
                    ScopeType = request.ScopeType,
                    PlannedAt = request.PlannedAt,
                    Status = "Pending"
                };
            }
            else
            {
                ticket = new InventoryCountsTicket
                {
                    WarehouseId = request.WarehouseId,
                    PerformedBy = request.PerformedBy,
                    AssignedTo = request.AssignedTo,
                    Name = request.Name,
                    Description = request.Description,
                    ScopeType = request.ScopeType,
                    ScopeId = request.ScopeId,
                    PlannedAt = request.PlannedAt,
                    Status = "Pending"
                };
            }

            foreach (var it in request.Items)
            {
                ticket.InventoryCountItems.Add(new InventoryCountItem
                {
                    ProductId = it.ProductId,
                    LocationId = it.LocationId
                });
            }

            var created = await _repo.CreateStockCountTicketAsync(ticket).ConfigureAwait(false);
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = request.PerformedBy,
                Action = "Create StockCountTicket",
                Entity = "StockCountTicket",
                EntityId = created.Id,
                Timestamp = now
            }).ConfigureAwait(false);
            return created;
        }

        public async Task<InventoryCountsTicket> UpdateStockCountTicketStatusAsync(int ticketId, int approverId, string status)
        {
            if (ticketId <= 0) throw new ArgumentException("Invalid ticket id.", nameof(ticketId));
            if (approverId <= 0) throw new ArgumentException("Invalid approver id.", nameof(approverId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

            var updated = await _repo.UpdateStockCountTicketStatusAsync(ticketId, approverId, status).ConfigureAwait(false);
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = approverId,
                Action = $"{status} StockCountTicket",
                Entity = "StockCountTicket",
                EntityId = updated.Id,
                Timestamp = now
            }).ConfigureAwait(false);
            return updated;
        }

        public async Task<InventoryCountsTicket> UpdateStockCountItemsAsync(int ticketId, UpdateStockCountItemsRequest request)
        {
            if (ticketId <= 0) throw new ArgumentException("Invalid ticket id.", nameof(ticketId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.PerformedBy <= 0) throw new ArgumentException("Invalid performedBy.", nameof(request.PerformedBy));
            if (request.Items == null || !request.Items.Any()) throw new InvalidOperationException("Items payload cannot be empty.");

            var domainItems = request.Items.Select(i => new InventoryCountItem
            {
                Id = i.StockCountItemId,
                ProductId = i.ProductId,
                CountedQuantity = i.CountedQuantity,
                LocationId = i.LocationId,
            }).ToList();

            return await _repo.UpdateStockCountItemsAsync(ticketId, domainItems, request.PerformedBy).ConfigureAwait(false);
        }

        public Task<List<InventoryCountsTicket>> GetStockCountTicketsByCompanyAsync(int companyId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            return _repo.GetStockCountTicketsByCompanyAsync(companyId);
        }

        public Task<InventoryCountsTicket> GetStockCountTicketByIdAsync(int companyId, int id)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (id <= 0) throw new ArgumentException("Invalid ticket id.", nameof(id));
            return _repo.GetStockCountTicketByIdAsync(companyId, id);
        }

        public Task<List<InventoryCountsTicket>> GetStockCountTicketsByStaffAsync(int companyId, int staffId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company id.", nameof(companyId));
            if (staffId <= 0) throw new ArgumentException("Invalid staff id.", nameof(staffId));
            return _repo.GetStockCountTicketsByStaffAsync(companyId, staffId);
        }
    }
}
