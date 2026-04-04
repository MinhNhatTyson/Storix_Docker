using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Domain.Exception;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Service.Implementation;
using Storix_BE.Service.Interfaces;
using System.Linq;
using AssignWarehouseRequest = Storix_BE.Service.Interfaces.AssignWarehouseRequest;
using CreateWarehouseRequest = Storix_BE.Service.Interfaces.CreateWarehouseRequest;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/company-warehouses/{companyId:int}/assignments")]
    [Authorize]
    public class WarehouseAssignmentsController : ControllerBase
    {
        private readonly IWarehouseAssignmentService _assignmentService;
        private readonly IUserService _userService;
        private readonly IInventoryOutboundService _inventoryOutboundService;

        public WarehouseAssignmentsController(
            IWarehouseAssignmentService assignmentService,
            IUserService userService,
            IInventoryOutboundService inventoryOutboundService)
        {
            _assignmentService = assignmentService;
            _userService = userService;
            _inventoryOutboundService = inventoryOutboundService;
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
        /// List all warehouses within the current company. Company Administrator only.
        /// </summary>
        [HttpGet("/api/company-warehouses/{companyId:int}/warehouses")]
        [Authorize(Roles = "2,3,4")]
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

        [HttpGet("/api/company-warehouses/{companyId:int}/warehouses/{warehouseId:int}/inventory")]
        [Authorize(Roles = "2,3,4")]
        public async Task<IActionResult> GetWarehouseInventory(int companyId, int warehouseId)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            if (warehouseId <= 0)
                return BadRequest(new { message = "WarehouseId is required." });

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

                var items = await _inventoryOutboundService.GetWarehouseInventoryAsync(companyId, warehouseId);
                return Ok(items);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
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
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// List all warehouse assignments within the current company. Company Administrator only.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAssignments(int companyId)
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
                var assignments = await _assignmentService.GetAssignmentsByCompanyAsync(companyId, roleId.Value);
                return Ok(assignments.Select(MapAssignment));
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
        /// List Manager/Staff assigned to a specific warehouse (within your company).
        /// </summary>
        [HttpGet("warehouse/{warehouseId:int}")]
        public async Task<IActionResult> GetAssignmentsByWarehouse(int companyId, int warehouseId)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            if (warehouseId <= 0)
                return BadRequest(new { message = "WarehouseId is required." });
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
                var assignments = await _assignmentService.GetAssignmentsByWarehouseAsync(
                    companyId,
                    roleId.Value,
                    warehouseId);
                return Ok(assignments.Select(MapAssignment));
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
        /// Assign a warehouse to a Manager/Staff. Company Administrator only.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AssignWarehouse(int companyId, [FromBody] AssignWarehouseRequest request)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            if (request == null || request.UserId <= 0 || request.WarehouseId <= 0)
                return BadRequest(new { message = "UserId and WarehouseId are required." });
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
                var assignment = await _assignmentService.AssignWarehouseAsync(companyId, roleId.Value, request);
                return Ok(MapAssignment(assignment));
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
        /// Unassign a user from a warehouse. Company Administrator only.
        /// </summary>
        [HttpDelete]
        public async Task<IActionResult> UnassignWarehouse(int companyId, [FromQuery] int userId, [FromQuery] int warehouseId)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            if (userId <= 0 || warehouseId <= 0)
                return BadRequest(new { message = "UserId and WarehouseId are required." });
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
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private static WarehouseAssignmentResponseDto MapAssignment(WarehouseAssignment assignment)
        {
            return new WarehouseAssignmentResponseDto(
                assignment.Id,
                assignment.UserId,
                assignment.WarehouseId,
                assignment.RoleInWarehouse,
                assignment.AssignedAt,
                assignment.User == null
                    ? null
                    : new UserSummaryDto(
                        assignment.User.Id,
                        assignment.User.CompanyId,
                        assignment.User.FullName,
                        assignment.User.Email,
                        assignment.User.Phone,
                        assignment.User.RoleId,
                        assignment.User.Role?.Name,
                        assignment.User.Status,
                        assignment.User.CreatedAt,
                        assignment.User.UpdatedAt),
                assignment.Warehouse == null
                    ? null
                    : new WarehouseSummaryDto(
                        assignment.Warehouse.Id,
                        assignment.Warehouse.CompanyId,
                        assignment.Warehouse.Name,
                        assignment.Warehouse.Status)
            );
        }
        /// <summary>
        /// Create new warehouse (Company Administrator only). Route: POST /api/company-warehouses/{companyId}
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "2")]
        [Route("~/api/update-company-warehouse/{companyId:int}/structure/{warehouseId:int}")]
        public async Task<IActionResult> UpdateWarehouseStructure(int companyId, int warehouseId, [FromBody] CreateWarehouseRequest request)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            if (warehouseId <= 0)
                return BadRequest(new { message = "WarehouseId is required." });
            if (request == null)
                return BadRequest(new { message = "Request body is required." });
            try
            {
                var warehouse = await _assignmentService.UpdateWarehouseStructureAsync(companyId, warehouseId, request);
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

        /// <summary>
        /// Create simple warehouse (Company Administrator only).
        /// Creates a warehouse with basic metadata (name, address, description, status) and optionally assigns a manager.
        /// Route: POST /api/company-warehouses/{companyId}
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "2")]
        [Route("~/api/create-company-warehouse/{companyId:int}")]
        public async Task<IActionResult> CreateSimpleWarehouse(int companyId, [FromBody] CreateSimpleWarehouseRequest request)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            if (request == null)
                return BadRequest(new { message = "Request body is required." });
            try
            {
                var warehouse = await _assignmentService.CreateSimpleWarehouseAsync(companyId, request);
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
        [Authorize(Roles = "2,3,4")]
        public async Task<IActionResult> GetWarehouseStructure(int companyId, int warehouseId)
        {
            if (companyId <= 0) return BadRequest(new { message = "CompanyId is required." });
            if (warehouseId <= 0) return BadRequest(new { message = "WarehouseId is required." });

            try
            {
                var warehouse = await _assignmentService.GetWarehouseStructureAsync(companyId, warehouseId);

                // Find the start node (type == "start")
                var startNode = warehouse.NavNodes?.FirstOrDefault(n => n.Type == "start");
                double? startX = startNode?.XCoordinate;
                double? startY = startNode?.YCoordinate;

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
                        x = z.XCoordinate,
                        y = z.YCoordinate,
                        width = z.Width,
                        height = z.Height,
                        length = z.Length,
                        IsEsd = z.IsEsd,
                        IsMsd = z.IsMsd,
                        IsCold = z.IsCold,
                        IsVulnerable = z.IsVulnerable,
                        IsHighValue = z.IsHighValue,
                        shelves = z.Shelves?.Select(s =>
                        {
                            // compute actual shelf coordinate = shelf coordinate + zone coordinate
                            var actualX = z.XCoordinate + s.XCoordinate;
                            var actualY = z.YCoordinate + s.YCoordinate;

                            // compute distance from start node if start exists
                            double? distanceFromStart = null;
                            if (startX.HasValue && startY.HasValue)
                            {
                                var dx = actualX - startX.Value;
                                var dy = actualY - startY.Value;
                                distanceFromStart = Math.Sqrt((double)(dx * dx + dy * dy));
                            }

                            return (object)new
                            {
                                id = s.IdCode,
                                code = s.Code,
                                x = s.XCoordinate,
                                y = s.YCoordinate,
                                width = s.Width,
                                height = s.Height,
                                length = s.Length,
                                distanceFromStart = distanceFromStart,
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
                                            code = b.Code,
                                            status = b.Status,
                                            percentage = b.Percentage,
                                            width = b.Width,
                                            height = b.Height,
                                            length = b.Length,
                                            productId = b.Inventory?.ProductId
                                        }).ToList()
                                        : new List<object>()
                                }).ToList()
                                : new List<object>()
                            };
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
        /// <summary>
        /// Delete a warehouse and all related structure (Company Administrator only).
        /// Route: DELETE /api/company-warehouses/{companyId}/warehouse/{warehouseId}
        /// </summary>
        [HttpDelete]
        [Authorize(Roles = "2")]
        [Route("~/api/delete-company-warehouses/{companyId:int}/warehouse/{warehouseId:int}")]
        public async Task<IActionResult> DeleteWarehouse(int companyId, int warehouseId)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "CompanyId is required." });
            if (warehouseId <= 0)
                return BadRequest(new { message = "WarehouseId is required." });
            try
            {
                var result = await _assignmentService.DeleteWarehouseAsync(companyId, warehouseId);
                if (!result) return NotFound();
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
