using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryOutboundController : ControllerBase
    {
        private readonly IInventoryOutboundService _service;

        public InventoryOutboundController(IInventoryOutboundService service)
        {
            _service = service;
        }
        /// <summary>
        /// Returns FIFO-ordered bin picking suggestions for all items in an outbound ticket.
        /// The system resolves the oldest batch first and cascades across bins and batches
        /// until the required quantity is covered.
        /// </summary>
        [HttpGet("tickets/{ticketId:int}/fifo-suggestions")]
        [Authorize(Roles = "2,3,4")]
        public async Task<IActionResult> GetFifoPickingSuggestions(int ticketId)
        {
            if (ticketId <= 0)
                return BadRequest(new { message = "Invalid ticket id." });

            try
            {
                var suggestions = await _service
                    .GetFifoPickingSuggestionsAsync(ticketId)
                    .ConfigureAwait(false);

                return Ok(suggestions);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
        [HttpPost("create-outbound-request")]
        public async Task<IActionResult> CreateRequest([FromBody] CreateOutboundRequestRequest request)
        {
            try
            {
                var authError = EnsureRole(4, "Only Staff (roleId=4) can create outbound requests.");
                if (authError != null) return authError;

                var outboundRequest = await _service.CreateOutboundRequestAsync(request);
                return Ok(outboundRequest);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("availability")]
        public async Task<IActionResult> GetAvailability([FromQuery] int warehouseId, [FromQuery] int[] productIds)
        {
            try
            {
                var authError = EnsureRole(3, "Only Manager (roleId=3) can view availability.");
                if (authError != null) return authError;

                if (warehouseId <= 0)
                    return BadRequest(new { message = "Invalid warehouseId." });
                if (productIds == null || productIds.Length == 0)
                    return BadRequest(new { message = "productIds is required." });

                var result = await _service.GetInventoryAvailabilityAsync(warehouseId, productIds);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }


        [HttpPut("update-outbound-request/{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOutboundRequestStatusRequest request)
        {
            try
            {
                var authError = EnsureRole(3, "Only Manager (roleId=3) can approve outbound requests.", "Super Admin (roleId=1) cannot approve outbound requests.");
                if (authError != null) return authError;

                var outboundRequest = await _service.UpdateOutboundRequestStatusAsync(id, request.ApproverId, request.Status);
                return Ok(outboundRequest);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("create-outbound-ticket/{requestId}/tickets")]
        public async Task<IActionResult> CreateTicketFromRequest(int requestId, [FromBody] CreateOutboundOrderFromRequestRequest payload)
        {
            try
            {
                var authError = EnsureRole(3, "Only Manager (roleId=3) can create outbound tickets.");
                if (authError != null) return authError;

                var ticket = await _service.CreateOutboundOrderFromRequestAsync(requestId, payload.CreatedBy, payload.StaffId, payload.Note, payload.PricingMethod);
                return Ok(ticket);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Update OutboundOrder (ticket) items — aligned with inbound edit-items payload.
        /// </summary>
        [HttpPut("tickets/{ticketId}/items")]
        public async Task<IActionResult> UpdateTicketItems(int ticketId, [FromBody] IEnumerable<UpdateOutboundOrderItemRequest> items)
        {
            try
            {
                var authError = EnsureRole(4, "Only Staff (roleId=4) can update outbound ticket items.");
                if (authError != null) return authError;

                var ticket = await _service.UpdateOutboundOrderItemsAsync(ticketId, items);
                return Ok(ticket);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPut("tickets/{ticketId}/status")]
        public async Task<IActionResult> UpdateTicketStatus(int ticketId, [FromBody] UpdateOutboundOrderStatusRequest payload)
        {
            try
            {
                var authError = EnsureRole(4, "Only Staff (roleId=4) can update outbound ticket status.");
                if (authError != null) return authError;

                var ticket = await _service.UpdateOutboundOrderStatusAsync(ticketId, payload.PerformedBy, payload.Status);
                return Ok(ticket);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("tickets/{ticketId:int}/issues")]
        public async Task<IActionResult> CreateTicketIssue(int ticketId, [FromBody] CreateOutboundIssueRequest payload)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticket id." });

            try
            {
                var authError = EnsureRole(4, "Only Staff (roleId=4) can create outbound issues.");
                if (authError != null) return authError;

                var issue = await _service.CreateOutboundIssueAsync(ticketId, payload);
                return Ok(issue);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPut("tickets/{ticketId:int}/issues/{issueId:int}")]
        public async Task<IActionResult> UpdateTicketIssue(int ticketId, int issueId, [FromBody] UpdateOutboundIssueRequest payload)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticket id." });
            if (issueId <= 0) return BadRequest(new { message = "Invalid issue id." });

            try
            {
                var authError = EnsureRole(4, "Only Staff (roleId=4) can update outbound issues.");
                if (authError != null) return authError;

                var issue = await _service.UpdateOutboundIssueAsync(ticketId, issueId, payload);
                return Ok(issue);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("tickets/{ticketId:int}/issues")]
        public async Task<IActionResult> GetTicketIssues(int ticketId)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticket id." });

            try
            {
                var authError = EnsureRoleIn(new[] { 3, 4 }, "Only Manager (roleId=3) or Staff (roleId=4) can view outbound issues.");
                if (authError != null) return authError;

                var issues = await _service.GetOutboundIssuesByTicketAsync(ticketId);
                return Ok(issues);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [Authorize(Roles = "2,3,4")]
        [HttpPost("tickets/{ticketId:int}/path-optimization")]
        public async Task<IActionResult> SavePathOptimization(int ticketId, [FromBody] CreateOutboundPathOptimizationRequest payload)
        {
            if (ticketId <= 0) return BadRequest(new { code = "INVALID_TICKET_ID", message = "Invalid ticket id." });

            try
            {
                var result = await _service.SaveOutboundPathOptimizationAsync(ticketId, payload);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { code = "PATH_OPT_OPERATION_INVALID", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { code = "PATH_OPT_VALIDATION_FAILED", message = ex.Message, details = ex.ParamName });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { code = "PATH_OPT_INTERNAL_ERROR", message = ex.Message });
            }
        }

        [Authorize(Roles = "2,3,4")]
        [HttpGet("tickets/{ticketId:int}/path-optimization")]
        public async Task<IActionResult> GetPathOptimizationByTicket(int ticketId)
        {
            if (ticketId <= 0) return BadRequest(new { code = "INVALID_TICKET_ID", message = "Invalid ticket id." });

            try
            {
                var result = await _service.GetOutboundPathOptimizationByTicketAsync(ticketId);
                if (result == null)
                    return NotFound(new { code = "PATH_OPT_NOT_FOUND", message = "Path optimization not found for this ticket." });

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { code = "PATH_OPT_OPERATION_INVALID", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { code = "PATH_OPT_VALIDATION_FAILED", message = ex.Message, details = ex.ParamName });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { code = "PATH_OPT_INTERNAL_ERROR", message = ex.Message });
            }
        }

        [HttpGet("requests/{companyId:int}")]
        public async Task<IActionResult> GetAllRequests(int companyId, [FromQuery] int? warehouseId)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (warehouseId.HasValue && warehouseId.Value <= 0)
                return BadRequest(new { message = "Invalid warehouse id." });

            try
            {
                var items = await _service.GetAllOutboundRequestsAsync(companyId, warehouseId);
                return Ok(items);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("requests/by-warehouse/{warehouseId:int}")]
        public async Task<IActionResult> GetRequestsByWarehouse(int warehouseId)
        {
            if (warehouseId <= 0) return BadRequest(new { message = "Invalid warehouse id." });

            try
            {
                var items = await _service.GetOutboundRequestsByWarehouseIdAsync(warehouseId);
                return Ok(items);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("requests/{companyId:int}/{id:int}")]
        public async Task<IActionResult> GetRequestById(int companyId, int id)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (id <= 0) return BadRequest(new { message = "Invalid request id." });

            try
            {
                var item = await _service.GetOutboundRequestByIdAsync(companyId, id);
                return Ok(item);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("tickets/{companyId:int}")]
        public async Task<IActionResult> GetAllTickets(int companyId, [FromQuery] int? warehouseId)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (warehouseId.HasValue && warehouseId.Value <= 0)
                return BadRequest(new { message = "Invalid warehouse id." });

            try
            {
                var items = await _service.GetAllOutboundOrdersAsync(companyId, warehouseId);
                return Ok(items);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("tickets/{companyId:int}/{id:int}")]
        public async Task<IActionResult> GetTicketById(int companyId, int id)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (id <= 0) return BadRequest(new { message = "Invalid ticket id." });

            try
            {
                var item = await _service.GetOutboundOrderByIdAsync(companyId, id);
                return Ok(item);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("tickets/by-warehouse/{warehouseId:int}")]
        public async Task<IActionResult> GetTicketsByWarehouse(int warehouseId)
        {
            if (warehouseId <= 0) return BadRequest(new { message = "Invalid warehouse id." });

            try
            {
                var items = await _service.GetOutboundOrdersByWarehouseIdAsync(warehouseId);
                return Ok(items);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
        [HttpGet("get-outbound-orders-for-staff/{companyId:int}/{staffId:int}")]
        public async Task<IActionResult> GetOutboundTasksByStaff(int companyId, int staffId)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (staffId <= 0) return BadRequest(new { message = "Invalid staff id." });

            try
            {
                var items = await _service.GetOutboundOrdersByStaffAsync(companyId, staffId);
                return Ok(items);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("tickets/{ticketId:int}/items/locations")]
        public async Task<IActionResult> GetTicketItemAvailableLocations(int ticketId)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticket id." });

            try
            {
                var authError = EnsureRoleIn(new[] { 3, 4 }, "Only Manager (roleId=3) or Staff (roleId=4) can view outbound item locations.");
                if (authError != null) return authError;

                var result = await _service.GetOutboundOrderItemAvailableLocationsAsync(ticketId).ConfigureAwait(false);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("tickets/{ticketId:int}/items/selected-locations")]
        public async Task<IActionResult> GetTicketItemSelectedLocations(int ticketId)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticket id." });

            try
            {
                var authError = EnsureRoleIn(new[] { 3, 4 }, "Only Manager (roleId=3) or Staff (roleId=4) can view outbound selected locations.");
                if (authError != null) return authError;

                var result = await _service.GetOutboundOrderItemSelectedLocationsAsync(ticketId).ConfigureAwait(false);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        private IActionResult? EnsureRole(int requiredRole, string forbiddenMessage, string? superAdminMessage = null)
        {
            if (User?.Identity?.IsAuthenticated != true)
                return Unauthorized(new { message = "Authentication required." });

            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrWhiteSpace(roleClaim))
                return StatusCode(403, new { message = "Role claim is missing." });

            if (!int.TryParse(roleClaim, out var roleId))
                return StatusCode(403, new { message = "Invalid role claim." });

            if (roleId == 1 && !string.IsNullOrWhiteSpace(superAdminMessage))
                return StatusCode(403, new { message = superAdminMessage });

            if (roleId != requiredRole)
                return StatusCode(403, new { message = forbiddenMessage });

            return null;
        }

        private IActionResult? EnsureRoleIn(IEnumerable<int> allowedRoles, string forbiddenMessage)
        {
            if (User?.Identity?.IsAuthenticated != true)
                return Unauthorized(new { message = "Authentication required." });

            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrWhiteSpace(roleClaim))
                return StatusCode(403, new { message = "Role claim is missing." });

            if (!int.TryParse(roleClaim, out var roleId))
                return StatusCode(403, new { message = "Invalid role claim." });

            if (allowedRoles == null || !allowedRoles.Contains(roleId))
                return StatusCode(403, new { message = forbiddenMessage });

            return null;
        }
    }
}
