using DocumentFormat.OpenXml.Office2010.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Asn1.Ocsp;
using Storix_BE.Service.Implementation;
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

            try
            {
                var deleted = await _service.DeleteAsync(id, companyId);
                if (!deleted) return NotFound();
                return Ok("Successfully deleted the product with id: " + id);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
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
        public async Task<IActionResult> Create([FromBody] CreateProductTypeRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request cannot be null." });

            try
            {
                var companyScopedRequest = new CreateProductTypeRequest(request.CompanyId, request.Name);
                var created = await _service.CreateProductTypeAsync(companyScopedRequest);
                return CreatedAtAction(nameof(GetAllProductTypes), new { userId = request.CompanyId }, created);
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
        [HttpGet("export/csv")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> ExportProductsCsv()
        {
            var products = await _service.GetProductsForExportAsync();
            var fileBytes = _service.ExportProductsToCsv(products);

            return File(
                fileBytes,
                "text/csv",
                $"products_{DateTime.UtcNow:yyyyMMddHHmmss}.csv"
            );
        }
        [HttpGet("export/excel")]
        public async Task<IActionResult> ExportProductsExcel()
        {
            var products = await _service.GetProductsForExportAsync();
            var fileBytes = _service.ExportProductsToExcel(products);

            return File(
                fileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"products_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx"
            );
        }
        [HttpPost("import/csv")]
        public async Task<IActionResult> ImportCsv(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required");

            var products = _service.ParseProductsFromCsv(file);
            await _service.ImportProductsAsync(products);

            return Ok(new { message = "CSV import successful" });
        }

        [HttpPost("import/excel")]
        public async Task<IActionResult> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required");

            var products = _service.ParseProductsFromExcel(file);
            await _service.ImportProductsAsync(products);

            return Ok(new { message = "Excel import successful" });
        }
        [HttpGet("categories/children/{parentId:int}")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetCategoryChildren(int parentId)
        {
            if (parentId <= 0) return BadRequest(new { message = "Invalid parent category id." });

            try
            {
                var children = await _service.GetChildCategoriesAsync(parentId);
                return Ok(children);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpGet("categories/company/{userId:int}")]
        [Authorize(Roles = "2,3")]
        public async Task<IActionResult> GetAllCategoriesForCompany(int userId)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });

            try
            {
                var companyId = await _service.GetCompanyIdByUserIdAsync(userId);
                if (companyId <= 0) return NotFound("Cannot find company id with the provided user id");

                var categories = await _service.GetAllProductCategoriesAsync(companyId);
                return Ok(categories);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("categories/create")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> CreateCategory([FromBody] CreateProductCategoryRequest request)
        {
            if (request == null) return BadRequest(new { message = "Request cannot be null." });

            try
            {
                var created = await _service.CreateProductCategoryAsync(request);
                return CreatedAtAction(nameof(GetAllCategoriesForCompany), new { userId = request.CompanyId }, created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("categories/{id:int}")]
        [Authorize(Roles = "2")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            if (id <= 0) return BadRequest(new { message = "Invalid category id." });

            try
            {
                var removed = await _service.DeleteProductCategoryAsync(id);
                if (!removed) return NotFound();
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpGet("inventory-locations/{userId:int}/{productId:int}/warehouse/{warehouseId:int}")]
        [Authorize(Roles = "2,3,4")]
        public async Task<IActionResult> GetProductInventoryLocations(int userId, int productId, int warehouseId)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });
            if (productId <= 0) return BadRequest(new { message = "Invalid product id." });
            if (warehouseId <= 0) return BadRequest(new { message = "Invalid warehouse id." });

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

            try
            {
                var locations = await _service.GetProductInventoryLocationsAsync(companyId, warehouseId, productId);
                return Ok(locations);
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
    }
}
