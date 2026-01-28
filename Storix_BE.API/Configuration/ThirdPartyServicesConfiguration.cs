
using Microsoft.Extensions.Options;

namespace CarServ.API.Configuration
{
    public static class ThirdPartyServicesConfiguration
    {
        public static IServiceCollection AddThirdPartyServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.ThirdPartyServicesCollection();
            return services;
        }

        public static void ThirdPartyServicesCollection(this IServiceCollection services)
        {
            
        }

        private static string GetEnvironmentVariableOrThrow(string key)
        {
            return Environment.GetEnvironmentVariable(key)
                   ?? throw new ArgumentNullException(key, $"Environment variable '{key}' is not set.");
        }
    }
}
