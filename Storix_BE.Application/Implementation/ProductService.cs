using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Office2016.Excel;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Org.BouncyCastle.Asn1.Ocsp;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using Storix_BE.Repository.Implementation;
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
            if (request.CategoryId.HasValue)
            {
                var category = await _repo.GetCategoryByIdAsync(request.CategoryId.Value);

                if (category == null)
                    throw new InvalidOperationException("Product category not found.");

                if (category.CompanyId.HasValue && category.CompanyId != request.CompanyId)
                    throw new InvalidOperationException("Category does not belong to this company.");

                var hasChildren = await _repo.CategoryHasChildrenAsync(category.Id);

                if (hasChildren)
                    throw new InvalidOperationException(
                        "Product must be assigned to the lowest level category.");
            }
            var product = new Product
            {
                CompanyId = request.CompanyId,
                Sku = request.Sku,
                Name = request.Name,
                CategoryId = request.CategoryId,
                Unit = request.Unit,
                Weight = request.Weight,
                Width = request.Width,
                Length = request.Length,
                Height = request.Height,
                IsEsd = request.IsEsd,
                IsMsd = request.IsMsd,
                IsCold = request.IsCold,
                IsVulnerable = request.IsVulnerable,
                IsHighValue = request.IsHighValue,
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
            if (request.CategoryId.HasValue)
            {
                var category = await _repo.GetCategoryByIdAsync(request.CategoryId.Value);

                if (category == null)
                    throw new InvalidOperationException("Product category not found.");

                if (category.CompanyId.HasValue && category.CompanyId != request.CompanyId)
                    throw new InvalidOperationException("Category does not belong to this company.");

                var hasChildren = await _repo.CategoryHasChildrenAsync(category.Id);

                if (hasChildren)
                    throw new InvalidOperationException(
                        "Product must be assigned to the lowest level category.");
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
            existing.CategoryId = request.CategoryId;
            if (request.Unit != null) existing.Unit = request.Unit;
            if (request.Weight.HasValue) existing.Weight = request.Weight;
            if (request.Width.HasValue) existing.Width = request.Width;
            if (request.Length.HasValue) existing.Length = request.Length;
            if (request.Height.HasValue) existing.Height = request.Height;
            if (request.IsEsd.HasValue) existing.IsEsd = request.IsEsd;
            if (request.IsMsd.HasValue) existing.IsMsd = request.IsMsd;
            if (request.IsCold.HasValue) existing.IsCold = request.IsCold;
            if (request.IsVulnerable.HasValue) existing.IsVulnerable = request.IsVulnerable;
            if (request.IsHighValue.HasValue) existing.IsHighValue = request.IsHighValue;
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

            // repository will validate uniqueness for the provided company and persist
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

        public List<ProductExportDto> ParseProductsFromCsv(IFormFile file)
        {
            return _repo.ParseProductsFromCsv(file);
        }

        public List<ProductExportDto> ParseProductsFromExcel(IFormFile file)
        {
            return _repo.ParseProductsFromExcel(file);
        }

        public Task ImportProductsAsync(List<ProductExportDto> dtos)
        {
            return _repo.ImportProductsAsync(dtos);
        }
        public async Task<List<ProductCategory>> GetChildCategoriesAsync(int parentId)
        {
            if (parentId <= 0) throw new InvalidOperationException("Invalid parent category id.");
            return await _repo.GetChildCategoriesAsync(parentId);
        }
        public async Task<List<ProductCategory>> GetAllProductCategoriesAsync(int companyId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            return await _repo.GetAllProductCategoriesAsync(companyId);
        }

        public async Task<ProductCategory> CreateProductCategoryAsync(CreateProductCategoryRequest request)
        {
            if (request == null) throw new InvalidOperationException("Request cannot be null.");
            if (request.CompanyId <= 0) throw new InvalidOperationException("CompanyId must be a positive integer.");
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Product category name is required.");

            var toCreate = new ProductCategory();
            if (request.ParentCategoryId.HasValue && request.ParentCategoryId.Value == 0)
            {
                toCreate = new ProductCategory
                {
                    CompanyId = request.CompanyId,
                    Name = name,
                };
            }
            else
            {
                toCreate = new ProductCategory
                {
                    CompanyId = request.CompanyId,
                    Name = name,
                    ParentCategoryId = request.ParentCategoryId
                };
            }

            // repository will validate parent, uniqueness and compute level
            return await _repo.CreateCategoryAsync(toCreate);
        }

        public async Task<bool> DeleteProductCategoryAsync(int id)
        {
            if (id <= 0) throw new InvalidOperationException("Invalid product category id.");
            var existing = await _repo.GetCategoryByIdAsync(id);
            if (existing == null) return false;
            return await _repo.RemoveCategoryAsync(existing);
        }
        public async Task UpdateProductPopularityAsync()
        {
            await _repo.UpdateProductPopularityAsync();
        }
    }
}
