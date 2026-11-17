using Microsoft.EntityFrameworkCore;
using MediaLibrary.Api.Security;
using MediaLibrary.Core.Data;

namespace MediaLibrary.Api.Endpoints;

/// <summary>
/// Довідник жанрів. Віддаємо лише дані для читання, без відстеження EF.
/// </summary>
public static class GenreEndpoints
{
    public static IEndpointRouteBuilder MapGenreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/genres")
            .WithTags("Genres")
            .RequireAuthorization();

        // Порожній шаблон зберігає точний контрактний шлях /api/genres без слеша в кінці.
        group.MapGet("", GetAll);

        return app;
    }

    private static async Task<IResult> GetAll(AppDbContext db, ICurrentUser currentUser)
    {
        var ownerId = currentUser.UserId;
        var genres = await db.Genres
            .AsNoTracking()
            .Where(genre => genre.MediaItems.Any(item => item.OwnerId == ownerId))
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
