using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/assignwarehouse")]
    [Authorize]
    public class WarehouseAssignmentsController : ControllerBase
    {
        private readonly IWarehouseAssignmentService _assignmentService;
        private readonly IUserService _userService;

        public WarehouseAssignmentsController(IWarehouseAssignmentService assignmentService, IUserService userService)
        {
            _assignmentService = assignmentService;
            _userService = userService;
        }

        private int? GetRoleIdFromToken()
        {
            var roleIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (string.IsNullOrEmpty(roleIdStr)) return null;
            return int.TryParse(roleIdStr, out var id) ? id : null;
        }

        private string? GetEmailFromToken()
        {
            return User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        }

        /// <summary>
        /// List all warehouse assignments within the current company. Company Administrator only.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAssignments()
        {
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            if (roleId == null || string.IsNullOrEmpty(email))
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null)
                    return Unauthorized();
                var assignments = await _assignmentService.GetAssignmentsByCompanyAsync(caller.CompanyId.Value, roleId.Value);
                return Ok(assignments);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        /// <summary>
        /// List Manager/Staff assigned to a specific warehouse (within your company).
        /// </summary>
        [HttpGet("warehouse/{warehouseId:int}")]
        public async Task<IActionResult> GetAssignmentsByWarehouse(int warehouseId)
        {
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            if (roleId == null || string.IsNullOrEmpty(email))
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null)
                    return Unauthorized();
                var assignments = await _assignmentService.GetAssignmentsByWarehouseAsync(
                    caller.CompanyId.Value,
                    roleId.Value,
                    warehouseId);
                return Ok(assignments);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Assign a warehouse to a Manager/Staff. Company Administrator only.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AssignWarehouse([FromBody] AssignWarehouseRequest request)
        {
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            if (roleId == null || string.IsNullOrEmpty(email))
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null)
                    return Unauthorized();
                var assignment = await _assignmentService.AssignWarehouseAsync(caller.CompanyId.Value, roleId.Value, request);
                return Ok(assignment);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Unassign a user from a warehouse. Company Administrator only.
        /// </summary>
        [HttpDelete]
        public async Task<IActionResult> UnassignWarehouse([FromQuery] int userId, [FromQuery] int warehouseId)
        {
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            if (roleId == null || string.IsNullOrEmpty(email))
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null)
                    return Unauthorized();
                var removed = await _assignmentService.UnassignWarehouseAsync(caller.CompanyId.Value, roleId.Value, userId, warehouseId);
                if (!removed)
                    return NotFound();
                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
