using Microsoft.EntityFrameworkCore;
using MediaLibrary.Api.Security;
using MediaLibrary.Core.Data;

namespace MediaLibrary.Api.Endpoints;

/// <summary>
/// Read-only genre catalog; query without EF change tracking.
/// </summary>
public static class GenreEndpoints
{
    public static IEndpointRouteBuilder MapGenreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/genres")
            .WithTags("Genres")
            .RequireAuthorization();

        // An empty route pattern preserves the exact /api/genres path without a trailing slash.
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
