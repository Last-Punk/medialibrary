using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Endpoints;

/// <summary>
/// Реальні ендпоінти /api/media (замість заглушки з Дня 2).
/// Формати відповідей і помилок — строго за CONTRACT.md.
/// </summary>
public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/media").WithTags("Media");

        group.MapGet("", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);
        group.MapGet("/{id:int}/sessions", GetSessions);
        group.MapPost("/{id:int}/sessions", AddSession);

        return app;
    }

    private static async Task<IResult> GetAll([AsParameters] MediaFilter filter, MediaService service)
    {
        try
        {
            var result = await service.GetFilteredAsync(filter);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(Error(ex.Message, "VALIDATION_ERROR"));
        }
    }

    private static async Task<IResult> GetById(int id, MediaService service)
    {
        var item = await service.GetByIdAsync(id);
        if (item is null)
            return Results.NotFound(Error("Media not found", "MEDIA_NOT_FOUND"));

        return Results.Ok(item);
    }

    private static async Task<IResult> Create(CreateMediaDto dto, MediaService service)
    {
        try
        {
            var created = await service.CreateAsync(dto);
            return Results.Created($"/api/media/{created.Id}", created);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(Error(ex.Message, "VALIDATION_ERROR"));
        }
    }

    private static async Task<IResult> Update(int id, UpdateMediaDto dto, MediaService service)
    {
        try
        {
            var updated = await service.UpdateAsync(id, dto);
            if (updated is null)
                return Results.NotFound(Error("Media not found", "MEDIA_NOT_FOUND"));

            return Results.Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(Error(ex.Message, "VALIDATION_ERROR"));
        }
    }

    private static async Task<IResult> Delete(int id, MediaService service)
    {
        var deleted = await service.DeleteAsync(id);
        if (!deleted)
            return Results.NotFound(Error("Media not found", "MEDIA_NOT_FOUND"));

        return Results.NoContent();
    }

    private static async Task<IResult> GetSessions(int id, MediaService service)
    {
        var sessions = await service.GetSessionsAsync(id);
        if (sessions is null)
            return Results.NotFound(Error("Media not found", "MEDIA_NOT_FOUND"));

        return Results.Ok(new { sessions });
    }

    private static async Task<IResult> AddSession(
        int id,
        CreateViewingSessionDto dto,
        MediaService service)
    {
        var session = await service.AddSessionAsync(id, dto);
        if (session is null)
            return Results.NotFound(Error("Media not found", "MEDIA_NOT_FOUND"));

        return Results.Created($"/api/media/{id}/sessions/{session.Id}", session);
    }

    private static object Error(string message, string code) => new
    {
        error = message,
        code = code
    };
}
