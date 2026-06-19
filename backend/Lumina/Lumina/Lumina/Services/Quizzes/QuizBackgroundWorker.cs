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
            try
            {
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);
                await workItem(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown: the stopping token fired while we were waiting
                // on the queue or running a work item. Exit the loop quietly.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing background work item.");
            }
        }

        _logger.LogInformation("Quiz Background Worker is stopping.");
    }
}
