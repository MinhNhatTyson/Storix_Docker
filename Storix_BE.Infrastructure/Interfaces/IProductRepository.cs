using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IProductRepository
    {
        Task<List<Product>> GetAllProductsAsync();
        Task<Product?> GetByIdAsync(int id, int companyId);
        Task<Product?> GetBySkuAsync(string sku, int companyId);
        Task<List<Product>> GetProductsByCompanyIdAsync(int companyId);
        Task<Product> CreateAsync(Product product);
        Task<int> UpdateAsync(Product product);
        Task<bool> RemoveAsync(Product product);
        Task<List<ProductType>> GetAllProductTypesAsync(int companyId);
        Task<ProductType> CreateProductTypeAsync(ProductType type, int companyId);
        Task<int> UpdateProductType(ProductType type);
        Task<bool> RemoveProductTypeAsync(ProductType type);
        Task<int?> GetCompanyIdByUserIdAsync(int userId);
        Task<List<ProductExportDto>> GetProductsForExportAsync();
        byte[] ExportProductsToCsv(List<ProductExportDto> products);
        byte[] ExportProductsToExcel(List<ProductExportDto> products);
    }
}
