using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapPost("/", async (
            CreateQuoteRequest request,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var creation = Quote.Create(request.Author, request.Text);
            if (!creation.IsSuccess)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quote"] = [creation.Error!]
                });
            }

            var quote = await repository.AddAsync(creation.Quote!, cancellationToken);
            return Results.Created($"/api/quotes/{quote.Id}", quote);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetActiveByIdAsync(id, cancellationToken);
            if (quote is null)
                return Results.NotFound();

            quote.SoftDelete();
            await repository.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }
}
