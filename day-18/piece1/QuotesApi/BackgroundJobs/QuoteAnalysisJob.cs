using QuotesApi.Repositories;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// The slow work this exercise moves off the request thread: "analyze" one quote (word/character
/// counts). The 5-second delay is simulated and chunked into 1-second steps specifically so the
/// cancellation token is observed mid-flight - a single `Task.Delay(5000, token)` would only ever
/// see the token in one place, but a real slow operation (e.g. several sequential DB/API calls)
/// checks it between steps the same way this does.
/// </summary>
public static class QuoteAnalysisJob
{
    public static Func<IServiceProvider, CancellationToken, Task> Create(Guid jobId, int quoteId)
    {
        return async (services, cancellationToken) =>
        {
            var jobStore = services.GetRequiredService<IJobStore>();
            var repository = services.GetRequiredService<IQuoteRepository>();
            var logger = services.GetRequiredService<ILogger<Program>>();

            var job = jobStore.Get(jobId);
            if (job is null)
                return;

            job.Status = JobStatus.Running;
            job.StartedAt = DateTimeOffset.UtcNow;

            try
            {
                var quote = await repository.GetByIdAsync(quoteId, cancellationToken);

                if (quote is null)
                {
                    job.Status = JobStatus.Failed;
                    job.Error = $"Quote {quoteId} not found.";
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    return;
                }

                for (var step = 1; step <= 5; step++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    job.Result = $"Analyzing... step {step}/5";
                }

                var words = quote.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var longestWord = words.OrderByDescending(w => w.Length).First();

                job.Result = $"{words.Length} words, {quote.Text.Length} characters, longest word \"{longestWord}\".";
                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;

                logger.LogInformation("Job {JobId} completed analysis of quote {QuoteId}.", jobId, quoteId);
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatus.Cancelled;
                job.Error = "Cancelled by application shutdown before the job finished.";
                job.CompletedAt = DateTimeOffset.UtcNow;
                throw;
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.Error = ex.Message;
                job.CompletedAt = DateTimeOffset.UtcNow;
                throw;
            }
        };
    }
}
