using QuotesApi.BackgroundJobs;

namespace QuotesApi.Extensions;

public static class JobEndpointExtensions
{
    public static void MapJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/jobs");

        // Enqueues, returns immediately - the caller gets a job id back and polls for status
        // instead of the request thread blocking for the ~5 seconds the analysis simulates.
        group.MapPost("/quote-analysis/{quoteId:int}", async (
            int quoteId,
            IJobStore jobStore,
            IBackgroundTaskQueue queue,
            CancellationToken cancellationToken) =>
        {
            var job = jobStore.Create("quote-analysis", quoteId.ToString());
            await queue.QueueAsync(QuoteAnalysisJob.Create(job.Id, quoteId), cancellationToken);

            return Results.Accepted($"/api/jobs/{job.Id}", job);
        });

        group.MapGet("/", (IJobStore jobStore) => Results.Ok(jobStore.GetAll()));

        group.MapGet("/{id:guid}", (Guid id, IJobStore jobStore) =>
        {
            var job = jobStore.Get(id);
            return job is null ? Results.NotFound() : Results.Ok(job);
        });
    }
}
