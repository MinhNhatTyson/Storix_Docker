using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Domain.Exception;
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
        /// <summary>
        /// Create new warehouse (Company Administrator only). Route: POST /api/company-warehouses/{companyId}
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [Route("~/api/create-company-warehouses/{companyId:int}/")]
        public async Task<IActionResult> CreateWarehouse(int companyId, [FromBody] CreateWarehouseRequest request)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            try
            {
                var warehouse = await _assignmentService.CreateWarehouseAsync(companyId, request);
                return Ok(new { Id = warehouse.Id });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (BusinessRuleException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpGet("~/api/get-warehouse-structure/{companyId:int}/{warehouseId:int}")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> GetWarehouseStructure(int companyId, int warehouseId)
        {
            if (companyId <= 0) return BadRequest(new { message = "CompanyId is required." });
            if (warehouseId <= 0) return BadRequest(new { message = "WarehouseId is required." });

            try
            {
                var warehouse = await _assignmentService.GetWarehouseStructureAsync(companyId, warehouseId);

                var nodes = warehouse.NavNodes == null
                    ? new List<object>()
                    : warehouse.NavNodes
                        .Select(n => new
                        {
                            id = n.IdCode,
                            x = n.XCoordinate,
                            y = n.YCoordinate,
                            radius = n.Radius,
                            side = n.Side,
                            type = n.Type
                        })
                        .Cast<object>()
                        .ToList();

                var edges = warehouse.NavEdges == null
                    ? new List<object>()
                    : warehouse.NavEdges
                    .Select(e => new
                    {
                        id = e.IdCode,
                        from = e.NodeFromNavigation?.IdCode,
                        to = e.NodeToNavigation?.IdCode,
                        distance = e.Distance
                    })
                    .Cast<object>()
                        .ToList();

                var zones = warehouse.StorageZones?
                    .Select(z => (object)new
                    {
                        id = z.IdCode,
                        code = z.Code,
                        x = (double?)null,
                        y = (double?)null,
                        width = z.Width,
                        height = z.Height,
                        shelves = z.Shelves?.Select(s => (object)new
                        {
                            id = s.IdCode,
                            code = s.Code,
                            x = s.XCoordinate,
                            y = s.YCoordinate,
                            width = s.Width,
                            height = s.Height,

                            accessNodes = (s.ShelfNodes != null
                            ? s.ShelfNodes.Select(sn => (object)new
                            {
                                id = sn.IdCode ?? sn.Node?.IdCode,
                                side = sn.Node?.Side,
                                x = sn.Node?.XCoordinate,
                                y = sn.Node?.YCoordinate
                            }).ToList()
                            : new List<object>()),

                            levels = s.ShelfLevels != null
                            ? s.ShelfLevels.Select(l => (object)new
                            {
                                id = l.IdCode,
                                code = l.Code,
                                bins = l.ShelfLevelBins != null
                                    ? l.ShelfLevelBins.Select(b => (object)new
                                    {
                                        id = b.IdCode,
                                        code = b.Code
                                    }).ToList()
                                    : new List<object>()
                            }).ToList()
                            : new List<object>()
                        }).ToList() ?? new List<object>()
                    }).ToList() ?? new List<object>();

                var response = new
                {
                    width = warehouse.Width,
                    height = warehouse.Height,
                    zones = zones,
                    nodes = nodes,
                    edges = edges
                };

                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (BusinessRuleException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }

        }
    }
}
