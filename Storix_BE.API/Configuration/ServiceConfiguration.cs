
// using Storix_BE.API.BackgroundJobs; // tạm tắt subscription job do deploy DB chưa có bảng subscriptions
// using Storix_BE.API.Filters; // tạm tắt filter subscription do deploy DB chưa có bảng subscriptions
using Storix_BE.Service.Implementation;
using Storix_BE.Service.Interfaces;
using Storix_BE.Service.Configuration;

namespace Storix_BE.API.Configuration
{
    public static class ServiceConfiguration
    {
        public static IServiceCollection AddServiceConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            // services.Configure<MomoGatewayOptions>(configuration.GetSection("PaymentGateways:MoMo")); // tạm tắt payment
            // services.Configure<PaymentRuntimeOptions>(configuration.GetSection("PaymentSettings")); // tạm tắt payment
            // services.AddHttpClient<IMomoAtmGatewayService, MomoAtmGatewayService>(); // tạm tắt payment

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IWarehouseAssignmentService, WarehouseAssignmentService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IInventoryInboundService, InventoryInboundService>();
            services.AddScoped<ISupplierService, SupplierService>();
            services.AddScoped<IInventoryOutboundService, InventoryOutboundService>();
            // services.AddScoped<IPaymentService, PaymentService>(); // tạm tắt payment
            // services.AddScoped<ISubscriptionService, SubscriptionService>(); // tạm tắt subscription
            services.AddScoped<IReportingService, ReportingService>();
            services.AddScoped<IInventoryCountService, InventoryCountService>();
            services.AddTransient<IImageService, ImageService>();
            services.AddTransient<IEmailService, EmailService>();

            // Filter kiểm tra subscription (Scoped vì phụ thuộc ISubscriptionService)
            // services.AddScoped<SubscriptionAccessFilter>(); // tạm tắt subscription

            // Background job tự động expire subscriptions mỗi giờ
            // services.AddHostedService<SubscriptionExpiryJob>(); // tạm tắt subscription

            return services;
        }
    }
}
