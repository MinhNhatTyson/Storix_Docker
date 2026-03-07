using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/inventory-count-tickets")]
    [Authorize(Roles = "3,4")]
    public class InventoryCountTicketsController : ControllerBase
    {
        private readonly IInventoryCountService _service;
        private readonly IUserService _userService;

        public InventoryCountTicketsController(IInventoryCountService service, IUserService userService)
        {
            _service = service;
            _userService = userService;
        }

        // Utility: allow manager to preview inventory products before creating ticket.
        [HttpGet("warehouses/{warehouseId:int}/inventory-products")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> ListInventoryProducts(int warehouseId, [FromQuery] int[]? productIds = null)
        {
            if (warehouseId <= 0) return BadRequest(new { message = "Invalid warehouseId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var products = await _service.ListInventoryProductsAsync(caller.CompanyId.Value, warehouseId, productIds)
                    .ConfigureAwait(false);
                return Ok(products);
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

        [HttpPost]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> CreateTicket([FromBody] CreateInventoryCountTicketRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var ticket = await _service.CreateTicketAsync(caller.CompanyId.Value, caller.Id, request).ConfigureAwait(false);
                return Ok(ticket);
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

        [HttpGet]
        public async Task<IActionResult> ListTickets([FromQuery] int? warehouseId = null, [FromQuery] string? status = null)
        {
            if (warehouseId.HasValue && warehouseId.Value <= 0)
                return BadRequest(new { message = "Invalid warehouseId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var items = await _service.ListTicketsAsync(caller.CompanyId.Value, warehouseId, status).ConfigureAwait(false);
                return Ok(items);
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

        [HttpGet("{ticketId:int}")]
        public async Task<IActionResult> GetTicketById(int ticketId)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticketId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var ticket = await _service.GetTicketByIdAsync(caller.CompanyId.Value, ticketId).ConfigureAwait(false);
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

        [HttpPost("{ticketId:int}/run")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> RunInventoryCheck(int ticketId)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticketId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.RunAsync(caller.CompanyId.Value, caller.Id, ticketId).ConfigureAwait(false);
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

        [HttpPost("{ticketId:int}/approve")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> Approve(int ticketId)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticketId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                await _service.ApproveAsync(caller.CompanyId.Value, caller.Id, ticketId).ConfigureAwait(false);
                return Ok(new { message = "Approved and inventory updated." });
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

        private async Task<Storix_BE.Domain.Models.User?> ResolveCallerAsync()
        {
            var email = GetEmailFromToken();
            if (string.IsNullOrWhiteSpace(email)) return null;
            return await _userService.GetByEmailAsync(email).ConfigureAwait(false);
        }

        private string? GetEmailFromToken()
        {
            return User.FindFirst(ClaimTypes.Email)?.Value;
        }
    }
}
