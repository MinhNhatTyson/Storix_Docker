

using Storix_BE.Service.Implementation;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.Configuration
{
    public static class ServiceConfiguration
    {
        public static IServiceCollection AddServiceConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IWarehouseAssignmentService, WarehouseAssignmentService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IInventoryInboundService, InventoryInboundService>();
            services.AddScoped<ISupplierService, SupplierService>();
            services.AddScoped<IInventoryOutboundService, InventoryOutboundService>();
            services.AddTransient<IEmailService, EmailService>();
            return services;
        }
    }
}
