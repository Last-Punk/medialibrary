var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

// ⚡ Заглушка для інтеграції
app.MapGet("/api/media", () => Results.Ok(new
{
    total = 2,
    offset = 0,
    limit = 20,
    items = new[]
    {
        new { id = 1, title = "Inception", year = 2010, type = "movie",
              rating = 9, status = "completed", genres = new[] { "Sci-Fi" } },
        new { id = 2, title = "The Witcher 3", year = 2015, type = "game",
              rating = 10, status = "completed", genres = new[] { "RPG" } }
    }
}));

app.Run();