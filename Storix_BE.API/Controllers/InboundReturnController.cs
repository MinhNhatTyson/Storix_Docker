
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;
using System;
using System.Threading.Tasks;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/inbound-returns")]
    [Authorize]
    public class InboundReturnController : ControllerBase
    {
        private readonly IInboundReturnService _service;

        public InboundReturnController(IInboundReturnService service)
        {
            _service = service;
        }

        // ────────────────────────────────────────────────────────────────────
        // POST /api/inbound-returns/{companyId}/{inboundOrderId}
        // Staff creates a return order for failed units from a QC-checked order.
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Staff flags failed units for return.
        ///
        /// PRE-CONDITIONS
        ///   • InboundOrder must be in status QUALITY_CHECK.
        ///   • Each item's ReturnQuantity must be ≤ its QC record's FailedQuantity.
        ///
        /// POST-CONDITIONS
        ///   • InboundReturnOrder created with status PENDING.
        ///   • InboundOrder status transitions to RETURN_PENDING.
        ///   • Managers are notified.
        /// </summary>
        [HttpPost("{companyId:int}/{inboundOrderId:int}")]
        [Authorize(Roles = "2,3,4")]
        public async Task<IActionResult> CreateReturnOrder(
            int companyId,
            int inboundOrderId,
            [FromBody] CreateReturnOrderRequest request)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "Invalid companyId." });
            if (inboundOrderId <= 0)
                return BadRequest(new { message = "Invalid inboundOrderId." });
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            try
            {
                var result = await _service.CreateReturnOrderAsync(
                    companyId, inboundOrderId, request);
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

        // ────────────────────────────────────────────────────────────────────
        // POST /api/inbound-returns/{companyId}/return-orders/{returnOrderId}/approve
        // Manager approves a PENDING return order.
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Manager/Admin approves a pending return order.
        ///
        /// PRE-CONDITIONS
        ///   • Return order must be in status PENDING.
        ///
        /// POST-CONDITIONS
        ///   • Return order status transitions to APPROVED.
        ///   • InboundOrder status transitions to RETURN_APPROVED.
        ///   • Staff who created the return is notified.
        /// </summary>
        [HttpPost("{companyId:int}/return-orders/{returnOrderId:int}/approve")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> ApproveReturnOrder(
            int companyId,
            int returnOrderId,
            [FromBody] ApproveReturnOrderRequest request)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "Invalid companyId." });
            if (returnOrderId <= 0)
                return BadRequest(new { message = "Invalid returnOrderId." });
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            try
            {
                var result = await _service.ApproveReturnOrderAsync(
                    companyId, returnOrderId, request);
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

        // ────────────────────────────────────────────────────────────────────
        // POST /api/inbound-returns/{companyId}/return-orders/{returnOrderId}/sent
        // Staff marks goods as physically shipped back to the supplier.
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Staff marks the return goods as physically shipped back.
        ///
        /// PRE-CONDITIONS
        ///   • Return order must be in status APPROVED.
        ///
        /// POST-CONDITIONS
        ///   • Return order status transitions to SENT.
        ///   • InboundOrder status transitions to RETURNED.
        ///   • Managers are notified.
        /// </summary>
        [HttpPost("{companyId:int}/return-orders/{returnOrderId:int}/sent")]
        [Authorize(Roles = "2,3,4")]
        public async Task<IActionResult> MarkReturnSent(
            int companyId,
            int returnOrderId,
            [FromBody] MarkReturnSentRequest request)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "Invalid companyId." });
            if (returnOrderId <= 0)
                return BadRequest(new { message = "Invalid returnOrderId." });
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            try
            {
                var result = await _service.MarkReturnSentAsync(
                    companyId, returnOrderId, request);
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

        // ────────────────────────────────────────────────────────────────────
        // GET /api/inbound-returns/{companyId}/return-orders/{returnOrderId}
        // ────────────────────────────────────────────────────────────────────

        [HttpGet("{companyId:int}/return-orders/{returnOrderId:int}")]
        [Authorize(Roles = "2,3,4")]
        public async Task<IActionResult> GetReturnOrderById(
            int companyId,
            int returnOrderId)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "Invalid companyId." });
            if (returnOrderId <= 0)
                return BadRequest(new { message = "Invalid returnOrderId." });

            try
            {
                var result = await _service.GetReturnOrderByIdAsync(
                    companyId, returnOrderId);
                return Ok(result);
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

        // ────────────────────────────────────────────────────────────────────
        // GET /api/inbound-returns/{companyId}/{inboundOrderId}
        // List all return orders for an inbound order.
        // ────────────────────────────────────────────────────────────────────

        [HttpGet("{companyId:int}/{inboundOrderId:int}")]
        [Authorize(Roles = "2,3,4")]
        public async Task<IActionResult> GetReturnOrdersByInboundOrder(
            int companyId,
            int inboundOrderId)
        {
            if (companyId <= 0)
                return BadRequest(new { message = "Invalid companyId." });
            if (inboundOrderId <= 0)
                return BadRequest(new { message = "Invalid inboundOrderId." });

            try
            {
                var result = await _service.GetReturnOrdersByInboundOrderAsync(
                    companyId, inboundOrderId);
                return Ok(result);
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
    }
}