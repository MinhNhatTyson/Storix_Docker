using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
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
                .ToListAsync();
        }

        public async Task<Product?> GetByIdAsync(int id, int companyId)
        {
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Type)
                .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);
        }

        public async Task<Product?> GetBySkuAsync(string sku, int companyId)
        {
            if (string.IsNullOrWhiteSpace(sku)) return null;
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Type)
                .FirstOrDefaultAsync(p => p.Sku == sku && p.CompanyId == companyId);
        }

        public async Task<List<Product>> GetProductsByCompanyIdAsync(int companyId)
        {
            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Type)
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
            existing.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _context.Products.Update(existing);
            return await _context.SaveChangesAsync();
        }

        public async Task<bool> RemoveAsync(Product product)
        {
            _context.Products.Remove(product);
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
    }
}
