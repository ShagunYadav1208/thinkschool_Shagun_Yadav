namespace QuotesApi.BackgroundJobs;

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}
