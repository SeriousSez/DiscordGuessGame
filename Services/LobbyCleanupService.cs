namespace DiscordGuessGame.Services;

public class LobbyCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LobbyCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

    public LobbyCleanupService(IServiceProvider serviceProvider, ILogger<LobbyCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lobby cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var lobbyService = scope.ServiceProvider.GetRequiredService<LobbyService>();
                    lobbyService.CleanupExpiredLobbies();
                }
            }
            catch (TaskCanceledException)
            {
                // Service is shutting down
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during lobby cleanup");
            }
        }

        _logger.LogInformation("Lobby cleanup service stopped");
    }
}
