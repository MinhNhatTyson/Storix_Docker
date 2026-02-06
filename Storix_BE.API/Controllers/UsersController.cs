using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Domain.Exception;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Service.Interfaces;
using CreateUserRequest = Storix_BE.Service.Interfaces.CreateUserRequest;
using UpdateUserRequest = Storix_BE.Service.Interfaces.UpdateUserRequest;

namespace Storix_BE.API.Controllers
{
    /// <summary>
    /// CRUD accounts within the company. Only Company Administrator can use these endpoints.
    /// Manager and Staff cannot register; they are created by Company Administrator.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPut("update-profile/{userId}")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateProfile(int userId, [FromForm] UpdateProfileDto dto)
        {
            try
            {
                var updatedUser = await _userService.UpdateProfileAsync(userId, dto);
                return Ok(MapUser(updatedUser));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpGet("get-user-profile/{userId}")]
        public async Task<IActionResult> GetUserProfile(int userId)
        {
            try
            {
                var profile = await _userService.GetUser(userId);
                if (profile == null) return NotFound();
                return Ok(profile);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        private int? GetCompanyIdFromToken()
        {
            var companyIdStr = User.FindFirst("CompanyId")?.Value;
            if (string.IsNullOrEmpty(companyIdStr)) return null;
            return int.TryParse(companyIdStr, out var id) ? id : null;
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
        /// List all users in the current company. Company Administrator only.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetUsers()
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
                var users = await _userService.GetUsersForCallerAsync(caller.Id, roleId.Value);
                return Ok(users.Select(MapUser));
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        /// <summary>
        /// Get a user by id (must belong to your company). Company Administrator only.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetUser(int id)
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
                var targetUser = await _userService.GetUserByIdAsync(id);
                if (targetUser == null)
                    return NotFound();
                if (targetUser.CompanyId != caller.CompanyId.Value)
                    return Unauthorized();
                return Ok(MapUser(targetUser));
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        /// <summary>
        /// Create a new user (Manager or Staff only). Company Administrator only.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
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
                var user = await _userService.CreateUserAsync(caller.Id, roleId.Value, request);
                return Ok(MapUser(user));
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
        }

        /// <summary>
        /// Update a user (Manager or Staff). Company Administrator only.
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
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
                var targetUser = await _userService.GetUserByIdAsync(id);
                if (targetUser == null)
                    return NotFound();
                if (targetUser.CompanyId != caller.CompanyId.Value)
                    return Unauthorized();

                var user = await _userService.UpdateUserAsync(id, caller.Id, roleId.Value, request);
                if (user == null)
                    return NotFound();
                return Ok(MapUser(user));
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
        }

        /// <summary>
        /// Delete a user (Manager or Staff only; cannot delete Company Administrator). Company Administrator only.
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUser(int id)
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

                var targetUser = await _userService.GetUserByIdAsync(id);
                if (targetUser == null)
                    return NotFound();
                if (targetUser.CompanyId != caller.CompanyId.Value)
                    return Unauthorized();

                var deleted = await _userService.DeleteUserAsync(id, caller.Id, roleId.Value);
                if (!deleted)
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
            catch (BusinessRuleException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
        }

        private static UserResponseDto MapUser(User user)
        {
            var assignment = user.WarehouseAssignments?
                .OrderByDescending(a => a.AssignedAt)
                .FirstOrDefault();

            return new UserResponseDto(
                user.Id,
                user.CompanyId,
                user.FullName,
                user.Email,
                user.Phone,
                user.RoleId,
                user.Role?.Name,
                assignment?.WarehouseId,
                assignment?.Warehouse?.Name,
                user.Status,
                user.CreatedAt,
                user.UpdatedAt
            );
        }
    }
}
