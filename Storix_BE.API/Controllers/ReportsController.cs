using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storix_BE.Service.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Reporting is company-scoped. Company Admin (role 2) and Manager (role 3) can create/view/export reports.
    [Authorize(Roles = "2,3")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportingService _reportingService;
        private readonly IUserService _userService;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            IReportingService reportingService,
            IUserService userService,
            ILogger<ReportsController> logger)
        {
            _reportingService = reportingService;
            _userService = userService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> CreateReport([FromBody] CreateReportApiRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

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
                LogCreateReportFailure(ex, request, effectiveCompanyId, caller?.Id, expectedClientError: true);
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                LogCreateReportFailure(ex, request, effectiveCompanyId, caller?.Id, expectedClientError: true);
                return BadRequest(new { message = ex.Message });
            }
            catch (DbUpdateException ex)
            {
                LogCreateReportFailure(ex, request, effectiveCompanyId, caller?.Id, expectedClientError: false);
                return HandleDatabaseException(ex, "create report");
            }
            catch (Exception ex)
            {
                LogCreateReportFailure(ex, request, effectiveCompanyId, caller?.Id, expectedClientError: false);
                return HandleUnexpectedException(ex, "create report");
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
            catch (DbUpdateException ex)
            {
                return HandleDatabaseException(ex, "load report");
            }
            catch (Exception ex)
            {
                return HandleUnexpectedException(ex, "load report");
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
            catch (DbUpdateException ex)
            {
                return HandleDatabaseException(ex, "list reports");
            }
            catch (Exception ex)
            {
                return HandleUnexpectedException(ex, "list reports");
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
            catch (DbUpdateException ex)
            {
                return HandleDatabaseException(ex, "export report PDF");
            }
            catch (Exception ex)
            {
                return HandleUnexpectedException(ex, "export report PDF");
            }
        }


        /// <summary>
        /// Resolves the authenticated caller and effective companyId for company-scoped report access.
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
                return (StatusCode(403, new { message = "Cross-company access denied. You can only access your own company." }), 0, null);

            if (!caller.CompanyId.HasValue || caller.CompanyId.Value <= 0)
                return (Unauthorized(new { message = "Missing companyId in token/user context." }), 0, null);

            return (null, caller.CompanyId.Value, caller);
        }

        private IActionResult HandleDatabaseException(DbUpdateException ex, string action)
        {
            var traceId = HttpContext.TraceIdentifier;
            var detail = GetInnermostMessage(ex);

            _logger.LogError(ex, "Database error while attempting to {Action}. TraceId={TraceId}", action, traceId);

            return StatusCode(500, new
            {
                message = $"Database error while attempting to {action}.",
                detail,
                traceId
            });
        }

        private IActionResult HandleUnexpectedException(Exception ex, string action)
        {
            var traceId = HttpContext.TraceIdentifier;

            _logger.LogError(ex, "Unexpected error while attempting to {Action}. TraceId={TraceId}", action, traceId);

            return StatusCode(500, new
            {
                message = $"Unexpected error while attempting to {action}.",
                detail = GetInnermostMessage(ex),
                traceId
            });
        }

        private void LogCreateReportFailure(
            Exception ex,
            CreateReportApiRequest request,
            int effectiveCompanyId,
            int? callerUserId,
            bool expectedClientError)
        {
            var traceId = HttpContext.TraceIdentifier;
            if (expectedClientError)
            {
                _logger.LogWarning(
                    ex,
                    "Create report validation failed. TraceId={TraceId}, EffectiveCompanyId={EffectiveCompanyId}, CallerUserId={CallerUserId}, ReportType={ReportType}, WarehouseId={WarehouseId}, ProductId={ProductId}, InventoryCountTicketId={InventoryCountTicketId}, TimeFrom={TimeFrom}, TimeTo={TimeTo}, ExceptionType={ExceptionType}, InnerExceptionType={InnerExceptionType}",
                    traceId,
                    effectiveCompanyId,
                    callerUserId,
                    request.ReportType,
                    request.WarehouseId,
                    request.ProductId,
                    request.InventoryCountTicketId,
                    request.TimeFrom,
                    request.TimeTo,
                    ex.GetType().FullName,
                    ex.InnerException?.GetType().FullName);
                return;
            }

            _logger.LogError(
                ex,
                "Create report failed. TraceId={TraceId}, EffectiveCompanyId={EffectiveCompanyId}, CallerUserId={CallerUserId}, ReportType={ReportType}, WarehouseId={WarehouseId}, ProductId={ProductId}, InventoryCountTicketId={InventoryCountTicketId}, TimeFrom={TimeFrom}, TimeTo={TimeTo}, ExceptionType={ExceptionType}, InnerExceptionType={InnerExceptionType}",
                traceId,
                effectiveCompanyId,
                callerUserId,
                request.ReportType,
                request.WarehouseId,
                request.ProductId,
                request.InventoryCountTicketId,
                request.TimeFrom,
                request.TimeTo,
                ex.GetType().FullName,
                ex.InnerException?.GetType().FullName);
        }

        private static string GetInnermostMessage(Exception ex)
        {
            var current = ex;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current.Message;
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
