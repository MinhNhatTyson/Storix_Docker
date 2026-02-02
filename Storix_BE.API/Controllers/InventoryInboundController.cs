using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryInboundController : ControllerBase
    {
        private readonly IInventoryInboundService _service;

        public InventoryInboundController(IInventoryInboundService service)
        {
            _service = service;
        }
        [HttpPost("create-inbound-request")]
        public async Task<IActionResult> CreateRequest([FromBody] CreateInboundRequestRequest request)
        {
            try
            {
                var inboundRequest = await _service.CreateInboundRequestAsync(request);
                return Ok(inboundRequest);
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
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPut("update-inbound-request/{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateInboundRequestStatusRequest request)
        {
            try
            {
                var inboundRequest = await _service.UpdateInboundRequestStatusAsync(id, request.ApproverId, request.Status);
                return Ok(inboundRequest);
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
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("create-inbound-ticket/{requestId}/tickets")]
        public async Task<IActionResult> CreateTicketFromRequest(int requestId, [FromBody] CreateTicketFromRequestRequest payload)
        {
            try
            {
                // payload contains CreatedBy
                var ticket = await _service.CreateTicketFromRequestAsync(requestId, payload.CreatedBy);
                return Ok(ticket);
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
                return StatusCode(500, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Update InboundOrder (ticket) items — modify expected/received quantities or add items.
        /// </summary>
        [HttpPut("tickets/{ticketId}/items")]
        public async Task<IActionResult> UpdateTicketItems(int ticketId, [FromBody] IEnumerable<UpdateInboundOrderItemRequest> items)
        {
            try
            {
                var ticket = await _service.UpdateTicketItemsAsync(ticketId, items);
                return Ok(ticket);
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
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
    public sealed record CreateTicketFromRequestRequest(int CreatedBy);
}
