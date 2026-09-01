using System.Collections.Concurrent;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Singleton, process-lifetime job registry. ConcurrentDictionary because both the request
/// thread (creating a job, reading status) and the QueuedHostedService's drain loop (mutating
/// status as it runs) touch it concurrently.
/// </summary>
public class JobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();

    public JobRecord Create(string type, string input)
    {
        var job = new JobRecord { Id = Guid.NewGuid(), Type = type, Input = input };
        _jobs[job.Id] = job;
        return job;
    }

    public JobRecord? Get(Guid id) => _jobs.GetValueOrDefault(id);

    public IReadOnlyCollection<JobRecord> GetAll() =>
        _jobs.Values.OrderByDescending(j => j.CreatedAt).ToList();
}
