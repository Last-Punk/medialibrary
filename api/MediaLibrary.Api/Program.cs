using Microsoft.EntityFrameworkCore;
using MediaLibrary.Api.Data;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<MediaService>();

// Зовнішні каталоги: HttpClient із DI, ключі — з конфігурації (Tmdb:ApiKey, Rawg:ApiKey).
builder.Services.AddHttpClient<TmdbService>();
builder.Services.AddHttpClient<RawgService>();
builder.Services.AddScoped<SearchAggregatorService>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.MapMediaEndpoints();
app.MapSearchEndpoints();
app.MapStatsEndpoints();
app.MapGenreEndpoints();

app.Run();
