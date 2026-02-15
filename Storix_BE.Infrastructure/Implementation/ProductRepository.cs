using ClosedXML.Excel;
using CsvHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
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
                .Include(p => p.Type)
                .Include(p => p.ProductPrices)
                .ToListAsync();
        }

        public async Task<Product?> GetByIdAsync(int id, int companyId)
        {
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Type)
                .Include(p => p.ProductPrices)
                .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);
        }

        public async Task<Product?> GetBySkuAsync(string sku, int companyId)
        {
            if (string.IsNullOrWhiteSpace(sku)) return null;
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Type)
                .Include(p => p.ProductPrices)
                .FirstOrDefaultAsync(p => p.Sku == sku && p.CompanyId == companyId);
        }

        public async Task<List<Product>> GetProductsByCompanyIdAsync(int companyId)
        {
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Type)
                .Include(p => p.ProductPrices)
                .Where(p => p.CompanyId == companyId)
                .ToListAsync();
        }

        public async Task<Product> CreateAsync(Product product)
        {
            product.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            if (product.TypeId.HasValue)
            {
                var type = await _context.ProductTypes.FindAsync(product.TypeId.Value);
                if (type == null)
                    throw new InvalidOperationException($"Product type with id {product.TypeId.Value} not found.");
                product.Type = type;
            }

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            // Reload to ensure navigation is populated for the returned entity
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Type)
                .FirstOrDefaultAsync(p => p.Id == product.Id) ?? product;
        }

        public async Task<int> UpdateAsync(Product product)
        {
            // Validate existing product exists
            var existing = await _context.Products.FirstOrDefaultAsync(p => p.Id == product.Id);
            if (existing == null)
                throw new InvalidOperationException($"Product with id {product.Id} not found.");

            // If TypeId changed / provided, validate and attach
            if (product.TypeId.HasValue)
            {
                var type = await _context.ProductTypes.FindAsync(product.TypeId.Value);
                if (type == null)
                    throw new InvalidOperationException($"Product type with id {product.TypeId.Value} not found.");
                existing.TypeId = product.TypeId;
                existing.Type = type;
            }
            else
            {
                existing.TypeId = null;
                existing.Type = null;
            }

            // Patch other fields
            existing.CompanyId = product.CompanyId;
            existing.Sku = product.Sku;
            existing.Name = product.Name;
            existing.Category = product.Category;
            existing.Unit = product.Unit;
            existing.Weight = product.Weight;
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
                .Where(t => t.CompanyId == companyId)
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
            var existsForCompany = await _context.ProductTypes
                .AsNoTracking()
                .Where(t => t.Name != null && t.Name.ToLower() == nameLower)
                .Where(t => t.CompanyId == companyId)
                .FirstOrDefaultAsync();

            if (existsForCompany != null)
                throw new InvalidOperationException($"Product type with name '{name}' already exists for this company.");

            var newType = new ProductType
            {
                Name = name,
                CompanyId = companyId
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
                .Include(t => t.Products)
                .Include(t => t.StorageZones)
                .FirstOrDefaultAsync(t => t.Id == type.Id);

            if (existing == null) return false;

            // Prevent deletion if referenced
            if ((existing.Products != null && existing.Products.Any()) ||
                (existing.StorageZones != null && existing.StorageZones.Any()))
            {
                throw new InvalidOperationException("Cannot remove product type because it is referenced by products or storage zones.");
            }

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
                .Include(p => p.Type)
                .Select(p => new ProductExportDto
                {
                    Id = p.Id,
                    Sku = p.Sku,
                    Name = p.Name,
                    Category = p.Category,
                    Unit = p.Unit,
                    Weight = p.Weight,
                    CompanyName = p.Company != null ? p.Company.Name : null,
                    ProductType = p.Type != null ? p.Type.Name : null,
                    CreatedAt = p.CreatedAt
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
            worksheet.Cell(1, 9).Value = "Created At";

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
                worksheet.Cell(row, 9).Value = p.CreatedAt;
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
                product.Category = dto.Category;
                product.Unit = dto.Unit;
                product.Weight = dto.Weight;
                product.CompanyId = companyId;
                product.TypeId = typeId;
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
    }
}
