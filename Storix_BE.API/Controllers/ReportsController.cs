using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Reporting is company-scoped. Only Company Admin (role 2) can create/view/export reports.
    [Authorize(Roles = "2")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportingService _reportingService;
        private readonly IUserService _userService;

        public ReportsController(IReportingService reportingService, IUserService userService)
        {
            _reportingService = reportingService;
            _userService = userService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateReport([FromBody] CreateReportApiRequest request)
        {
            var (error, effectiveCompanyId, caller) = await ResolveCallerAsync(request.CompanyId);
            if (error != null) return error;

            try
            {
                var payload = new CreateReportRequest(
                    request.ReportType,
                    request.WarehouseId,
                    request.ProductId,
                    request.InventoryCountTicketId,
                    request.TimeFrom,
                    request.TimeTo);
                var result = await _reportingService.CreateReportAsync(effectiveCompanyId, caller!.Id, payload);
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

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetReportById(int id, [FromQuery] int? companyId = null)
        {
            var (error, effectiveCompanyId, _) = await ResolveCallerAsync(companyId);
            if (error != null) return error;

            try
            {
                var report = await _reportingService.GetReportAsync(effectiveCompanyId, id);
                if (report == null) return NotFound(new { message = "Report not found." });
                return Ok(report);
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

        [HttpGet]
        public async Task<IActionResult> ListReports(
            [FromQuery] int? companyId = null,
            [FromQuery] string? type = null,
            [FromQuery] int? warehouseId = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50)
        {
            var (error, effectiveCompanyId, _) = await ResolveCallerAsync(companyId);
            if (error != null) return error;

            try
            {
                var items = await _reportingService.ListReportsAsync(effectiveCompanyId, type, warehouseId, from, to, skip, take);
                return Ok(items);
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

        [HttpPost("{id:int}/export/pdf")]
        public async Task<IActionResult> ExportPdf(int id, [FromQuery] int? companyId = null)
        {
            var (error, effectiveCompanyId, _) = await ResolveCallerAsync(companyId);
            if (error != null) return error;

            try
            {
                var artifact = await _reportingService.ExportReportPdfAsync(effectiveCompanyId, id);
                return Ok(artifact);
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


        /// <summary>
        /// Resolves the authenticated caller and effective companyId for Company Admin scope.
        /// Returns (errorResult, effectiveCompanyId, caller). If errorResult is non-null, return it immediately.
        /// </summary>
        private async Task<(IActionResult? error, int effectiveCompanyId, Storix_BE.Domain.Models.User? caller)> ResolveCallerAsync(int? requestedCompanyId)
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
                return (Unauthorized(), 0, null);

            var caller = await _userService.GetByEmailAsync(email);
            if (caller == null)
                return (Unauthorized(), 0, null);

            if (requestedCompanyId.HasValue && requestedCompanyId.Value > 0 && caller.CompanyId != requestedCompanyId.Value)
                return (StatusCode(403, new { message = "Cross-company access denied. Company Administrator can only access its own company." }), 0, null);

            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return (Unauthorized(new { message = "Missing companyId in token/user context." }), 0, null);

            return (null, caller.CompanyId.Value, caller);
        }
    }

    public sealed record CreateReportApiRequest(
        string ReportType,
        int? WarehouseId,
        int? ProductId,
        DateTime TimeFrom,
        DateTime TimeTo,
        int? InventoryCountTicketId,
        int? CompanyId);
}
