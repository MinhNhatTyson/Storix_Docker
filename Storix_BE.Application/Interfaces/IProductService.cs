using Microsoft.AspNetCore.Http;
using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IProductService
    {
        Task<Product?> GetByIdAsync(int id, int companyId);
        Task<Product?> GetBySkuAsync(string sku, int companyId);
        Task<List<Product>> GetAllAsync();
        Task<Product> CreateAsync(CreateProductRequest request);
        Task<Product?> UpdateAsync(int id, UpdateProductRequest request);
        Task<bool> DeleteAsync(int id, int companyId);

        Task<List<Product>> GetByCompanyAsync(int companyId);


        Task<List<ProductType>> GetAllProductTypesAsync(int companyId);
        Task<ProductType> CreateProductTypeAsync(CreateProductTypeRequest request);
        Task<ProductType?> UpdateProductTypeAsync(int id, UpdateProductTypeRequest request);
        Task<bool> DeleteProductTypeAsync(int id);
        Task<int> GetCompanyIdByUserIdAsync(int userId);
    }
    public sealed record CreateProductTypeRequest(int CompanyId, string Name);
    public sealed record UpdateProductTypeRequest(string Name);
    public sealed record CreateProductRequest(
        int CompanyId,
        string? Sku,
        string? Name,
        int? TypeId,
        string? Category,
        string? Unit,
        double? Weight,
        string? Description,
        IFormFile? Image);

    public sealed record UpdateProductRequest(
        int CompanyId,
        string Sku,
        string? Name,
        int? TypeId,
        string? Category,
        string? Unit,
        double? Weight,
        string? Description,
        IFormFile? Image);
}
