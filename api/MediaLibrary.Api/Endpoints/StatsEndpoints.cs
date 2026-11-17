using Microsoft.EntityFrameworkCore;
using MediaLibrary.Core.Data;

namespace MediaLibrary.Api.Endpoints;

/// <summary>
/// Агрегована статистика медіатеки за CONTRACT.md.
/// Групування виконується в SQL, а enum перетворюється на рядок уже в пам'яті.
/// </summary>
public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stats").WithTags("Stats");

        group.MapGet("", GetStats);

        return app;
    }

    private static async Task<IResult> GetStats(AppDbContext db)
    {
        var media = db.MediaItems.AsNoTracking();

        var total = await media.CountAsync();

        var statusRows = await media
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync();

        var typeRows = await media
            .GroupBy(item => item.Type)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToListAsync();

        // EF не транслює enum.ToString(), тому контрактний нижній регістр робимо після SQL.
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

        // Порожня вибірка оцінок — це 0 за контрактом, а не null.
        var avgRating = average.HasValue ? Math.Round(average.Value, 1) : 0;

        var topGenres = await db.Genres
            .AsNoTracking()
            .Select(genre => new
            {
                name = genre.Name,
                count = genre.MediaItems.Count()
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
