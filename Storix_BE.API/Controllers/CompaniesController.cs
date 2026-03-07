using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Storix_BE.Domain.Models;
using Storix_BE.Service.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompaniesController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IConfiguration _config;

        public CompaniesController(IUserService userService, IConfiguration config)
        {
            _userService = userService;
            _config = config;
        }

        /// <summary>
        /// Company self-registration endpoint.
        /// Creates a new company and assigns the caller as Company Administrator.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> RegisterCompany([FromBody] CompanyRegisterRequest request)
        {
            try
            {
                var user = await _userService.RegisterCompanyAsync(
                    request.CompanyName,
                    request.BusinessCode,
                    request.Address,
                    request.ContactEmail,
                    request.ContactPhone,
                    request.AdminFullName,
                    request.AdminEmail,
                    request.AdminPhone,
                    request.Password);

                var token = GenerateJsonWebToken(user);

                return Ok(new
                {
                    Token = token,
                    RoleId = user.RoleId,
                    CompanyId = user.CompanyId
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private string GenerateJsonWebToken(User user)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"],
                new Claim[]
                {
                    new(ClaimTypes.Email, user.Email ?? string.Empty),
                    new(ClaimTypes.Role, user.RoleId.ToString() ?? string.Empty),
                    new("CompanyId", (user.CompanyId?.ToString() ?? string.Empty)),
                },
                expires: DateTime.Now.AddMinutes(120),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public sealed record CompanyRegisterRequest(
        string CompanyName,
        string? BusinessCode,
        string? Address,
        string? ContactEmail,
        string? ContactPhone,
        string AdminFullName,
        string AdminEmail,
        string? AdminPhone,
        string Password);
}

