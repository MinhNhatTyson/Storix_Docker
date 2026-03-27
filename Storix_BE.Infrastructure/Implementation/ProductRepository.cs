using ClosedXML.Excel;
using CsvHelper;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class ProductRepository : GenericRepository<Product>, IProductRepository
    {
        private readonly StorixDbContext _context;

        public ProductRepository(StorixDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<List<Product>> GetAllProductsAsync()
        {
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.ProductPrices)
                .Include(p => p.Category)
                .ToListAsync();
        }

        public async Task<Product?> GetByIdAsync(int id, int companyId)
        {
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.ProductPrices)
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);
        }

        public async Task<Product?> GetBySkuAsync(string sku, int companyId)
        {
            if (string.IsNullOrWhiteSpace(sku)) return null;
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.ProductPrices)
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Sku == sku && p.CompanyId == companyId);
        }

        public async Task<List<Product>> GetProductsByCompanyIdAsync(int companyId)
        {
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.ProductPrices)
                .Where(p => p.CompanyId == companyId)
                .ToListAsync();
        }
        public async Task<bool> CategoryHasChildrenAsync(int categoryId)
        {
            return await _context.ProductCategories
                .AnyAsync(c => c.ParentCategoryId == categoryId);
        }
        public async Task<ProductCategory?> GetCategoryByIdAsync(int id)
        {
            return await _context.ProductCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);
        }
        public async Task<List<ProductCategory>> GetChildCategoriesAsync(int parentId)
        {
            if (parentId <= 0) throw new InvalidOperationException("Invalid parent category id.");

            return await _context.ProductCategories
                .AsNoTracking()
                .Where(c => c.ParentCategoryId == parentId)
               .ToListAsync();
        }

        public async Task<Product> CreateAsync(Product product)
        {
            product.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            // Reload to ensure navigation is populated for the returned entity
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == product.Id) ?? product;
        }

        public async Task<int> UpdateAsync(Product product)
        {
            // Validate existing product exists
            var existing = await _context.Products.FirstOrDefaultAsync(p => p.Id == product.Id);
            if (existing == null)
                throw new InvalidOperationException($"Product with id {product.Id} not found.");

            // If CategoryId changed / provided, validate and attach
            if (product.CategoryId.HasValue)
            {
                var category = await _context.ProductCategories.FindAsync(product.CategoryId.Value);
                if (category == null)
                    throw new InvalidOperationException($"Product category with id {product.CategoryId.Value} not found.");
                // ensure category belongs to same company when both sides have company id
                if (category.CompanyId.HasValue && product.CompanyId.HasValue && category.CompanyId != product.CompanyId)
                    throw new InvalidOperationException("Product category does not belong to the same company as the product.");
                existing.CategoryId = product.CategoryId;
                existing.Category = category;
            }
            else
            {
                existing.CategoryId = null;
                existing.Category = null;
            }

            // Patch other fields
            existing.CompanyId = product.CompanyId;
            existing.Sku = product.Sku;
            existing.Name = product.Name;
            existing.Unit = product.Unit;
            existing.Weight = product.Weight;
            existing.Width = product.Width;
            existing.Length = product.Length;
            existing.Height = product.Height;
            existing.IsEsd = product.IsEsd;
            existing.IsMsd = product.IsMsd;
            existing.Description = product.Description;
            existing.Image = product.Image;
            existing.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _context.Products.Update(existing);
            return await _context.SaveChangesAsync();
        }

        public async Task<bool> RemoveAsync(Product product)
        {
            if (product == null) throw new InvalidOperationException("Product cannot be null.");
            if (product.Id <= 0) throw new InvalidOperationException("Invalid product id.");

            var inboundItems = await _context.InboundOrderItems
                .AsNoTracking()
                .Where(i => i.ProductId == product.Id)
                .Select(i => new { i.Id, i.InboundOrderId, i.InboundRequestId })
                .ToListAsync();

            var outboundItems = await _context.OutboundOrderItems
                .AsNoTracking()
                .Where(i => i.ProductId == product.Id)
                .Select(i => new { i.Id, i.OutboundOrderId, i.OutboundRequestId })
                .ToListAsync();

            if (inboundItems.Any() || outboundItems.Any())
            {
                var messages = new List<string>();

                if (inboundItems.Any())
                {
                    var inboundOrderIds = inboundItems.Where(x => x.InboundOrderId.HasValue).Select(x => x.InboundOrderId!.Value).Distinct().ToList();
                    var inboundRequestIds = inboundItems.Where(x => x.InboundRequestId.HasValue).Select(x => x.InboundRequestId!.Value).Distinct().ToList();

                    if (inboundOrderIds.Any())
                        messages.Add($"Inbound orders: {string.Join(',', inboundOrderIds)}");
                    if (inboundRequestIds.Any())
                        messages.Add($"Inbound requests: {string.Join(',', inboundRequestIds)}");
                    if (!inboundOrderIds.Any() && !inboundRequestIds.Any())
                        messages.Add("Inbound order items exist referencing this product.");
                }

                if (outboundItems.Any())
                {
                    var outboundOrderIds = outboundItems.Where(x => x.OutboundOrderId.HasValue).Select(x => x.OutboundOrderId!.Value).Distinct().ToList();
                    var outboundRequestIds = outboundItems.Where(x => x.OutboundRequestId.HasValue).Select(x => x.OutboundRequestId!.Value).Distinct().ToList();

                    if (outboundOrderIds.Any())
                        messages.Add($"Outbound orders: {string.Join(',', outboundOrderIds)}");
                    if (outboundRequestIds.Any())
                        messages.Add($"Outbound requests: {string.Join(',', outboundRequestIds)}");
                    if (!outboundOrderIds.Any() && !outboundRequestIds.Any())
                        messages.Add("Outbound order items exist referencing this product.");
                }

                var detail = string.Join("; ", messages);
                throw new InvalidOperationException($"Cannot remove product with id {product.Id} because it is referenced by existing order items. {detail}");
            }
            var existingProduct = await _context.Products.FindAsync(product.Id);
            if (existingProduct == null) return false;

            _context.Products.Remove(existingProduct);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<ProductType>> GetAllProductTypesAsync(int companyId)
        {
            var types = await _context.ProductTypes
                .AsNoTracking()
                .OrderBy(t => t.Id)
                .ToListAsync();
            return types;
        }

        public async Task<ProductType> CreateProductTypeAsync(ProductType type, int companyId)
        {
            if (type == null) throw new InvalidOperationException("Type cannot be null.");
            var name = type.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Product type name is required.");

            var nameLower = name.ToLowerInvariant();
            // check if a product type with same name is already used by this company
            var existsForCompany = await _context.ProductTypes
                .AsNoTracking()
                .Where(t => t.Name != null && t.Name.ToLower() == nameLower)
                .FirstOrDefaultAsync();

            if (existsForCompany != null)
                throw new InvalidOperationException($"Product type with name '{name}' already exists for this company.");

            var newType = new ProductType
            {
                Name = name,
            };

            _context.ProductTypes.Add(newType);
            await _context.SaveChangesAsync();

            // Return the created entity (detached)
            return await _context.ProductTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == newType.Id) ?? newType;
        }

        public async Task<int> UpdateProductType(ProductType type)
        {
            if (type == null) throw new InvalidOperationException("Type cannot be null.");
            var existing = await _context.ProductTypes.FirstOrDefaultAsync(t => t.Id == type.Id);
            if (existing == null)
                throw new InvalidOperationException($"Product type with id {type.Id} not found.");

            var name = type.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Product type name is required.");

            var nameLower = name.ToLowerInvariant();
            var collision = await _context.ProductTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id != type.Id && t.Name != null && t.Name.ToLower() == nameLower);

            if (collision != null)
                throw new InvalidOperationException($"Another product type with name '{name}' already exists.");

            existing.Name = name;
            _context.ProductTypes.Update(existing);
            return await _context.SaveChangesAsync();
        }

        public async Task<bool> RemoveProductTypeAsync(ProductType type)
        {
            if (type == null) throw new InvalidOperationException("Type cannot be null.");
            var existing = await _context.ProductTypes
                .FirstOrDefaultAsync(t => t.Id == type.Id);

            if (existing == null) return false;

            _context.ProductTypes.Remove(existing);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int?> GetCompanyIdByUserIdAsync(int userId)
        {
            if (userId <= 0) return null;
            var companyId = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.CompanyId)
                .FirstOrDefaultAsync();
            return companyId;
        }
        public async Task<List<ProductExportDto>> GetProductsForExportAsync()
        {
            return await _context.Products
                .Include(p => p.Company)
                .Select(p => new ProductExportDto
                {
                    Id = p.Id,
                    Sku = p.Sku,
                    Name = p.Name,
                    Category = p.Category.Name,
                    Unit = p.Unit,
                    Weight = p.Weight,
                    CompanyName = p.Company != null ? p.Company.Name : null,
                    Description = p.Description
                })
                .ToListAsync();
        }
        public byte[] ExportProductsToCsv(List<ProductExportDto> products)
        {
            using var memoryStream = new MemoryStream();

            // Use a block to ensure disposal happens before ToArray()
            using (var writer = new StreamWriter(memoryStream, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(products);
                // Explicitly flush to be safe
                writer.Flush();
            }

            // Accessing the array AFTER the writer is disposed ensures all data is present
            return memoryStream.ToArray();
        }
        public byte[] ExportProductsToExcel(List<ProductExportDto> products)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Products");

            // Headers
            worksheet.Cell(1, 1).Value = "ID";
            worksheet.Cell(1, 2).Value = "SKU";
            worksheet.Cell(1, 3).Value = "Name";
            worksheet.Cell(1, 4).Value = "Category";
            worksheet.Cell(1, 5).Value = "Unit";
            worksheet.Cell(1, 6).Value = "Weight";
            worksheet.Cell(1, 7).Value = "Company";
            worksheet.Cell(1, 8).Value = "Type";
            worksheet.Cell(1, 9).Value = "Description";

            // Data
            for (int i = 0; i < products.Count; i++)
            {
                var row = i + 2;
                var p = products[i];

                worksheet.Cell(row, 1).Value = p.Id;
                worksheet.Cell(row, 2).Value = p.Sku;
                worksheet.Cell(row, 3).Value = p.Name;
                worksheet.Cell(row, 4).Value = p.Category;
                worksheet.Cell(row, 5).Value = p.Unit;
                worksheet.Cell(row, 6).Value = p.Weight;
                worksheet.Cell(row, 7).Value = p.CompanyName;
                worksheet.Cell(row, 8).Value = p.ProductType;
                worksheet.Cell(row, 9).Value = p.Description;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
        public List<ProductExportDto> ParseProductsFromCsv(IFormFile file)
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            return csv.GetRecords<ProductExportDto>().ToList();
        }
        public List<ProductExportDto> ParseProductsFromExcel(IFormFile file)
        {
            var products = new List<ProductExportDto>();

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            var rows = worksheet.RangeUsed().RowsUsed().Skip(1);

            foreach (var row in rows)
            {
                products.Add(new ProductExportDto
                {
                    Sku = row.Cell(2).GetString(),
                    Name = row.Cell(3).GetString(),
                    Category = row.Cell(4).GetString(),
                    Unit = row.Cell(5).GetString(),
                    Weight = row.Cell(6).GetDouble(),
                    CompanyName = row.Cell(7).GetString(),
                    ProductType = row.Cell(8).GetString(),
                    Description = row.Cell(9).GetString()
                });
            }

            return products;
        }
        public async Task ImportProductsAsync(List<ProductExportDto> dtos)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            foreach (var dto in dtos)
            {
                if (string.IsNullOrWhiteSpace(dto.Sku))
                    continue; // or log error

                var product = await _context.Products
                    .FirstOrDefaultAsync(p => p.Sku == dto.Sku);

                var companyId = await ResolveCompanyId(dto.CompanyName);
                var typeId = await ResolveProductTypeId(dto.ProductType);
                var categoryId = await ResolveProductCategoryId(dto.Category);

                if (product == null)
                {
                    // INSERT
                    product = new Product
                    {
                        Sku = dto.Sku,
                        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                    };

                    _context.Products.Add(product);
                }

                // UPDATE (shared)
                product.Name = dto.Name;
                product.CategoryId = categoryId;
                product.Unit = dto.Unit;
                product.Weight = dto.Weight;
                product.CompanyId = companyId;
                product.Description = dto.Description;
                product.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        private async Task<int?> ResolveCompanyId(string? companyName)
        {
            if (string.IsNullOrWhiteSpace(companyName)) return null;

            return await _context.Companies
                .Where(c => c.Name == companyName)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync();
        }
        private async Task<int?> ResolveProductTypeId(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            return await _context.ProductTypes
                .Where(c => c.Name == typeName)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync();
        }
        private async Task<int?> ResolveProductCategoryId(string? categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return null;

            return await _context.ProductCategories
                .Where(c => c.Name == categoryName)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync();
        }
        public async Task<List<ProductCategory>> GetAllProductCategoriesAsync(int companyId)
        {
            return await _context.ProductCategories
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .OrderBy(c => c.Id)
                .ToListAsync();
        }

        public async Task<ProductCategory> CreateCategoryAsync(ProductCategory category)
        {
            if (category == null) throw new InvalidOperationException("Category cannot be null.");
            var name = category.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Product category name is required.");

            // ensure unique name within same company and parent scope
            var nameLower = name.ToLowerInvariant();
            var parentId = category.ParentCategoryId;
            var exists = await _context.ProductCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == category.CompanyId &&
                    c.ParentCategoryId == parentId &&
                    c.Name != null && c.Name.ToLower() == nameLower);

            if (exists != null)
                throw new InvalidOperationException($"Product category with name '{name}' already exists in the same scope.");

            // resolve parent (if any) and compute level
            int level = 0;
            if (parentId.HasValue)
            {
                var parent = await _context.ProductCategories.FindAsync(parentId.Value);
                if (parent == null)
                    throw new InvalidOperationException($"Parent product category with id {parentId.Value} not found.");
                // ensure same company when specified
                if (parent.CompanyId.HasValue && category.CompanyId.HasValue && parent.CompanyId != category.CompanyId)
                    throw new InvalidOperationException("Parent category does not belong to the same company as the new category.");
                level = parent.Level + 1;
            }

            var newCategory = new ProductCategory
            {
                Name = name,
                CompanyId = category.CompanyId,
                ParentCategoryId = parentId,
                Level = level,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            _context.ProductCategories.Add(newCategory);
            await _context.SaveChangesAsync();

            return await _context.ProductCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == newCategory.Id) ?? newCategory;
        }

        public async Task<bool> RemoveCategoryAsync(ProductCategory category)
        {
            if (category == null) throw new InvalidOperationException("Category cannot be null.");
            var existing = await _context.ProductCategories
                .Include(c => c.InverseParentCategory)
                .Include(c => c.Products)
                .FirstOrDefaultAsync(c => c.Id == category.Id);

            if (existing == null) return false;

            if ((existing.InverseParentCategory != null && existing.InverseParentCategory.Any()) ||
                (existing.Products != null && existing.Products.Any()))
            {
                throw new InvalidOperationException("Cannot remove product category because it has child categories or products referencing it.");
            }

            _context.ProductCategories.Remove(existing);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task UpdateProductPopularityAsync()
        {
            // Example using Raw SQL in Entity Framework
            var sql = @"UPDATE products
                    SET popularity_score = sub.new_score
                    FROM (
                        SELECT 
                            p.id,
                            (COALESCE(SUM(ooi.quantity), 0) * 0.6) + 
                            (CASE 
                                WHEN MAX(it.created_at) IS NULL THEN 0 
                                ELSE (30 - EXTRACT(DAY FROM (NOW() - MAX(it.created_at)))) 
                             END * 0.4) as new_score
                        FROM products p
                        LEFT JOIN outbound_order_items ooi ON p.id = ooi.product_id
                        LEFT JOIN inventory_transactions it ON p.id = it.product_id 
                            AND it.transaction_type = 'OUT'
                        WHERE it.created_at > NOW() - INTERVAL '30 days' OR it.created_at IS NULL
                        GROUP BY p.id
                    ) AS sub
                    WHERE products.id = sub.id;";
            await _context.Database.ExecuteSqlRawAsync(sql);
        }
    }
}