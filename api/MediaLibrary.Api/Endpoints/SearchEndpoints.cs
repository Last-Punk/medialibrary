using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Endpoints;

/// <summary>
/// Search endpoints defined by CONTRACT.md: GET /api/search and POST /api/search/import.
/// Handlers are separate methods because a lambda returning different IResult types
/// (Results.Ok, Created, or Conflict) does not support type inference here.
/// </summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search")
            .WithTags("Search")
            .RequireAuthorization();

        group.MapGet("", Search)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/import", Import)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// GET /api/search?query=...&amp;type=...
    /// Always return { "results": [...] }, including when the search finds nothing.
    /// </summary>
    private static async Task<IResult> Search(
        string? query,
        string? type,
        SearchAggregatorService service)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Results.BadRequest(Error("Query is required", "VALIDATION_ERROR"));

        try
        {
            var results = await service.SearchAsync(query, type);
            return Results.Ok(new { results });
        }
        catch (ArgumentException ex)
        {
            // An invalid type uses the same validation error as /api/media.
            return Results.BadRequest(Error(ex.Message, "VALIDATION_ERROR"));
        }
    }

    /// <summary>
    /// POST /api/search/import imports a selected result into the library.
    /// If genres are omitted, they are fetched from the matching external catalog.
    /// A duplicate ExternalId + ExternalSource pair returns 409 MEDIA_DUPLICATE.
    /// </summary>
    private static async Task<IResult> Import(
        ImportMediaDto dto,
        MediaService service,
        SearchAggregatorService catalogs)
    {
        try
        {
            var created = await service.ImportFromExternalAsync(dto, catalogs.FetchImportGenresAsync);
            return Results.Created($"/api/media/{created.Id}", created);
        }
        catch (DuplicateException ex)
        {
            return Results.Conflict(Error(ex.Message, "MEDIA_DUPLICATE"));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(Error(ex.Message, "VALIDATION_ERROR"));
        }
    }

    private static object Error(string message, string code) => new
    {
        error = message,
        code = code
    };
}
