using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParkFlow.Modules.Reservation.Application.Abstractions;

namespace ParkFlow.Modules.Reservation.Infrastructure.BackgroundJobs;

/// <summary>
/// Flow 2 from the README: periodically finds reservations nobody confirmed/acted on in time and
/// expires them. Conceptual for Day 22 — a fixed in-process poll rather than a distributed job
/// scheduler, and it does not yet publish the ReservationExpired integration event itself (that
/// would need this worker to also own an IIntegrationEventPublisher call per reservation, wired the
/// same way ReservationApplicationService does it).
/// </summary>
public sealed class ReservationExpirationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReservationExpirationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var candidates = await repository.GetExpiredCandidatesAsync(DateTimeOffset.UtcNow, stoppingToken);

            foreach (var reservation in candidates)
            {
                reservation.Expire();
            }

            if (candidates.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Expired {Count} reservation(s).", candidates.Count);
            }
        }
    }
}
