using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            var companyId = GetCompanyIdFromToken();
            var roleId = GetRoleIdFromToken();
            if (companyId == null || roleId == null)
                return Unauthorized();

            try
            {
                var users = await _userService.GetUsersByCompanyAsync(companyId.Value, roleId.Value);
                return Ok(users);
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
            var companyId = GetCompanyIdFromToken();
            var roleId = GetRoleIdFromToken();
            if (companyId == null || roleId == null)
                return Unauthorized();

            try
            {
                var users = await _userService.GetUsersByCompanyAsync(companyId.Value, roleId.Value);
                var user = users.FirstOrDefault(u => u.Id == id);
                if (user == null)
                    return NotFound();
                return Ok(user);
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
            var companyId = GetCompanyIdFromToken();
            var roleId = GetRoleIdFromToken();
            if (companyId == null || roleId == null)
                return Unauthorized();

            try
            {
                var user = await _userService.CreateUserAsync(companyId.Value, roleId.Value, request);
                return Ok(user);
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
        /// Update a user (Manager or Staff). Company Administrator only.
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
        {
            var companyId = GetCompanyIdFromToken();
            var roleId = GetRoleIdFromToken();
            if (companyId == null || roleId == null)
                return Unauthorized();

            try
            {
                var user = await _userService.UpdateUserAsync(id, companyId.Value, roleId.Value, request);
                if (user == null)
                    return NotFound();
                return Ok(user);
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
        /// Delete a user (Manager or Staff only; cannot delete Company Administrator). Company Administrator only.
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var companyId = GetCompanyIdFromToken();
            var roleId = GetRoleIdFromToken();
            var email = GetEmailFromToken();
            if (companyId == null || roleId == null || string.IsNullOrEmpty(email))
                return Unauthorized();

            try
            {
                var caller = await _userService.GetByEmailAsync(email);
                if (caller == null)
                    return Unauthorized();

                var deleted = await _userService.DeleteUserAsync(id, companyId.Value, roleId.Value, caller.Id);
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
        }
    }
}
