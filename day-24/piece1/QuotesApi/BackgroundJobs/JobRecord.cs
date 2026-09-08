namespace QuotesApi.BackgroundJobs;

/// <summary>
/// In-memory record of one queued job. Lives only for the lifetime of the process - there is no
/// database table backing this, which is the deliberate simplification this exercise's README
/// contrasts against Hangfire (whose job storage survives a restart; this doesn't).
/// </summary>
public class JobRecord
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Input { get; init; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}
