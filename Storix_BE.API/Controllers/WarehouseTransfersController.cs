using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Domain.Models;
using Storix_BE.Service.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/warehouse-transfers")]
    [Authorize(Roles = "3,4")]
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
        public async Task<IActionResult> Create([FromBody] CreateTransferOrderRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.CreateDraftAsync(caller.CompanyId.Value, caller.Id, request).ConfigureAwait(false);
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

        [HttpPut("{transferOrderId:int}")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> UpdateDraft(int transferOrderId, [FromBody] UpdateTransferOrderRequest request)
        {
            if (transferOrderId <= 0) return BadRequest(new { message = "Invalid transferOrderId." });
            if (request == null) return BadRequest(new { message = "Request body is required." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.UpdateDraftAsync(caller.CompanyId.Value, caller.Id, transferOrderId, request).ConfigureAwait(false);
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

        [HttpPost("{transferOrderId:int}/items")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> AddItem(int transferOrderId, [FromBody] AddTransferOrderItemRequest request)
        {
            if (transferOrderId <= 0) return BadRequest(new { message = "Invalid transferOrderId." });
            if (request == null) return BadRequest(new { message = "Request body is required." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.AddItemAsync(caller.CompanyId.Value, caller.Id, transferOrderId, request).ConfigureAwait(false);
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

        [HttpPut("{transferOrderId:int}/items/{itemId:int}")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> UpdateItem(int transferOrderId, int itemId, [FromBody] UpdateTransferOrderItemRequest request)
        {
            if (transferOrderId <= 0 || itemId <= 0) return BadRequest(new { message = "Invalid transferOrderId or itemId." });
            if (request == null) return BadRequest(new { message = "Request body is required." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.UpdateItemAsync(caller.CompanyId.Value, caller.Id, transferOrderId, itemId, request).ConfigureAwait(false);
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

        [HttpDelete("{transferOrderId:int}/items/{itemId:int}")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> RemoveItem(int transferOrderId, int itemId)
        {
            if (transferOrderId <= 0 || itemId <= 0) return BadRequest(new { message = "Invalid transferOrderId or itemId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.RemoveItemAsync(caller.CompanyId.Value, caller.Id, transferOrderId, itemId).ConfigureAwait(false);
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

        [HttpPost("{transferOrderId:int}/submit")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> Submit(int transferOrderId)
        {
            return await ExecuteManagerTransition(transferOrderId, (companyId, userId) => _service.SubmitAsync(companyId, userId, transferOrderId)).ConfigureAwait(false);
        }

        [HttpPost("{transferOrderId:int}/approve")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> Approve(int transferOrderId)
        {
            return await ExecuteManagerTransition(transferOrderId, (companyId, userId) => _service.ApproveAsync(companyId, userId, transferOrderId)).ConfigureAwait(false);
        }

        [HttpPost("{transferOrderId:int}/reject")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> Reject(int transferOrderId, [FromBody] RejectTransferOrderRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest(new { message = "Reason is required." });

            return await ExecuteManagerTransition(transferOrderId, (companyId, userId) => _service.RejectAsync(companyId, userId, transferOrderId, request.Reason)).ConfigureAwait(false);
        }

        [HttpPost("{transferOrderId:int}/start-picking")]
        [Authorize(Roles = "4")]
        public async Task<IActionResult> StartPicking(int transferOrderId)
        {
            return await ExecuteStaffTransition(transferOrderId, (companyId, userId) => _service.StartPickingAsync(companyId, userId, transferOrderId)).ConfigureAwait(false);
        }

        [HttpPost("{transferOrderId:int}/mark-packed")]
        [Authorize(Roles = "4")]
        public async Task<IActionResult> MarkPacked(int transferOrderId)
        {
            return await ExecuteStaffTransition(transferOrderId, (companyId, userId) => _service.MarkPackedAsync(companyId, userId, transferOrderId)).ConfigureAwait(false);
        }

        [HttpPost("{transferOrderId:int}/ship")]
        [Authorize(Roles = "4")]
        public async Task<IActionResult> Ship(int transferOrderId)
        {
            return await ExecuteStaffTransition(transferOrderId, (companyId, userId) => _service.ShipAsync(companyId, userId, transferOrderId)).ConfigureAwait(false);
        }

        [HttpPost("{transferOrderId:int}/receive")]
        [Authorize(Roles = "4")]
        public async Task<IActionResult> Receive(int transferOrderId, [FromBody] ReceiveTransferOrderRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            return await ExecuteStaffTransition(transferOrderId, (companyId, userId) => _service.ReceiveAsync(companyId, userId, transferOrderId, request)).ConfigureAwait(false);
        }

        [HttpPost("{transferOrderId:int}/quality-check")]
        [Authorize(Roles = "4")]
        public async Task<IActionResult> QualityCheck(int transferOrderId, [FromBody] TransferQualityCheckRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            return await ExecuteStaffTransition(transferOrderId, (companyId, userId) => _service.QualityCheckAsync(companyId, userId, transferOrderId, request)).ConfigureAwait(false);
        }

        [HttpPost("{transferOrderId:int}/cancel")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> Cancel(int transferOrderId, [FromBody] CancelTransferOrderRequest? request)
        {
            return await ExecuteManagerTransition(transferOrderId, (companyId, userId) => _service.CancelAsync(companyId, userId, transferOrderId, request?.Reason)).ConfigureAwait(false);
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

        [HttpGet("{transferOrderId:int}/availability")]
        public async Task<IActionResult> CheckAvailability(int transferOrderId)
        {
            if (transferOrderId <= 0) return BadRequest(new { message = "Invalid transferOrderId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await _service.CheckAvailabilityAsync(caller.CompanyId.Value, transferOrderId).ConfigureAwait(false);
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

        private async Task<IActionResult> ExecuteManagerTransition(int transferOrderId, Func<int, int, Task<TransferOrderDetailDto>> transition)
        {
            if (transferOrderId <= 0) return BadRequest(new { message = "Invalid transferOrderId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await transition(caller.CompanyId.Value, caller.Id).ConfigureAwait(false);
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

        private async Task<IActionResult> ExecuteStaffTransition(int transferOrderId, Func<int, int, Task<TransferOrderDetailDto>> transition)
        {
            if (transferOrderId <= 0) return BadRequest(new { message = "Invalid transferOrderId." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var result = await transition(caller.CompanyId.Value, caller.Id).ConfigureAwait(false);
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

    public sealed record RejectTransferOrderRequest(string Reason);
    public sealed record CancelTransferOrderRequest(string? Reason);
}