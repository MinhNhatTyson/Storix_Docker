using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Repository.DTO;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/company-warehouses/{companyId:int}/assignments")]
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

        private int? GetCompanyIdFromToken()
        {
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            if (string.IsNullOrEmpty(companyIdStr)) return null;
            return int.TryParse(companyIdStr, out var id) ? id : null;
        }
        /// <summary>
        /// List all warehouses within the current company. Company Administrator only.
        /// </summary>
        [HttpGet("/api/company-warehouses/{companyId:int}/warehouses")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> GetWarehouses(int companyId)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            if (roleId == null || string.IsNullOrEmpty(email))
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null)
                    return Unauthorized();
                if (caller.CompanyId.Value != companyId)
                    return Forbid();

                var warehouses = await _assignmentService.GetWarehousesByCompanyAsync(companyId, roleId.Value);
                return Ok(warehouses.Select(w => new WarehouseSummaryDto(
                    w.Id,
                    w.CompanyId,
                    w.Name,
                    w.Status
                )));
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        /// <summary>
        /// List all warehouse assignments within the current company. Company Administrator only.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAssignments(int companyId)
        {
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            var tokenCompanyId = GetCompanyIdFromToken();
            if (roleId == null || string.IsNullOrEmpty(email) || tokenCompanyId == null || tokenCompanyId.Value != companyId)
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null || caller.CompanyId.Value != companyId)
                    return Unauthorized();
                var assignments = await _assignmentService.GetAssignmentsByCompanyAsync(companyId, roleId.Value);
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
        public async Task<IActionResult> GetAssignmentsByWarehouse(int companyId, int warehouseId)
        {
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            var tokenCompanyId = GetCompanyIdFromToken();
            if (roleId == null || string.IsNullOrEmpty(email) || tokenCompanyId == null || tokenCompanyId.Value != companyId)
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null || caller.CompanyId.Value != companyId)
                    return Unauthorized();
                var assignments = await _assignmentService.GetAssignmentsByWarehouseAsync(
                    companyId,
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
        public async Task<IActionResult> AssignWarehouse(int companyId, [FromBody] AssignWarehouseRequest request)
        {
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            var tokenCompanyId = GetCompanyIdFromToken();
            if (roleId == null || string.IsNullOrEmpty(email) || tokenCompanyId == null || tokenCompanyId.Value != companyId)
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null || caller.CompanyId.Value != companyId)
                    return Unauthorized();
                var assignment = await _assignmentService.AssignWarehouseAsync(companyId, roleId.Value, request);
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
        public async Task<IActionResult> UnassignWarehouse(int companyId, [FromQuery] int userId, [FromQuery] int warehouseId)
        {
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            var tokenCompanyId = GetCompanyIdFromToken();
            if (roleId == null || string.IsNullOrEmpty(email) || tokenCompanyId == null || tokenCompanyId.Value != companyId)
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller?.CompanyId == null || caller.CompanyId.Value != companyId)
                    return Unauthorized();
                var removed = await _assignmentService.UnassignWarehouseAsync(companyId, roleId.Value, userId, warehouseId);
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
