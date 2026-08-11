using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (QuotesDbContext db, CancellationToken cancellationToken) =>
        {
            var quotes = await db.Quotes.AsNoTracking().OrderBy(quote => quote.Id).ToListAsync(cancellationToken);
            return Results.Ok(quotes);
        }).AllowAnonymous();

        group.MapPost("/", async (
            CreateQuoteRequest request,
            QuotesDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Author) || string.IsNullOrWhiteSpace(request.Text))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quote"] = ["Author and text are required."]
                });

            var quote = new Quote
            {
                Author = request.Author.Trim(),
                Text = request.Text.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            await db.Quotes.AddAsync(quote, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(quote);
        }).RequireAuthorization();

        group.MapDelete("/{id:int}", async (int id, QuotesDbContext db, CancellationToken cancellationToken) =>
        {
            var quote = await db.Quotes.FindAsync([id], cancellationToken);
            if (quote is null)
                return Results.NotFound();

            db.Quotes.Remove(quote);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
