using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Storix_BE.API.Controllers
{
    /// <summary>
    /// Barcode-scan-to-receive flow for inbound orders.
    /// Replaces the legacy quality-check submission step.
    ///
    /// Typical staff workflow (mobile):
    ///   1. POST   .../session/start    — open session
    ///   2. POST   .../session/scan     — scan one unit (repeat per unit)
    ///   3. GET    .../session          — check progress at any time
    ///   4. POST   .../session/finalize — submit QC + close session
    ///      (or DELETE .../session      — discard without saving)
    /// </summary>
    [ApiController]
    [Route("api/inbound-barcode/{inboundOrderId:int}")]
    [Authorize(Roles = "2,3,4")]
    public class BarcodeScanController : ControllerBase
    {
        private readonly IBarcodeScanService _service;

        public BarcodeScanController(IBarcodeScanService service)
        {
            _service = service;
        }

        // ── POST .../session/start ────────────────────────────────────────────

        /// <summary>
        /// Opens a new barcode scan session for the given inbound order.
        /// The order must be in "Waiting for payment" / "WAITING_RECEIPT" status.
        /// Only one active session per order is allowed.
        /// </summary>
        [HttpPost("session/start")]
        public async Task<IActionResult> StartSession(
            int inboundOrderId,
            [FromBody] StartBarcodeSessionRequest request)
        {
            if (inboundOrderId <= 0)
                return BadRequest(new { message = "Invalid inboundOrderId." });
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            var companyId = GetCompanyIdFromClaims();
            if (!companyId.HasValue)
                return StatusCode(403, new { message = "CompanyId claim is missing." });

            try
            {
                var session = await _service.StartSessionAsync(companyId.Value, inboundOrderId, request);
                return Ok(session);
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

        // ── GET .../session ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the current session state (scan progress per product).
        /// Returns 404 when no active session exists for this order.
        /// </summary>
        [HttpGet("session")]
        public async Task<IActionResult> GetSession(int inboundOrderId)
        {
            if (inboundOrderId <= 0)
                return BadRequest(new { message = "Invalid inboundOrderId." });

            var companyId = GetCompanyIdFromClaims();
            if (!companyId.HasValue)
                return StatusCode(403, new { message = "CompanyId claim is missing." });

            try
            {
                var session = await _service.GetSessionAsync(companyId.Value, inboundOrderId);
                if (session is null)
                    return NotFound(new { message = $"No active barcode scan session found for order {inboundOrderId}." });

                return Ok(session);
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

        // ── POST .../session/scan ─────────────────────────────────────────────

        /// <summary>
        /// Records one unit scan for the product identified by the given SKU.
        ///
        /// Responses:
        ///   200  — scan accepted (check WarningMessage for over-scan alert)
        ///   400  — SKU not found in the order, or session not active
        /// </summary>
        [HttpPost("session/scan")]
        public async Task<IActionResult> Scan(
            int inboundOrderId,
            [FromBody] ScanBarcodeRequest request)
        {
            if (inboundOrderId <= 0)
                return BadRequest(new { message = "Invalid inboundOrderId." });
            if (request == null || string.IsNullOrWhiteSpace(request.Sku))
                return BadRequest(new { message = "SKU is required." });

            var companyId = GetCompanyIdFromClaims();
            if (!companyId.HasValue)
                return StatusCode(403, new { message = "CompanyId claim is missing." });

            try
            {
                var result = await _service.ScanAsync(companyId.Value, inboundOrderId, request);
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

        // ── POST .../session/finalize ─────────────────────────────────────────

        /// <summary>
        /// Finalizes the scan session:
        ///   - Persists QC records (replaces the legacy SubmitQualityCheckAsync).
        ///   - Transitions the InboundOrder to QUALITY_CHECK status.
        ///   - Removes the in-memory session.
        ///
        /// The optional <c>QcOverrides</c> array lets staff supply corrected quantities
        /// or failure reasons for products that were over-scanned or visually damaged.
        /// Products not included in overrides are derived from the scan count.
        /// </summary>
        [HttpPost("session/finalize")]
        public async Task<IActionResult> FinalizeSession(
            int inboundOrderId,
            [FromBody] FinalizeBarcodeSessionRequest request)
        {
            if (inboundOrderId <= 0)
                return BadRequest(new { message = "Invalid inboundOrderId." });
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            var companyId = GetCompanyIdFromClaims();
            if (!companyId.HasValue)
                return StatusCode(403, new { message = "CompanyId claim is missing." });

            try
            {
                var result = await _service.FinalizeSessionAsync(companyId.Value, inboundOrderId, request);
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

        // ── DELETE .../session ────────────────────────────────────────────────

        /// <summary>
        /// Discards the active session without persisting any QC data.
        /// Use when staff wants to restart scanning from scratch.
        /// </summary>
        [HttpDelete("session")]
        public async Task<IActionResult> DiscardSession(int inboundOrderId)
        {
            if (inboundOrderId <= 0)
                return BadRequest(new { message = "Invalid inboundOrderId." });

            var companyId = GetCompanyIdFromClaims();
            if (!companyId.HasValue)
                return StatusCode(403, new { message = "CompanyId claim is missing." });

            try
            {
                await _service.DiscardSessionAsync(companyId.Value, inboundOrderId);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private int? GetCompanyIdFromClaims()
        {
            var claim = User.Claims.FirstOrDefault(c =>
                c.Type.Equals("companyId", StringComparison.OrdinalIgnoreCase) ||
                c.Type.Equals("CompanyId", StringComparison.OrdinalIgnoreCase) ||
                c.Type.Equals("company_id", StringComparison.OrdinalIgnoreCase));

            return claim != null && int.TryParse(claim.Value, out var id) && id > 0 ? id : null;
        }
    }
}