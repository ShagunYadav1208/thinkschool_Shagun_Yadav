using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ParkFlow.Api.Security;

/// <summary>
/// Documents the API-key requirement *in the OpenAPI contract itself* — hardening the OpenAPI
/// surface means the spec has to tell the truth about what it takes to call this API, not just
/// have the runtime enforce it silently. Applied to every operation (mirrors the fallback
/// authorization policy in Program.cs, which also applies to everything by default).
/// </summary>
internal sealed class ApiKeySecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            [ApiKeyAuthenticationDefaults.Scheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ApiKeyAuthenticationDefaults.HeaderName,
                Description = "Required on every endpoint except /health. See THREAT-MODEL.md section 5.",
            },
        };

        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations!.Values))
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationDefaults.Scheme, document)] = [],
            });
        }

        return Task.CompletedTask;
    }
}
