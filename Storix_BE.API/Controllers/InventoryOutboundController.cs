using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;
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

        [HttpPost("create-outbound-request")]
        public async Task<IActionResult> CreateRequest([FromBody] CreateOutboundRequestRequest request)
        {
            try
            {
                var authError = EnsureRole(3, "Only Manager (roleId=3) can create outbound requests.");
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
                var authError = EnsureRole(2, "Only Company Administrator (roleId=2) can approve outbound requests.", "Super Admin (roleId=1) cannot approve outbound requests.");
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

                var ticket = await _service.CreateOutboundOrderFromRequestAsync(requestId, payload.CreatedBy, payload.StaffId, payload.Note);
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
        /// Update OutboundOrder (ticket) items — modify quantities or add items.
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

        [HttpPost("tickets/{ticketId}/confirm")]
        public async Task<IActionResult> ConfirmOutbound(int ticketId, [FromBody] ConfirmOutboundOrderRequest payload)
        {
            try
            {
                var authError = EnsureRole(3, "Only Manager (roleId=3) can confirm outbound orders.");
                if (authError != null) return authError;

                var ticket = await _service.ConfirmOutboundOrderAsync(ticketId, payload.PerformedBy);
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
    }
}
