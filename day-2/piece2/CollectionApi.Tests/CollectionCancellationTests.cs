using CollectionApi.Models;
using CollectionApi.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CollectionApi.Tests;

public class CollectionCancellationTests
{
    [Fact]
    public async Task GetCollections_WhenRequestIsCancelled_CancelsTheRepositoryOperation()
    {
        var slowRepository = new SlowCollectionRepository();
        await using var factory = new CollectionApiFactory(slowRepository);
        using var client = factory.CreateClient();
        using var cancellationSource = new CancellationTokenSource();

        var requestTask = client.GetAsync("/api/collections/", cancellationSource.Token);
        await slowRepository.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestTask);
        await slowRepository.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class CollectionApiFactory(SlowCollectionRepository repository) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICollectionRepository>();
                services.AddScoped<ICollectionRepository>(_ => repository);
            });
        }
    }

    private sealed class SlowCollectionRepository : ICollectionRepository
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<CollectionItem>> GetAllAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }

            return [];
        }

        public Task<CollectionItem> AddAsync(CollectionItem item, CancellationToken cancellationToken) =>
            Task.FromResult(item);
    }
}
