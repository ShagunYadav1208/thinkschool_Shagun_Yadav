using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Caching;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.ServiceBusMessaging;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (
            int? page,
            int? size,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var currentPage = page ?? 1;
            var pageSize = size ?? 10;

            if (currentPage < 1 || pageSize < 1 || pageSize > 100)
            {
                var errors = new Dictionary<string, string[]>();

                if (currentPage < 1)
                    errors["page"] = ["Page must be greater than 0."];

                if (pageSize is < 1 or > 100)
                    errors["size"] = ["Size must be between 1 and 100."];

                return Results.ValidationProblem(errors);
            }

            var quotes = await repository.GetPagedAsync(
                currentPage,
                pageSize,
                cancellationToken);

            return Results.Ok(quotes);
        });

        // Day 21: the hot read. Wrapped in HybridCache.GetOrCreateAsync - a cache hit never
        // touches the repository/DB at all; a miss runs the factory below, and HybridCache
        // collapses any other concurrent callers asking for the *same* id during that miss into
        // the one in-flight factory call (its built-in stampede protection) rather than letting
        // each of them start its own DB read. ICacheMetrics records both sides so the load test
        // has real before/after numbers to point at, not just an assertion that it works.
        group.MapGet("/{id:int}", async (
            int id,
            HybridCache cache,
            IQuoteRepository repository,
            ICacheMetrics metrics,
            CancellationToken cancellationToken) =>
        {
            metrics.RecordCacheRequest();

            var quote = await cache.GetOrCreateAsync(
                QuoteCacheKeys.ById(id),
                async ct =>
                {
                    metrics.RecordCacheMiss();
                    return await repository.GetByIdAsync(id, ct);
                },
                cancellationToken: cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        // Day 21: load-test comparison baseline only - deliberately bypasses the cache so the
        // same endpoint shape can be hit with the same load profile as the cached route above,
        // to get an honest "before" DB-queries/sec and p99 to compare the cached "after"
        // against. Not a general-purpose duplicate of the route above.
        group.MapGet("/{id:int}/uncached", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        group.MapPost("/", async (
            CreateQuoteRequest request,
            IQuoteRepository repository,
            QuotesDbContext dbContext,
            IQuoteEventIdStore eventIds,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var validationErrors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Author))
                validationErrors["author"] = ["Author is required."];
            else if (request.Author.Length > 100)
                validationErrors["author"] = ["Author must be 100 characters or fewer."];

            if (string.IsNullOrWhiteSpace(request.Text))
                validationErrors["text"] = ["Text is required."];
            else if (request.Text.Length > 1000)
                validationErrors["text"] = ["Text must be 1000 characters or fewer."];

            if (validationErrors.Count > 0)
                return Results.ValidationProblem(validationErrors);

            var quote = new Quote
            {
                Author = request.Author.Trim(),
                Text = request.Text.Trim()
            };

            // Day 20: the domain write and the outbox write commit as one EF transaction - either
            // both land, or neither does. This replaces Day 19's inline "publish to Service Bus
            // right here, best-effort" - that approach had a real gap: if the publish silently
            // failed (or the process died between the DB commit and the publish call), the DB write
            // and the "event was sent" fact could diverge with nothing durable recording that a
            // publish was even owed. The outbox row IS that durable record; a separate relay
            // (OutboxRelayService) is the only thing that ever calls the publisher now.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var created = await repository.AddAsync(quote, cancellationToken);

            var outboxMessage = OutboxMessage.ForQuoteCreated(created);
            dbContext.OutboxMessages.Add(outboxMessage);
            await dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            // Test-harness state only, for POST /api/servicebus/replay/{quoteId} (day 19) - lets
            // that manual idempotency demo still refer to a real, relay-published message id.
            eventIds.Remember(new QuoteCreatedEvent(outboxMessage.Id, created.Id, created.Author, created.Text, outboxMessage.CreatedAt));

            logger.LogInformation(
                "Created quote {QuoteId} by {Author}, outbox row {OutboxId} queued for relay.",
                created.Id,
                created.Author,
                outboxMessage.Id);

            return Results.Created($"/api/quotes/{created.Id}", created);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            HybridCache cache,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
                id,
                cancellationToken);

            if (!deleted)
                return Results.NotFound();

            // Without this, a quote fetched (and cached) before its delete would keep serving
            // from cache for up to the entry's remaining TTL after it's gone from the DB.
            await cache.RemoveAsync(QuoteCacheKeys.ById(id), cancellationToken);

            logger.LogInformation(
                "Deleted quote {QuoteId}",
                id);

            return Results.NoContent();
        });
    }
}

public static class QuoteCacheKeys
{
    public static string ById(int id) => $"quote:{id}";
}