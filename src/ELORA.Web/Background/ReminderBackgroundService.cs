using ELORA.Web.Services;

namespace ELORA.Web.Background;

/// <summary>Периодически рассылает напоминания о визитах (примерно за 24 часа).</summary>
public sealed class ReminderBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly IServiceProvider _services;
    private readonly ILogger<ReminderBackgroundService> _logger;

    public ReminderBackgroundService(IServiceProvider services, ILogger<ReminderBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Даём приложению подняться и применить миграции/seed.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var reminder = scope.ServiceProvider.GetRequiredService<ReminderService>();
                var count = await reminder.RunOnceAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("Отправлено напоминаний: {Count}", count);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка цикла напоминаний");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
