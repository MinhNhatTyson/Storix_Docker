using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Models;
using Storix_BE.Service.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/warehouse-transfers")]
    [Authorize(Roles = "2,3,4")]
    public class WarehouseTransfersController : ControllerBase
    {
        private readonly IWarehouseTransferService _service;
        private readonly IUserService _userService;

        public WarehouseTransfersController(IWarehouseTransferService service, IUserService userService)
        {
            _service = service;
            _userService = userService;
        }

        [HttpPost]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> CreateTransfer([FromBody] CreateTransferOrderRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.CreateAsync(caller.CompanyId.Value, caller.Id, request).ConfigureAwait(false);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (DbUpdateException ex)
            {
                return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("{transferOrderId:int}/approve")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> ApproveTransfer(int transferOrderId, [FromBody] ApproveTransferOrderRequest? request)
        {
            if (transferOrderId <= 0) return BadRequest(new { message = "Invalid transferOrderId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.ApproveAsync(caller.CompanyId.Value, caller.Id, transferOrderId, request?.ReceiverStaffId).ConfigureAwait(false);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (DbUpdateException ex)
            {
                return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? sourceWarehouseId, [FromQuery] int? destinationWarehouseId, [FromQuery] string? status)
        {
            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.GetAllAsync(caller.CompanyId.Value, sourceWarehouseId, destinationWarehouseId, status).ConfigureAwait(false);
                return Ok(result);
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

        [HttpGet("warehouse/{warehouseId:int}")]
        public async Task<IActionResult> GetBySourceWarehouseId(int warehouseId, [FromQuery] string? status)
        {
            if (warehouseId <= 0) return BadRequest(new { message = "Invalid warehouseId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.GetAllBySourceWarehouseAsync(caller.CompanyId.Value, warehouseId, status).ConfigureAwait(false);
                return Ok(result);
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

        [HttpGet("{transferOrderId:int}")]
        public async Task<IActionResult> GetById(int transferOrderId)
        {
            if (transferOrderId <= 0) return BadRequest(new { message = "Invalid transferOrderId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.GetByIdAsync(caller.CompanyId.Value, transferOrderId).ConfigureAwait(false);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("warehouse/{warehouseId:int}/availability")]
        public async Task<IActionResult> GetByWarehouseAvailability(int warehouseId)
        {
            if (warehouseId <= 0) return BadRequest(new { message = "Invalid warehouseId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.CheckAvailabilityAsync(caller.CompanyId.Value, warehouseId).ConfigureAwait(false);
                return Ok(result);
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

        private async Task<User?> ResolveCallerAsync()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email)) return null;
            return await _userService.GetByEmailAsync(email).ConfigureAwait(false);
        }
    }
}
