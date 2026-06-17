namespace Lumina.Services.Quizzes;

public class QuizBackgroundWorker : BackgroundService
{
    private readonly ILogger<QuizBackgroundWorker> _logger;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceProvider _serviceProvider;

    public QuizBackgroundWorker(
        IBackgroundTaskQueue taskQueue,
        ILogger<QuizBackgroundWorker> logger,
        IServiceProvider serviceProvider)
    {
        _taskQueue = taskQueue;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quiz Background Worker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await _taskQueue.DequeueAsync(stoppingToken);

            try
            {
                await workItem(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing {WorkItem}.", nameof(workItem));
            }
        }

        _logger.LogInformation("Quiz Background Worker is stopping.");
    }
}
