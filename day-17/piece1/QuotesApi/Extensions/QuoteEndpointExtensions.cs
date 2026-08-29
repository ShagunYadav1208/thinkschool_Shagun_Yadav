using Microsoft.AspNetCore.Mvc;
using QuotesApi.Models;
using QuotesApi.Repositories;

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
            IQuoteRepository repository,
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

            var created = await repository.AddAsync(
                quote,
                cancellationToken);

            logger.LogInformation(
                "Created quote {QuoteId} by {Author}",
                created.Id,
                created.Author);

            return Results.Created($"/api/quotes/{created.Id}", created);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
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