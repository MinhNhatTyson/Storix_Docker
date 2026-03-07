using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/inventory-count-items")]
    [Authorize(Roles = "3,4")]
    public class InventoryCountItemsController : ControllerBase
    {
        private readonly IInventoryCountService _service;
        private readonly IUserService _userService;

        public InventoryCountItemsController(IInventoryCountService service, IUserService userService)
        {
            _service = service;
            _userService = userService;
        }

        [HttpPatch("{itemId:int}")]
        public async Task<IActionResult> UpdateCountedQuantity(int itemId, [FromBody] UpdateInventoryCountItemRequest request)
        {
            if (itemId <= 0) return BadRequest(new { message = "Invalid itemId." });
            if (request == null) return BadRequest(new { message = "Request body is required." });

            var caller = await ResolveCallerAsync().ConfigureAwait(false);
            if (caller == null) return Unauthorized(new { message = "Unauthorized. Invalid or missing token." });
            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return BadRequest(new { message = "User does not belong to any company. Access denied." });

            try
            {
                var updated = await _service.UpdateCountedQuantityAsync(caller.CompanyId.Value, caller.Id, caller.RoleId ?? 0, itemId, request).ConfigureAwait(false);
                return Ok(updated);
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
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email)) return null;
            return await _userService.GetByEmailAsync(email).ConfigureAwait(false);
        }
    }
}
