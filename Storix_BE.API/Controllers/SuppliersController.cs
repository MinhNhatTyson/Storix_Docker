using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SuppliersController : ControllerBase
    {
        private readonly ISupplierService _service;

        public SuppliersController(ISupplierService service)
        {
            _service = service;
        }

        [HttpGet("get-all/{userId:int}")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetAll(int userId)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });

            try
            {
                var companyId = await _service.GetCompanyIdByUserIdAsync(userId);
                var items = await _service.GetByCompanyAsync(companyId);
                return Ok(items);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("get-by-id/{userId:int}/{id:int}")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetById(int userId, int id)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });
            if (id <= 0) return BadRequest(new { message = "Invalid supplier id." });

            try
            {
                var companyId = await _service.GetCompanyIdByUserIdAsync(userId);
                var item = await _service.GetByIdAsync(id, companyId);
                if (item == null) return NotFound();
                return Ok(item);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("add-new-supplier")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Create([FromBody] Storix_BE.Service.Interfaces.CreateSupplierRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request cannot be null." });

            try
            {
                var created = await _service.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { userId = request.CompanyId, id = created.Id }, created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("update-a-supplier/{id:int}")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Update(int id, [FromBody] Storix_BE.Service.Interfaces.UpdateSupplierRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request cannot be null." });

            try
            {
                var updated = await _service.UpdateAsync(id, request);
                if (updated == null) return NotFound();
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("delete-a-supplier/{userId:int}/{id:int}")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Delete(int userId, int id)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });
            if (id <= 0) return BadRequest(new { message = "Invalid supplier id." });

            try
            {
                var companyId = await _service.GetCompanyIdByUserIdAsync(userId);
                var deleted = await _service.DeleteAsync(id, companyId);
                if (!deleted) return NotFound();
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
