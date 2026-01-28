using Storix_BE.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System;
using Storix_BE.Domain.Context;


namespace Storix_BE.API.Configuration
{
    public static class DatabaseConfiguration
    {
        public static IServiceCollection AddDatabaseConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException("Connection string 'DefaultConnection' is not configured.");
            }
            services.AddDbContext<StorixDbContext>(options =>
                options.UseNpgsql(connectionString));
            return services;
        }
    }
}
