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

            var domainItems = request.Items.Select(i => new InventoryCountItem
            {
                Id = i.StockCountItemId,
                ProductId = i.ProductId,
                CountedQuantity = i.CountedQuantity,
                LocationId = i.LocationId,
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
