using Microsoft.EntityFrameworkCore;
using MediaLibrary.Core.Data;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Middleware;
using MediaLibrary.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Провайдер БД обирається середовищем: PostgreSQL у Production, SQLite у Development.
// Рядок підключення в обох випадках конфігураційний — ConnectionStrings:Default
// (у Production його задає змінна середовища ConnectionStrings__Default).
// Міграції згенеровані окремо під кожен провайдер — у власних збірках
// MediaLibrary.Migrations.Sqlite / MediaLibrary.Migrations.Postgres.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

if (builder.Environment.IsProduction())
{
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly("MediaLibrary.Migrations.Postgres")));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlite(connectionString, sqlite =>
            sqlite.MigrationsAssembly("MediaLibrary.Migrations.Sqlite")));
}

builder.Services.AddScoped<MediaService>();

// Зовнішні каталоги: HttpClient із DI, ключі — з конфігурації (Tmdb:ApiKey, Rawg:ApiKey).
builder.Services.AddHttpClient<TmdbService>();
builder.Services.AddHttpClient<RawgService>();
builder.Services.AddScoped<SearchAggregatorService>();

var app = builder.Build();

// Схема БД синхронізується з моделлю на старті: застосовуються міграції
// активного провайдера (SQLite для Development, PostgreSQL для Production).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapMediaEndpoints();
app.MapSearchEndpoints();
app.MapStatsEndpoints();
app.MapGenreEndpoints();

app.Run();
