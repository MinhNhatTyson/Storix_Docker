using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Storix_BE.API.BackgroundJobs
{
    /// <summary>
    /// Runs once a day and ensures a monthly reset of products.popularity_score is performed
    /// (idempotent: it records the last reset in ActivityLogs and will only reset once per calendar month).
    /// </summary>
    public class ProductPopularityResetJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ProductPopularityResetJob> _logger;
        private const int RunHourUtc = 2; // hour of day in UTC when job runs (2:00 UTC)

        public ProductPopularityResetJob(IServiceScopeFactory scopeFactory, ILogger<ProductPopularityResetJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ProductPopularityResetJob starting. Will run daily at {Hour}:00 UTC.", RunHourUtc);

            // small startup delay to avoid running while migrations are being applied
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ProductPopularityResetJob encountered an error.");
                }

                // compute delay until next scheduled run (next day at RunHourUtc)
                var now = DateTime.UtcNow;
                var nextRun = new DateTime(now.Year, now.Month, now.Day, RunHourUtc, 0, 0, DateTimeKind.Utc);
                if (now >= nextRun) nextRun = nextRun.AddDays(1);
                var delay = nextRun - now;

                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // app is shutting down
                }
            }

            _logger.LogInformation("ProductPopularityResetJob stopping.");
        }

        private async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<StorixDbContext>();

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            // Check last reset recorded in ActivityLogs
            var lastReset = await ctx.ActivityLogs
                .AsNoTracking()
                .Where(l => l.Entity == "ProductPopularity" && l.Action != null && l.Action.StartsWith("MONTHLY_RESET"))
                .OrderByDescending(l => l.Timestamp)
                .Select(l => l.Timestamp)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var needReset = lastReset == null
                            || lastReset.Value.Year != now.Year
                            || lastReset.Value.Month != now.Month;

            if (!needReset)
            {
                _logger.LogDebug("ProductPopularityResetJob: monthly reset already performed for {Year}-{Month}.", now.Year, now.Month);
                return;
            }

            _logger.LogInformation("ProductPopularityResetJob: performing monthly reset of popularity_score for {Year}-{Month}.", now.Year, now.Month);

            // Perform reset (single SQL to avoid loading all entities)
            await ctx.Database.ExecuteSqlRawAsync("UPDATE products SET popularity_score = 0;", cancellationToken).ConfigureAwait(false);

            // Record audit log
            ctx.ActivityLogs.Add(new ActivityLog
            {
                UserId = null,
                Entity = "ProductPopularity",
                EntityId = 0,
                Action = $"MONTHLY_RESET:PERFORMED_AT={now:O}",
                Timestamp = now
            });

            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("ProductPopularityResetJob: reset completed for {Year}-{Month}.", now.Year, now.Month);
        }
    }
}