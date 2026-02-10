using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class ProductService : IProductService
    {
        private readonly IProductRepository _repo;
        private readonly IImageService _imageService;

        public ProductService(IProductRepository repo, IImageService imageService)
        {
            _repo = repo;
            _imageService = imageService;
        }

        public async Task<List<Product>> GetAllAsync()
        {
            return await _repo.GetAllProductsAsync();
        }

        public async Task<Product?> GetByIdAsync(int id, int companyId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            return await _repo.GetByIdAsync(id, companyId);
        }

        public async Task<List<Product>> GetByCompanyAsync(int companyId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            return await _repo.GetProductsByCompanyIdAsync(companyId);
        }

        public async Task<Product> CreateAsync(CreateProductRequest request)
        {
            if (request == null) throw new InvalidOperationException("Request cannot be null.");
            if (request.CompanyId <= 0) throw new InvalidOperationException("CompanyId must be a positive integer.");
            if (string.IsNullOrWhiteSpace(request.Name)) throw new InvalidOperationException("Product name is required.");
            if (request.Weight.HasValue && request.Weight.Value < 0) throw new InvalidOperationException("Weight cannot be negative.");

            if (!string.IsNullOrWhiteSpace(request.Sku))
            {
                if (request.Sku.Length > 100)
                    throw new InvalidOperationException("SKU is too long (max 100 chars).");

                foreach (char c in request.Sku)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                        throw new InvalidOperationException("SKU contains invalid characters.");
                }

                var exists = await _repo.GetBySkuAsync(request.Sku, request.CompanyId);
                if (exists != null)
                    throw new InvalidOperationException("SKU already exists.");
            }

            string? imageUrl = null;

            if (request.Image != null)
            {
                imageUrl = await _imageService.UploadProductImageAsync(request.Image);
            }

            var product = new Product
            {
                CompanyId = request.CompanyId,
                Sku = request.Sku,
                Name = request.Name,
                TypeId = request.TypeId,
                Category = request.Category,
                Unit = request.Unit,
                Weight = request.Weight,
                Description = request.Description,
                Image = imageUrl
            };

            return await _repo.CreateAsync(product);
        }

        public async Task<Product?> UpdateAsync(int id, UpdateProductRequest request)
        {
            if (request == null) throw new InvalidOperationException("Request cannot be null.");
            if (request.CompanyId <= 0) throw new InvalidOperationException("CompanyId must be a positive integer.");
            if (string.IsNullOrWhiteSpace(request.Sku)) throw new InvalidOperationException("SKU is required when updating a product.");
            if (request.Weight.HasValue && request.Weight.Value < 0) throw new InvalidOperationException("Weight cannot be negative.");

            // SKU format validation
            if (request.Sku.Length > 100) throw new InvalidOperationException("SKU is too long (max 100 chars).");
            foreach (char c in request.Sku)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                    throw new InvalidOperationException("SKU contains invalid characters. Only letters, digits, '-' and '_' are allowed.");
            }


            var existing = await _repo.GetByIdAsync(id, request.CompanyId);
            if (existing == null) return null;

            string? imageUrl = null;

            if (request.Image != null)
            {
                imageUrl = await _imageService.UploadProductImageAsync(request.Image);
                existing.Image = imageUrl;
            }

            if (!string.IsNullOrWhiteSpace(request.Sku) && request.Sku != existing.Sku)
            {
                var skuCollision = await _repo.GetBySkuAsync(request.Sku, request.CompanyId);
                if (skuCollision != null && skuCollision.Id != id)
                    throw new InvalidOperationException("SKU already exists.");
                existing.Sku = request.Sku;
            }

            if (request.Name != null) existing.Name = request.Name;
            existing.TypeId = request.TypeId;
            if (request.Category != null) existing.Category = request.Category;
            if (request.Unit != null) existing.Unit = request.Unit;
            if (request.Weight.HasValue) existing.Weight = request.Weight;
            if (request.Description != null) existing.Description = request.Description;

            await _repo.UpdateAsync(existing);
            return existing;
        }

        public async Task<bool> DeleteAsync(int id, int companyId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            var existing = await _repo.GetByIdAsync(id, companyId);
            if (existing == null) return false;
            await _repo.RemoveAsync(existing);
            return true;
        }

        public async Task<Product?> GetBySkuAsync(string sku, int companyId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            if (string.IsNullOrWhiteSpace(sku)) throw new InvalidOperationException("SKU is required.");
            return await _repo.GetBySkuAsync(sku, companyId);
        }

        public async Task<List<ProductType>> GetAllProductTypesAsync(int companyId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            return await _repo.GetAllProductTypesAsync(companyId);
        }

        public async Task<ProductType> CreateProductTypeAsync(CreateProductTypeRequest request)
        {
            if (request == null) throw new InvalidOperationException("Request cannot be null.");
            if (request.CompanyId <= 0) throw new InvalidOperationException("CompanyId must be a positive integer.");

            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Product type name is required.");

            var newType = new ProductType
            {
                Name = name
            };

            return await _repo.CreateProductTypeAsync(newType, request.CompanyId);
        }

        public async Task<ProductType?> UpdateProductTypeAsync(int id, UpdateProductTypeRequest request)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid product type id.");
            if (request == null) throw new InvalidOperationException("Request cannot be null.");
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Product type name is required.");

            var toUpdate = new ProductType
            {
                Id = id,
                Name = name
            };

            var updatedCount = await _repo.UpdateProductType(toUpdate);
            if (updatedCount <= 0) return null;

            return new ProductType { Id = id, Name = name };
        }

        public async Task<bool> DeleteProductTypeAsync(int id)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid product type id.");
            var toDelete = new ProductType { Id = id };
            return await _repo.RemoveProductTypeAsync(toDelete);
        }
        public async Task<int> GetCompanyIdByUserIdAsync(int userId)
        {
            if (userId <= 0) throw new InvalidOperationException("Invalid user id.");
            var companyId = await _repo.GetCompanyIdByUserIdAsync(userId);
            if (companyId == null || companyId <= 0)
                throw new InvalidOperationException("User not found or not assigned to a company.");
            return companyId.Value;
        }
        public async Task<List<ProductExportDto>> GetProductsForExportAsync()
        {
            return await _repo.GetProductsForExportAsync();
        }

        public byte[] ExportProductsToCsv(List<ProductExportDto> products)
        {
            return _repo.ExportProductsToCsv(products);
        }
        public byte[] ExportProductsToExcel(List<ProductExportDto> products)
        {
            return _repo.ExportProductsToExcel(products);
        }
    }
}
