using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.BackgroundJobs
{
    /// <summary>
    /// Background service chạy mỗi giờ để tự động expire các subscription quá hạn.
    /// Sử dụng IServiceScopeFactory để tạo scoped DI scope cho mỗi lần chạy.
    /// </summary>
    public class SubscriptionExpiryJob : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionExpiryJob> _logger;

        public SubscriptionExpiryJob(
            IServiceScopeFactory scopeFactory,
            ILogger<SubscriptionExpiryJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SubscriptionExpiryJob khởi động, chạy mỗi {Interval}.", Interval);

            // Delay ngắn khi startup để tránh chạy trước khi DB migrations hoàn tất
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }

            _logger.LogInformation("SubscriptionExpiryJob dừng.");
        }

        private async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Tạo scope mới cho mỗi lần chạy vì ISubscriptionService là Scoped
                await using var scope = _scopeFactory.CreateAsyncScope();
                var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

                _logger.LogDebug("SubscriptionExpiryJob: bắt đầu expire subscriptions...");
                await subscriptionService.ExpireSubscriptionsAsync();
                _logger.LogDebug("SubscriptionExpiryJob: hoàn tất.");
            }
            catch (OperationCanceledException)
            {
                // Bình thường khi app shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubscriptionExpiryJob: lỗi khi expire subscriptions.");
                // Không throw - job sẽ tiếp tục chạy lần sau
            }
        }
    }
}
