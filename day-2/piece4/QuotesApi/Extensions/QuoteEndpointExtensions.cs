using Microsoft.AspNetCore.Mvc;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

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

        group.MapGet("/{id:int}", async (
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
            IQuoteService quoteService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await quoteService.CreateAsync(
                    request,
                    cancellationToken);

                logger.LogInformation(
                    "Created quote {QuoteId} by {Author}",
                    created.Id,
                    created.Author);

                return Results.Created($"/api/quotes/{created.Id}", created);
            }
            catch (QuoteValidationException exception)
            {
                var errors = exception.Errors
                    .GroupBy(error => error.Field)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Message).ToArray());

                return Results.ValidationProblem(errors);
            }
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteService quoteService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var deleted = await quoteService.DeleteAsync(
                id,
                cancellationToken);

            if (!deleted)
                return Results.NotFound();

            logger.LogInformation(
                "Deleted quote {QuoteId}",
                id);

            return Results.NoContent();
        });
    }
}
