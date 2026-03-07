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
        private readonly IReportingRepository _reportingRepo;

        public InventoryCountService(IInventoryCountRepository repo, IReportingRepository reportingRepo)
        {
            _repo = repo;
            _reportingRepo = reportingRepo;
        }

        public async Task<IReadOnlyList<InventoryCountInventoryProductDto>> ListInventoryProductsAsync(int companyId, int warehouseId, IEnumerable<int>? productIds = null)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company.", nameof(companyId));
            if (warehouseId <= 0) throw new ArgumentException("Invalid warehouse.", nameof(warehouseId));

            var rows = await _repo.ListInventoryProductsAsync(companyId, warehouseId, productIds).ConfigureAwait(false);
            return rows
                .Where(x => x.ProductId.HasValue)
                .Select(x => new InventoryCountInventoryProductDto(
                    x.ProductId!.Value,
                    x.Product?.Sku,
                    x.Product?.Name,
                    x.Quantity ?? 0))
                .ToList();
        }

        public async Task<InventoryCountTicketDetailDto> CreateTicketAsync(int companyId, int createdByUserId, CreateInventoryCountTicketRequest request)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company.", nameof(companyId));
            if (createdByUserId <= 0) throw new ArgumentException("Invalid user.", nameof(createdByUserId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.WarehouseId <= 0) throw new ArgumentException("Warehouse is required.", nameof(request.WarehouseId));

            var ticket = await _repo.CreateTicketAsync(
                    companyId,
                    request.WarehouseId,
                    createdByUserId,
                    request.Name,
                    request.Type,
                    request.Description,
                    request.ProductIds,
                    request.AssignedTo)
                .ConfigureAwait(false);

            return MapTicketDetail(ticket);
        }

        public async Task<List<InventoryCountTicketListItemDto>> ListTicketsAsync(int companyId, int? warehouseId, string? status)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company.", nameof(companyId));
            var tickets = await _repo.ListTicketsAsync(companyId, warehouseId, status).ConfigureAwait(false);
            return tickets.Select(MapTicketListItem).ToList();
        }

        public async Task<InventoryCountTicketDetailDto> GetTicketByIdAsync(int companyId, int ticketId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company.", nameof(companyId));
            if (ticketId <= 0) throw new ArgumentException("Invalid ticket.", nameof(ticketId));

            var ticket = await _repo.GetTicketByIdAsync(companyId, ticketId).ConfigureAwait(false);
            return MapTicketDetail(ticket);
        }

        public async Task<InventoryCountItemDto> UpdateCountedQuantityAsync(int companyId, int callerUserId, int callerRoleId, int itemId, UpdateInventoryCountItemRequest request)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company.", nameof(companyId));
            if (callerUserId <= 0) throw new ArgumentException("Invalid caller user.", nameof(callerUserId));
            if (itemId <= 0) throw new ArgumentException("Invalid item.", nameof(itemId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.CountedQuantity < 0)
                throw new ArgumentException("Counted quantity must be greater than or equal to 0.", nameof(request.CountedQuantity));

            var itemBeforeUpdate = await _repo.GetItemByIdAsync(companyId, itemId).ConfigureAwait(false);
            if (itemBeforeUpdate.InventoryCount == null)
                throw new InvalidOperationException("Inventory count item is not linked to a ticket.");

            var ticketStatus = itemBeforeUpdate.InventoryCount.Status ?? string.Empty;
            if (!string.Equals(ticketStatus, "Counting", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Counted quantity can only be updated when ticket status is 'Counting'.");

            if (callerRoleId == 4)
            {
                var assignedTo = itemBeforeUpdate.InventoryCount.AssignedTo;
                if (!assignedTo.HasValue || assignedTo.Value != callerUserId)
                    throw new InvalidOperationException("You are not assigned to this inventory count ticket.");
            }
            else if (callerRoleId != 3)
            {
                throw new InvalidOperationException("Only manager or assigned staff can update counted quantity.");
            }

            var item = await _repo.UpdateCountedQuantityAsync(companyId, itemId, request.CountedQuantity, request.Description, request.Status)
                .ConfigureAwait(false);

            return MapItem(item);
        }

        public async Task<RunInventoryCountResultDto> RunAsync(int companyId, int createdByUserId, int ticketId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company.", nameof(companyId));
            if (createdByUserId <= 0) throw new ArgumentException("Invalid user.", nameof(createdByUserId));
            if (ticketId <= 0) throw new ArgumentException("Invalid ticket.", nameof(ticketId));

            var ticket = await _repo.GetTicketByIdAsync(companyId, ticketId).ConfigureAwait(false);
            if (!string.Equals(ticket.Status, "Counting", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run check is only allowed when ticket status is 'Counting'.");
            if (!ticket.WarehouseId.HasValue)
                throw new InvalidOperationException("Ticket has no warehouse.");

            var items = (ticket.InventoryCountItems ?? new List<InventoryCountItem>()).ToList();
            if (items.Count == 0)
                throw new InvalidOperationException("Ticket has no items.");
            if (items.Any(i => !i.CountedQuantity.HasValue))
                throw new InvalidOperationException("Some items are missing counted quantity. Please enter counted quantity for all items before running the check.");

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

            var dataItems = items.Select(i => new
            {
                itemId = i.Id,
                productId = i.ProductId,
                sku = i.Product?.Sku,
                name = i.Product?.Name,
                systemQuantity = i.SystemQuantity,
                countedQuantity = i.CountedQuantity,
                discrepancy = i.Discrepancy,
                description = i.Description,
                status = i.Status
            }).ToList();

            var summaryObj = new
            {
                ticketId = ticket.Id,
                warehouseId = ticket.WarehouseId,
                itemCount = dataItems.Count,
                countedCount = dataItems.Count(x => x.countedQuantity.HasValue),
                positiveDiscrepancyCount = dataItems.Count(x => (x.discrepancy ?? 0) > 0),
                negativeDiscrepancyCount = dataItems.Count(x => (x.discrepancy ?? 0) < 0),
                totalDiscrepancyAbs = dataItems.Sum(x => Math.Abs(x.discrepancy ?? 0))
            };

            var report = new Report
            {
                CompanyId = companyId,
                CreatedByUserId = createdByUserId,
                ReportType = ReportTypes.InventoryTracking,
                WarehouseId = ticket.WarehouseId,
                Status = "Running",
                CreatedAt = now,
                ParametersJson = JsonSerializer.Serialize(new { ticketId = ticket.Id }, jsonOptions)
            };

            report = await _reportingRepo.CreateReportAsync(report).ConfigureAwait(false);

            try
            {
                report.SummaryJson = JsonSerializer.Serialize(summaryObj, jsonOptions);
                report.DataJson = JsonSerializer.Serialize(new { ticketId = ticket.Id, items = dataItems }, jsonOptions);
                report.SchemaVersion = "1";
                report.Status = "Succeeded";
                report.CompletedAt = now;
                report.ErrorMessage = null;
                await _reportingRepo.UpdateReportAsync(report).ConfigureAwait(false);

                await _repo.MarkTicketReadyForApprovalAsync(companyId, ticketId).ConfigureAwait(false);
                return new RunInventoryCountResultDto(report.Id, ParseJson(report.SummaryJson), ParseJson(report.DataJson));
            }
            catch (Exception ex)
            {
                report.Status = "Failed";
                report.CompletedAt = now;
                report.ErrorMessage = ex.Message;
                await _reportingRepo.UpdateReportAsync(report).ConfigureAwait(false);
                throw;
            }
        }

        public async Task ApproveAsync(int companyId, int performedByUserId, int ticketId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid company.", nameof(companyId));
            if (performedByUserId <= 0) throw new ArgumentException("Invalid user.", nameof(performedByUserId));
            if (ticketId <= 0) throw new ArgumentException("Invalid ticket.", nameof(ticketId));

            await _repo.ApplyApprovalAsync(companyId, ticketId, performedByUserId).ConfigureAwait(false);
        }

        private static InventoryCountTicketListItemDto MapTicketListItem(InventoryCountsTicket t)
            => new(
                t.Id,
                t.WarehouseId,
                t.Name,
                t.Type,
                t.Status,
                t.CreatedAt,
                t.ExecutedDay,
                t.FinishedDay,
                t.InventoryCountItems?.Count ?? 0);

        private static InventoryCountTicketDetailDto MapTicketDetail(InventoryCountsTicket t)
            => new(
                t.Id,
                t.WarehouseId,
                t.Name,
                t.Type,
                t.Status,
                t.CreatedAt,
                t.ExecutedDay,
                t.FinishedDay,
                t.Description,
                (t.InventoryCountItems ?? Enumerable.Empty<InventoryCountItem>()).Select(MapItem).ToList());

        private static InventoryCountItemDto MapItem(InventoryCountItem i)
            => new(i.Id, i.ProductId, i.Product?.Sku, i.Product?.Name, i.SystemQuantity, i.CountedQuantity, i.Discrepancy, i.Status, i.Description);

        private static JsonElement ParseJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return JsonDocument.Parse("{}").RootElement.Clone();

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
    }
}
