using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MediaLibrary.Api.Configuration;
using MediaLibrary.Api.Documentation;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Middleware;
using MediaLibrary.Api.Security;
using MediaLibrary.Api.Services;
using MediaLibrary.Core.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the JWT returned by POST /api/auth/telegram."
    });
    options.OperationFilter<BearerSecurityOperationFilter>();
});

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
var rateLimitingOptions = builder.Configuration.GetSection(RateLimitingOptions.SectionName)
    .Get<RateLimitingOptions>() ?? new RateLimitingOptions();
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

builder.Services.AddOptions<BotAuthOptions>()
    .BindConfiguration(BotAuthOptions.SectionName)
    .Validate(
        options => string.IsNullOrWhiteSpace(options.ApiKey)
            || Encoding.UTF8.GetByteCount(options.ApiKey) >= 32,
        "BotAuth:ApiKey must contain at least 32 UTF-8 bytes when configured.")
    .Validate(
        options => !builder.Environment.IsProduction() || !string.IsNullOrWhiteSpace(options.ApiKey),
        "BotAuth:ApiKey is required in Production.")
    .Validate(
        options => string.IsNullOrWhiteSpace(options.ApiKey)
            || !string.Equals(options.ApiKey, jwtOptions.Secret, StringComparison.Ordinal),
        "BotAuth:ApiKey must be distinct from Jwt:Secret.")
    .ValidateOnStart();

builder.Services.AddOptions<RateLimitingOptions>()
    .BindConfiguration(RateLimitingOptions.SectionName)
    .Validate(
        options => options.GlobalPermitLimit > 0
            && options.AuthPermitLimit > 0
            && options.BotAuthPermitLimit > 0,
        "Rate limiting permit limits must be positive.")
    .Validate(
        options => options.GlobalWindowSeconds > 0
            && options.AuthWindowSeconds > 0
            && options.BotAuthWindowSeconds > 0,
        "Rate limiting windows must be positive.")
    .Validate(
        options => options.GlobalQueueLimit >= 0
            && options.AuthQueueLimit >= 0
            && options.BotAuthQueueLimit >= 0,
        "Rate limiting queue limits cannot be negative.")
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
            OnTokenValidated = context =>
            {
                var subject = context.Principal?.FindFirst("sub")?.Value;
                if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
                    || userId <= 0)
                {
                    context.Fail("The token does not contain a valid user subject.");
                }

                return Task.CompletedTask;
            },
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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests", code = "RATE_LIMITED" },
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPartitionKeys.UserOrIp(context),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimitingOptions.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitingOptions.GlobalWindowSeconds),
                QueueLimit = rateLimitingOptions.GlobalQueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    options.AddPolicy("telegram-auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPartitionKeys.Ip(context),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimitingOptions.AuthPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitingOptions.AuthWindowSeconds),
                QueueLimit = rateLimitingOptions.AuthQueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    options.AddPolicy("bot-auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPartitionKeys.Ip(context),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimitingOptions.BotAuthPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitingOptions.BotAuthWindowSeconds),
                QueueLimit = rateLimitingOptions.BotAuthQueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
});

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

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
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
    _ = scope.ServiceProvider.GetRequiredService<IOptions<BotAuthOptions>>().Value;
    _ = scope.ServiceProvider.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapMediaEndpoints();
app.MapSearchEndpoints();
app.MapStatsEndpoints();
app.MapGenreEndpoints();

app.Run();
