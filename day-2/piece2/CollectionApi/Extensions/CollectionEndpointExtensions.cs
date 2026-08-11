using CollectionApi.Models;
using CollectionApi.Services;

namespace CollectionApi.Extensions;

public static class CollectionEndpointExtensions
{
    public static void MapCollectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/collections");

        group.MapGet("/", async (
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var items = await service.GetAllAsync(cancellationToken);
            return Results.Ok(items);
        });

        group.MapPost("/", async (
            CreateCollectionItemRequest request,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name is required."]
                });
            }

            var item = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/collections/{item.Id}", item);
        });
    }
}
