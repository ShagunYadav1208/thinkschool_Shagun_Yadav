using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class CollectionEndpointExtensions
{
    public static void MapCollectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/collections");

        group.MapPost("/", async (CreateCollectionRequest request, ICollectionRepository repository, CancellationToken cancellationToken) =>
        {
            try
            {
                var collection = Collection.Create(request.Name, request.OwnerId);
                await repository.AddAsync(collection, cancellationToken);
                return Results.Created($"/api/collections/{collection.Id}", collection);
            }
            catch (CollectionDomainException exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Collection invariant violated", detail: exception.Message);
            }
        });

        group.MapPost("/{id:int}/items/{quoteId:int}", async (int id, int quoteId, ICollectionRepository repository, CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(id, cancellationToken);
            if (collection is null)
                return Results.NotFound();

            try
            {
                collection.AddItem(quoteId);
                await repository.UpdateAsync(collection, cancellationToken);
                return Results.Ok(collection);
            }
            catch (CollectionDomainException exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Collection invariant violated", detail: exception.Message);
            }
        });

        group.MapDelete("/{id:int}/items/{quoteId:int}", async (int id, int quoteId, ICollectionRepository repository, CancellationToken cancellationToken) =>
        {
            var collection = await repository.GetByIdAsync(id, cancellationToken);
            if (collection is null)
                return Results.NotFound();

            try
            {
                collection.RemoveItem(quoteId);
                await repository.UpdateAsync(collection, cancellationToken);
                return Results.NoContent();
            }
            catch (CollectionDomainException exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Collection invariant violated", detail: exception.Message);
            }
        });
    }
}
