using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;
using System;
using System.Threading.Tasks;
namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryCountController : ControllerBase
    {
        private readonly IInventoryCountService _service;

        public InventoryCountController(IInventoryCountService service)
        {
            _service = service;
        }

        [HttpPost("create-ticket")]
        public async Task<IActionResult> CreateTicket([FromBody] CreateStockCountTicketRequest request)
        {
            try
            {
                var ticket = await _service.CreateStockCountTicketAsync(request);
                return Ok(ticket);
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

        [HttpPut("update-ticket/{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStockCountTicketStatusRequest payload)
        {
            try
            {
                var ticket = await _service.UpdateStockCountTicketStatusAsync(id, payload.ApproverId, payload.Status);
                return Ok(ticket);
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

        [HttpPut("tickets/{ticketId}/items")]
        public async Task<IActionResult> UpdateTicketItems(int ticketId, [FromBody] UpdateStockCountItemsRequest request)
        {
            try
            {
                var ticket = await _service.UpdateStockCountItemsAsync(ticketId, request);
                return Ok(ticket);
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

        [HttpGet("tickets/{companyId:int}")]
        public async Task<IActionResult> GetAllTickets(int companyId)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            try
            {
                var items = await _service.GetStockCountTicketsByCompanyAsync(companyId);
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

        [HttpGet("tickets/{companyId:int}/{id:int}")]
        public async Task<IActionResult> GetTicketById(int companyId, int id)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (id <= 0) return BadRequest(new { message = "Invalid ticket id." });

            try
            {
                var item = await _service.GetStockCountTicketByIdAsync(companyId, id);
                return Ok(item);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
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

        [HttpGet("get-tasks-for-staff/{companyId:int}/{staffId:int}")]
        public async Task<IActionResult> GetTasksByStaff(int companyId, int staffId)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (staffId <= 0) return BadRequest(new { message = "Invalid staff id." });

            try
            {
                var items = await _service.GetStockCountTicketsByStaffAsync(companyId, staffId);
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


    }
}
