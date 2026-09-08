using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Extensions;

public static class OutboxEndpointExtensions
{
    public static void MapOutboxEndpoints(this WebApplication app)
    {
        app.MapGet("/api/outbox", async (QuotesDbContext db, CancellationToken cancellationToken) =>
        {
            var rows = await db.OutboxMessages
                .AsNoTracking()
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id,
                    m.QuoteId,
                    m.EventType,
                    m.CreatedAt,
                    m.ProcessedAt,
                    m.Attempts,
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(rows);
        });
    }
}
