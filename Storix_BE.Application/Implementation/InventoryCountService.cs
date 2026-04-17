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
        private readonly INotificationService _notificationService;
        private readonly IUserRepository _userRepository;

        public InventoryCountService(IInventoryCountRepository repo, IActivityLogRepository activityLogRepo, INotificationService notificationService, IUserRepository userRepository)
        {
            _repo = repo;
            _activityLogRepo = activityLogRepo;
            _notificationService = notificationService;
            _userRepository = userRepository;
        }

        public async Task<InventoryCountsTicket> CreateStockCountTicketAsync(CreateStockCountTicketRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.PerformedBy <= 0) throw new ArgumentException("Invalid performedBy.", nameof(request.PerformedBy));
            if (request.Items == null || !request.Items.Any())
                throw new InvalidOperationException("Ticket must contain at least one item to count.");

            // Validate StorageZoneIds if provided (basic validation: ids must be positive).
            if (request.StorageZoneIds != null && request.StorageZoneIds.Any(id => id <= 0))
                throw new ArgumentException("StorageZoneIds must contain only positive integers.", nameof(request.StorageZoneIds));

            var ticket = new InventoryCountsTicket
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
            if (request.StorageZoneIds != null)
            {
                var zonePlaceholders = request.StorageZoneIds
                    .Where(id => id > 0)
                    .Distinct()
                    .Select(id => new StorageZone { Id = id })
                    .ToList();

                // preserve empty list vs null semantics: initialize collection on ticket
                foreach (var z in zonePlaceholders)
                    ticket.StorageZones.Add(z);
            }
            // Note: multiple StorageZoneIds cannot be persisted into the existing single ScopeId FK.
            // We therefore validate the incoming IDs here but do not set ScopeId. If needed, repository or a future schema change
            // should persist the full set (e.g. junction table or JSON column). For now keep ScopeId null.
            // The presence of StorageZoneIds can still be used by downstream logic if implemented later.
            // (No further action required when array is empty — it's allowed.)

            foreach (var it in request.Items)
            {
                ticket.InventoryCountItems.Add(new InventoryCountItem
                {
                    ProductId = it.ProductId,
                    LocationId = null
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
            if (created.AssignedTo.HasValue && created.AssignedTo.Value > 0)
            {
                try
                {
                    var title = "New stock count ticket assigned";
                    var message = $"Stock count ticket #{created.Id} has been created and assigned to you.";
                    await _notificationService.SendNotificationToUserAsync(
                        created.AssignedTo.Value,
                        title,
                        message,
                        type: "StockCountTicket",
                        category: "InventoryCount",
                        referenceType: "StockCountTicket",
                        referenceId: created.Id,
                        createdByUserId: request.PerformedBy
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to notify assigned staff {created.AssignedTo}: {ex.Message}");
                }
            }

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
            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var companyId = updated.PerformedByNavigation?.CompanyId ?? _userRepository.GetUserByIdWithRoleAsync(approverId).Result?.CompanyId;
                    if (companyId.HasValue && companyId.Value > 0)
                    {
                        var title = $"Stock count ticket {status.ToLowerInvariant()}";
                        var message = $"Stock count ticket #{updated.Id} has been {status.ToLowerInvariant()}.";
                        await _notificationService.SendNotificationToManagersAsync(
                            companyId.Value,
                            title,
                            message,
                            type: "StockCountTicket",
                            category: "InventoryCount",
                            referenceType: "StockCountTicket",
                            referenceId: updated.Id,
                            createdByUserId: approverId
                        ).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to notify managers for stock count ticket {updated.Id}: {ex.Message}");
                }
            }

            return updated;
        }

        public async Task<InventoryCountsTicket> UpdateStockCountItemsAsync(int ticketId, UpdateStockCountItemsRequest request)
        {
            if (ticketId <= 0) throw new ArgumentException("Invalid ticket id.", nameof(ticketId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.PerformedBy <= 0) throw new ArgumentException("Invalid performedBy.", nameof(request.PerformedBy));
            if (request.Items == null || !request.Items.Any()) throw new InvalidOperationException("Items payload cannot be empty.");

            // Map incoming DTOs to domain InventoryCountItem instances.
            // Note: clients provide BinId (string). If BinId is an integer representation of an InventoryLocation.Id we use it.
            // Otherwise we preserve the BinId string in Description so repository or operators can resolve it later if needed.
            var domainItems = request.Items.Select(i =>
            {
                int? locationId = null;
                if (!string.IsNullOrWhiteSpace(i.BinId) && int.TryParse(i.BinId, out var parsedLocationId) && parsedLocationId > 0)
                    locationId = parsedLocationId;

                return new InventoryCountItem
                {
                    Id = i.StockCountItemId,
                    ProductId = i.ProductId,
                    CountedQuantity = i.CountedQuantity,
                    // if BinId is numeric -> set LocationId; otherwise preserve BinId in Description for downstream resolution
                    LocationId = locationId,
                    Description = locationId == null ? i.BinId : null
                };
            }).ToList();

            var updated = await _repo.UpdateStockCountItemsAsync(ticketId, domainItems, request.PerformedBy).ConfigureAwait(false);

            // After staff finishes updating items notify managers
            try
            {
                var companyId = updated.Warehouse?.CompanyId ?? updated.PerformedByNavigation?.CompanyId;
                if (!companyId.HasValue)
                {
                    var usr = await _userRepository.GetUserByIdWithRoleAsync(request.PerformedBy).ConfigureAwait(false);
                    companyId = usr?.CompanyId;
                }

                if (companyId.HasValue && companyId.Value > 0)
                {
                    var title = "Stock count ticket updated by staff";
                    var message = $"Stock count ticket #{updated.Id} has been updated by staff.";
                    await _notificationService.SendNotificationToManagersAsync(
                        companyId.Value,
                        title,
                        message,
                        type: "StockCountTicket",
                        category: "InventoryCount",
                        referenceType: "StockCountTicket",
                        referenceId: updated.Id,
                        createdByUserId: request.PerformedBy
                    ).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to notify managers for stock count ticket {ticketId}: {ex.Message}");
            }

            return updated;
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
