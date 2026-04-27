using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class InventoryInboundController : ControllerBase
    {
        private readonly IInventoryInboundService _service;

        public InventoryInboundController(IInventoryInboundService service)
        {
            _service = service;
        }
        [HttpPost("create-inbound-request")]
        [Authorize(Roles = "2,3")]
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
        [Authorize(Roles = "3")]
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
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> CreateTicketFromRequest(int requestId, [FromBody] CreateTicketFromRequestRequest payload)
        {
            try
            {
                // payload now contains CreatedBy and optional StaffId
                var ticket = await _service.CreateTicketFromRequestAsync(requestId, payload.CreatedBy, payload.StaffId);
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
        /// Accepts location assignments to record where received units are stored (bin id code + quantity).
        /// </summary>
        [HttpPut("update-tickets/{ticketId}/items")]
        [Authorize(Roles = "2,3,4")]
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

        [HttpGet("requests/{companyId:int}")]
        public async Task<IActionResult> GetAllRequests(int companyId)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });

            try
            {
                var items = await _service.GetAllInboundRequestsAsync(companyId);
                return Ok(items);
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

        [HttpGet("tickets/{companyId:int}")]
        public async Task<IActionResult> GetAllTickets(int companyId)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });

            try
            {
                var items = await _service.GetAllInboundOrdersAsync(companyId);
                return Ok(items);
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

        [HttpGet("requests/{companyId:int}/{id:int}")]
        public async Task<IActionResult> GetRequestById(int companyId, int id)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (id <= 0) return BadRequest(new { message = "Invalid request id." });

            try
            {
                var item = await _service.GetInboundRequestByIdAsync(companyId, id);
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

        [HttpGet("tickets/{companyId:int}/{id:int}")]
        public async Task<IActionResult> GetTicketById(int companyId, int id)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (id <= 0) return BadRequest(new { message = "Invalid ticket id." });

            try
            {
                var item = await _service.GetInboundOrderByIdAsync(companyId, id);
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
        [HttpGet("get-inbound-orders-for-staff/{companyId:int}/{staffId:int}")]
        public async Task<IActionResult> GetInboundTasksByStaff(int companyId, int staffId)
        {
            if (companyId <= 0) return BadRequest(new { message = "Invalid company id." });
            if (staffId <= 0) return BadRequest(new { message = "Invalid staff id." });

            try
            {
                var items = await _service.GetInboundOrdersByStaffAsync(companyId, staffId);
                return Ok(items);
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
        [HttpGet("export/inbound-request/{requestId:int}/csv")]
        public async Task<IActionResult> ExportInboundRequestCsv(int requestId)
        {
            if (requestId <= 0) return BadRequest(new { message = "Invalid inbound request id." });

            try
            {
                var dto = await _service.GetInboundRequestForExportAsync(requestId);
                var fileBytes = _service.ExportInboundRequestToCsv(dto);

                return File(
                    fileBytes,
                    "text/csv",
                    $"inbound_request_{requestId}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"
                );
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

        [HttpGet("export/inbound-request/{requestId:int}/excel")]
        public async Task<IActionResult> ExportInboundRequestExcel(int requestId)
        {
            if (requestId <= 0) return BadRequest(new { message = "Invalid inbound request id." });

            try
            {
                var dto = await _service.GetInboundRequestForExportAsync(requestId);
                var fileBytes = _service.ExportInboundRequestToExcel(dto);

                return File(
                    fileBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"inbound_request_{requestId}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx"
                );
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

        [HttpGet("export/inbound-ticket/{orderId:int}/csv")]
        public async Task<IActionResult> ExportInboundOrderCsv(int orderId)
        {
            if (orderId <= 0) return BadRequest(new { message = "Invalid inbound order id." });

            try
            {
                var dto = await _service.GetInboundOrderForExportAsync(orderId);
                var fileBytes = _service.ExportInboundOrderToCsv(dto);

                return File(
                    fileBytes,
                    "text/csv",
                    $"inbound_ticket_{orderId}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"
                );
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

        [HttpGet("export/inbound-ticket/{orderId:int}/excel")]
        public async Task<IActionResult> ExportInboundOrderExcel(int orderId)
        {
            if (orderId <= 0) return BadRequest(new { message = "Invalid inbound order id." });

            try
            {
                var dto = await _service.GetInboundOrderForExportAsync(orderId);
                var fileBytes = _service.ExportInboundOrderToExcel(dto);

                return File(
                    fileBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"inbound_ticket_{orderId}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx"
                );
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
        [HttpPost("import-inbound-request-from-excel")]
        public async Task<IActionResult> ImportInboundExcel(IFormFile file)
        {
            var result = await _service.ImportInboundRequestAsync(file);
            return Ok(result);
        }
        [HttpPost("storage-recommendations")]
        public async Task<IActionResult> AddStorageRecommendations([FromBody] IInventoryInboundService.AddStorageRecommendationsRequest request)
        {
            try
            {
                // Basic controller-side validation for the new payload shape (many recommendations per inbound product)
                if (request == null) return BadRequest(new { message = "Request body is required." });

                var items = request.StorageRecommendations?.ToList();
                if (items == null || !items.Any())
                    return BadRequest(new { message = "storageRecommendations payload is required and cannot be empty." });

                foreach (var it in items)
                {
                    if (it.InboundProductId <= 0)
                        return BadRequest(new { message = "Each storage recommendation item must contain a valid inboundProductId." });

                    if (it.Recommendations == null || !it.Recommendations.Any())
                        return BadRequest(new { message = "Each storage recommendation item must contain one or more Recommendations." });

                    foreach (var rec in it.Recommendations)
                    {
                        if (rec == null)
                            return BadRequest(new { message = "Recommendation entries cannot be null." });

                        if (string.IsNullOrWhiteSpace(rec.BinId))
                            return BadRequest(new { message = "Recommendation.BinId (ShelfLevelBin.IdCode) is required." });

                        if (rec.Quantity.HasValue && rec.Quantity.Value < 0)
                            return BadRequest(new { message = "Recommendation.Quantity cannot be negative." });
                    }
                }

                await _service.AddStorageRecommendationsAsync(request);
                return Ok(new { message = "Storage recommendations added successfully." });
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
        [HttpGet("get-inbound-orders/{inboundOrderId:int}/storage-recommendations")]
        public async Task<IActionResult> GetStorageRecommendations(int inboundOrderId)
        {
            if (inboundOrderId <= 0) return BadRequest(new { message = "Invalid inbound order id." });

            try
            {
                var items = await _service.GetStorageRecommendationsByInboundOrderIdAsync(inboundOrderId);
                return Ok(items);
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
        [HttpPost("tickets/{ticketId:int}/assign-staff")]
        [Authorize(Roles = "3")]
        public async Task<IActionResult> AssignStaffToTicket(int ticketId, [FromBody] AssignStaffRequest request)
        {
            if (ticketId <= 0) return BadRequest(new { message = "Invalid ticket id." });
            if (request == null) return BadRequest(new { message = "Request body is required." });
            if (request.CompanyId <= 0) return BadRequest(new { message = "CompanyId is required." });
            if (request.ManagerId <= 0) return BadRequest(new { message = "ManagerId is required." });
            if (request.StaffId <= 0) return BadRequest(new { message = "StaffId is required." });

            try
            {
                var updated = await _service.AssignStaffToInboundOrderAsync(request.CompanyId, ticketId, request.ManagerId, request.StaffId);
                return Ok(updated);
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
    public sealed record AssignStaffRequest(int CompanyId, int ManagerId, int StaffId);
    public sealed record CreateTicketFromRequestRequest(int CreatedBy, int StaffId);
}
