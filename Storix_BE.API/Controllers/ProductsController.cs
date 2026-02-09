using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _service;

        public ProductsController(IProductService service)
        {
            _service = service;
        }
        [HttpGet("get-all/{userId:int}")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetAllProductsFromACompany(int userId)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });

            int companyId;
            try
            {
                companyId = await _service.GetCompanyIdByUserIdAsync(userId);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (companyId <= 0) return NotFound("Cannot find company id with the provided user id");
            var items = await _service.GetByCompanyAsync(companyId);
            return Ok(items);
        }
        [HttpGet("get-by-id/{userId:int}/{id:int}")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetById(int userId, int id)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });
            if (id <= 0) return BadRequest(new { message = "Invalid product id." });

            int companyId;
            try
            {
                companyId = await _service.GetCompanyIdByUserIdAsync(userId);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (companyId <= 0) return NotFound("Cannot find company id with the provided user id");

            var item = await _service.GetByIdAsync(id, companyId);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpGet("get-by-sku/{userId:int}/sku/{sku}")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetBySku(int userId, string sku)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });
            if (string.IsNullOrWhiteSpace(sku)) return BadRequest(new { message = "SKU is required." });

            int companyId;
            try
            {
                companyId = await _service.GetCompanyIdByUserIdAsync(userId);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (companyId <= 0) return NotFound("Cannot find company id with the provided user id");

            var item = await _service.GetBySkuAsync(sku, companyId);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost("create")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Create([FromForm] CreateProductRequest request)
        {
            try
            {
                var product = await _service.CreateAsync(request);
                return Ok(product);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        [HttpPut("update{id:int}")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Update(int id, [FromForm] Storix_BE.Service.Interfaces.UpdateProductRequest request)
        {
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

        [HttpDelete("delete/{userId:int}/{id:int}")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Delete(int userId, int id)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });
            if (id <= 0) return BadRequest(new { message = "Invalid product id." });

            int companyId;
            try
            {
                companyId = await _service.GetCompanyIdByUserIdAsync(userId);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (companyId <= 0) return NotFound("Cannot find company id with the provided user id");

            var deleted = await _service.DeleteAsync(id, companyId);
            if (!deleted) return NotFound();
            return Ok("Succesfully deleted the product with id: " + id);
        }

        [HttpGet("get-all-product-types/{userId:int}")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetAllProductTypes(int userId)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });

            try
            {
                var companyId = await _service.GetCompanyIdByUserIdAsync(userId);
                if (companyId <= 0) return NotFound("Cannot find company id with the provided user id");

                var types = await _service.GetAllProductTypesAsync(companyId);
                return Ok(types);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("create-new-product-type")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Create([FromBody] Storix_BE.Service.Interfaces.CreateProductTypeRequest request)
        {
            try
            {
                var created = await _service.CreateProductTypeAsync(request);
                return Ok(created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("update-type-name/{id:int}")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Update(int id, [FromBody] Storix_BE.Service.Interfaces.UpdateProductTypeRequest request)
        {
            try
            {
                var updated = await _service.UpdateProductTypeAsync(id, request);
                if (updated == null) return NotFound();
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("delete-product-type/{id:int}")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var removed = await _service.DeleteProductTypeAsync(id);
                if (!removed) return NotFound();
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
