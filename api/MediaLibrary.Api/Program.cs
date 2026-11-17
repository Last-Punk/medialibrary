using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MediaLibrary.Api.Configuration;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Middleware;
using MediaLibrary.Api.Services;
using MediaLibrary.Core.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddOptions<JwtOptions>()
    .Bind(jwtSection)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
    .Validate(
        options => Encoding.UTF8.GetByteCount(options.Secret ?? string.Empty) >= 32,
        "Jwt:Secret must contain at least 32 UTF-8 bytes.")
    .Validate(options => options.ExpiresHours is > 0 and <= 720, "Jwt:ExpiresHours must be between 1 and 720.")
    .ValidateOnStart();

builder.Services.AddOptions<TelegramOptions>()
    .BindConfiguration(TelegramOptions.SectionName)
    .Validate(
        options => options.InitDataMaxAgeMinutes is > 0 and <= 10080,
        "Telegram:InitDataMaxAgeMinutes must be between 1 and 10080.")
    .Validate(
        options => !builder.Environment.IsProduction() || !string.IsNullOrWhiteSpace(options.BotToken),
        "Telegram:BotToken is required in Production.")
    .ValidateOnStart();

var signingKey = string.IsNullOrWhiteSpace(jwtOptions.Secret)
    ? null
    : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.Response.HasStarted)
                    return;

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Unauthorized",
                    code = "UNAUTHORIZED"
                });
            }
        };
    });
builder.Services.AddAuthorization();

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
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton(TimeProvider.System);

// Зовнішні каталоги: HttpClient із DI, ключі — з конфігурації (Tmdb:ApiKey, Rawg:ApiKey).
builder.Services.AddHttpClient<TmdbService>();
builder.Services.AddHttpClient<RawgService>();
builder.Services.AddScoped<SearchAggregatorService>();

var app = builder.Build();

// Схема БД синхронізується з моделлю на старті: застосовуються міграції
// активного провайдера (SQLite для Development, PostgreSQL для Production).
using (var scope = app.Services.CreateScope())
{
    // Fail before touching the database if security-critical configuration is invalid.
    _ = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapMediaEndpoints();
app.MapSearchEndpoints();
app.MapStatsEndpoints();
app.MapGenreEndpoints();

app.Run();
