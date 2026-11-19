using Microsoft.EntityFrameworkCore;
using MediaLibrary.Api.Security;
using MediaLibrary.Core.Data;

namespace MediaLibrary.Api.Endpoints;

/// <summary>Read-only tag dictionary scoped to the authenticated owner's library.</summary>
public static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tags")
            .WithTags("Tags")
            .RequireAuthorization();

        group.MapGet("", GetAll);

        return app;
    }

    private static async Task<IResult> GetAll(AppDbContext db, ICurrentUser currentUser)
    {
        var ownerId = currentUser.UserId;
        var tags = await db.Tags
            .AsNoTracking()
            .Where(tag => tag.MediaItems.Any(item => item.OwnerId == ownerId))
            .OrderBy(tag => tag.Name)
            .Select(tag => new
            {
                id = tag.Id,
                name = tag.Name
            })
            .ToListAsync();

        return Results.Ok(new { tags });
    }
}
