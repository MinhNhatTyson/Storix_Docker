
using Storix_BE.Repository.Implementation;
using Storix_BE.Repository.Interfaces;

namespace Storix_BE.API.Configuration
{
    public static class RepositoryConfiguration
    {
        public static IServiceCollection AddRepositoryConfiguration(this IServiceCollection services, IConfiguration configuration)
        {

            services.AddScoped<IUserRepository, UserRepository>();
            return services;
        }
    }
}
