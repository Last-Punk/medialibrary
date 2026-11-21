using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MediaLibrary.Api.Documentation;

/// <summary>Adds the appropriate security scheme to protected API operations.</summary>
public sealed class BearerSecurityOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var endpointMetadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (endpointMetadata?.OfType<BotApiKeyAuthenticationMetadata>().Any() == true)
        {
            AddSecurityRequirement(operation, "BotApiKey", context.Document);
            return;
        }

        if (endpointMetadata?.OfType<IAllowAnonymous>().Any() == true)
            return;

        AddSecurityRequirement(operation, "Bearer", context.Document);
    }

    private static void AddSecurityRequirement(
        OpenApiOperation operation,
        string scheme,
        OpenApiDocument document)
    {
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, document)] = new List<string>()
        });
    }
}
