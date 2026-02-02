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
        [HttpPost("requests")]
        public async Task<IActionResult> CreateRequest([FromBody] CreateInboundRequestRequest request)
        {
            try
            {
                var id = await _service.CreateInboundRequestAsync(request);
                return Ok(new { Id = id });
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

        [HttpPut("requests/{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateInboundRequestStatusRequest request)
        {
            try
            {
                var updatedId = await _service.UpdateInboundRequestStatusAsync(id, request.ApproverId, request.Status);
                return Ok(new { Id = updatedId });
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
}
