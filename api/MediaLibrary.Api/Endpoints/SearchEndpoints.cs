using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Endpoints;

/// <summary>
/// Ендпоінти пошуку за CONTRACT.md: GET /api/search та POST /api/search/import.
/// Обробники — окремі методи, бо лямбда з різними типами IResult
/// (Results.Ok / Created / Conflict) не проходить виведення типу.
/// </summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search").WithTags("Search");

        group.MapGet("", Search);
        group.MapPost("/import", Import);

        return app;
    }

    /// <summary>
    /// GET /api/search?query=...&amp;type=...
    /// Відповідь завжди { "results": [...] } — навіть коли нічого не знайдено.
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
            // Невалідний type — та сама помилка валідації, що й у /api/media.
            return Results.BadRequest(Error(ex.Message, "VALIDATION_ERROR"));
        }
    }

    /// <summary>
    /// POST /api/search/import — імпорт обраного результату в медіатеку.
    /// Дублікат за ExternalId + ExternalSource → 409 MEDIA_DUPLICATE.
    /// </summary>
    private static async Task<IResult> Import(ImportMediaDto dto, MediaService service)
    {
        try
        {
            var created = await service.ImportFromExternalAsync(dto);
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
