using Microsoft.EntityFrameworkCore;
using MediaLibrary.Api.Security;
using MediaLibrary.Core.Data;

namespace MediaLibrary.Api.Endpoints;

/// <summary>
/// Aggregated library statistics as defined by CONTRACT.md.
/// Grouping is performed in SQL; enums are converted to strings in memory.
/// </summary>
public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stats")
            .WithTags("Stats")
            .RequireAuthorization();

        group.MapGet("", GetStats);

        return app;
    }

    private static async Task<IResult> GetStats(AppDbContext db, ICurrentUser currentUser)
    {
        var ownerId = currentUser.UserId;
        var media = db.MediaItems
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId);

        var total = await media.CountAsync();

        var statusRows = await media
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync();

        var typeRows = await media
            .GroupBy(item => item.Type)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToListAsync();

        // EF does not translate enum.ToString(), so apply the contract lowercase format after the SQL query.
        var byStatus = statusRows.Select(row => new
        {
            status = row.Status.ToString().ToLowerInvariant(),
            count = row.Count
        }).ToList();

        var byType = typeRows.Select(row => new
        {
            type = row.Type.ToString().ToLowerInvariant(),
            count = row.Count
        }).ToList();

        var average = await media
            .Where(item => item.Rating != null)
            .Select(item => (double?)item.Rating)
            .AverageAsync();

        // The contract represents an empty rating set as 0 rather than null.
        var avgRating = average.HasValue ? Math.Round(average.Value, 1) : 0;

        var topGenres = await db.Genres
            .AsNoTracking()
            .Where(genre => genre.MediaItems.Any(item => item.OwnerId == ownerId))
            .Select(genre => new
            {
                name = genre.Name,
                count = genre.MediaItems.Count(item => item.OwnerId == ownerId)
            })
            .OrderByDescending(genre => genre.count)
            .ThenBy(genre => genre.name)
            .Take(5)
            .ToListAsync();

        return Results.Ok(new
        {
            total,
            byStatus,
            byType,
            avgRating,
            topGenres
        });
    }
}
