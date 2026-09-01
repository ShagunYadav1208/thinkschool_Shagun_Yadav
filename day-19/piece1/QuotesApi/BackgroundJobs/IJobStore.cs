namespace QuotesApi.BackgroundJobs;

public interface IJobStore
{
    JobRecord Create(string type, string input);

    JobRecord? Get(Guid id);

    IReadOnlyCollection<JobRecord> GetAll();
}
