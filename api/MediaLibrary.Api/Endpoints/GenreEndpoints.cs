using Microsoft.EntityFrameworkCore;
using MediaLibrary.Api.Data;

namespace MediaLibrary.Api.Endpoints;

/// <summary>
/// Довідник жанрів. Віддаємо лише дані для читання, без відстеження EF.
/// </summary>
public static class GenreEndpoints
{
    public static IEndpointRouteBuilder MapGenreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/genres").WithTags("Genres");

        // Порожній шаблон зберігає точний контрактний шлях /api/genres без слеша в кінці.
        group.MapGet("", GetAll);

        return app;
    }

    private static async Task<IResult> GetAll(AppDbContext db)
    {
        var genres = await db.Genres
            .AsNoTracking()
            .OrderBy(genre => genre.Name)
            .Select(genre => new
            {
                id = genre.Id,
                name = genre.Name
            })
            .ToListAsync();

        return Results.Ok(new { genres });
    }
}
