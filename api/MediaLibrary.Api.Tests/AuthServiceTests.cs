using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.WebUtilities;
using MediaLibrary.Api.Configuration;
using MediaLibrary.Api.Services;
using MediaLibrary.Core.Data;
using MediaLibrary.Core.Models.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaLibrary.Api.Tests;

/// <summary>Exercises Telegram Web App HMAC validation, freshness checks, profile upsert, and JWT issuance.</summary>
public sealed class AuthServiceTests
{
    private const string BotToken = "123456:unit-test-telegram-bot-token";
    private const string JwtSecret = "test-only-signing-secret-with-more-than-32-bytes";
    private const string Issuer = "MediaLibrary.Tests";
    private const string Audience = "MediaLibrary.Tests.Client";
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthenticateTelegramAsync_ValidInitDataCreatesUserAndReturnsSignedJwt()
    {
        using var db = CreateDb();
        var auth = CreateService(db, Now);
        var initData = CreateInitData(Now, telegramUserId: 73421);

        var result = await auth.AuthenticateTelegramAsync(initData);

        Assert.NotNull(result);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(73421L, result.User.TelegramUserId);
        Assert.Equal("alice_test", result.User.Username);
        Assert.Equal("Alice", result.User.FirstName);
        Assert.Equal(Now.UtcDateTime, result.User.CreatedAtUtc);
        Assert.Equal(Now.UtcDateTime, result.User.LastSeenAtUtc);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(
            result.Token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret))
            },
            out var validatedToken);

        Assert.Equal(result.User.Id.ToString(CultureInfo.InvariantCulture), principal.FindFirst("sub")?.Value);
        Assert.Equal("73421", principal.FindFirst("tid")?.Value);
        Assert.Equal(Now.AddHours(6).UtcDateTime, ((JwtSecurityToken)validatedToken).ValidTo);
    }

    [Fact]
    public async Task AuthenticateTelegramAsync_AllowsOmittedOptionalProfileFields()
    {
        using var db = CreateDb();
        var initData = CreateInitData(Now, "{\"id\":73421}");

        var result = await CreateService(db, Now).AuthenticateTelegramAsync(initData);

        Assert.NotNull(result);
        Assert.Null(result.User.Username);
        Assert.Null(result.User.FirstName);
    }

    [Fact]
    public async Task AuthenticateTelegramAsync_ExistingTelegramUserIsUpdatedNotDuplicated()
    {
        using var db = CreateDb();
        var originalCreatedAt = Now.AddDays(-10).UtcDateTime;
        db.Users.Add(new User
        {
            TelegramUserId = 73421,
            Username = "old_name",
            FirstName = "Old name",
            CreatedAtUtc = originalCreatedAt,
            LastSeenAtUtc = originalCreatedAt
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, Now);
        var result = await service.AuthenticateTelegramAsync(CreateInitData(Now, telegramUserId: 73421));

        Assert.NotNull(result);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal("alice_test", result.User.Username);
        Assert.Equal("Alice", result.User.FirstName);
        Assert.Equal(originalCreatedAt, result.User.CreatedAtUtc);
        Assert.Equal(Now.UtcDateTime, result.User.LastSeenAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuthenticateTelegramAsync_EmptyInitDataIsRejected(string? initData)
    {
        using var db = CreateDb();

        var result = await CreateService(db, Now).AuthenticateTelegramAsync(initData);

        Assert.Null(result);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Theory]
    [InlineData("missing-hash")]
    [InlineData("missing-auth-date")]
    [InlineData("missing-user")]
    [InlineData("duplicate-hash")]
    [InlineData("duplicate-auth-date")]
    [InlineData("duplicate-user")]
    [InlineData("invalid-hex-hash")]
    [InlineData("wrong-length-hash")]
    [InlineData("tampered-user")]
    [InlineData("malformed-auth-date")]
    [InlineData("stale-auth-date")]
    [InlineData("future-auth-date")]
    [InlineData("malformed-user-json")]
    [InlineData("user-not-object")]
    [InlineData("missing-user-id")]
    [InlineData("zero-user-id")]
    [InlineData("negative-user-id")]
    [InlineData("string-user-id")]
    [InlineData("boolean-user-id")]
    [InlineData("null-user-id")]
    [InlineData("object-user-id")]
    [InlineData("array-user-id")]
    [InlineData("invalid-first-name-type")]
    [InlineData("oversized-init-data")]
    public async Task AuthenticateTelegramAsync_RejectsMalformedTamperedOrInvalidInitData(string scenario)
    {
        using var db = CreateDb();
        var initData = CreateInitData(Now);

        initData = scenario switch
        {
            "missing-hash" => RemoveField(initData, "hash"),
            "missing-auth-date" => RemoveField(initData, "auth_date"),
            "missing-user" => RemoveField(initData, "user"),
            "duplicate-hash" => initData + "&hash=" + GetField(initData, "hash"),
            "duplicate-auth-date" => initData + "&auth_date=" + Now.ToUnixTimeSeconds(),
            "duplicate-user" => initData + "&user=%7B%7D",
            "invalid-hex-hash" => SetField(initData, "hash", new string('z', 64)),
            "wrong-length-hash" => SetField(initData, "hash", "abcd"),
            "tampered-user" => SetField(initData, "user", JsonSerializer.Serialize(new { id = 999, username = "mallory" })),
            "malformed-auth-date" => CreateInitData("not-a-timestamp", DefaultUserJson()),
            "stale-auth-date" => CreateInitData(Now.AddMinutes(-31)),
            "future-auth-date" => CreateInitData(Now.AddMinutes(6)),
            "malformed-user-json" => CreateInitData(Now, "{"),
            "user-not-object" => CreateInitData(Now, "[]"),
            "missing-user-id" => CreateInitData(Now, "{\"first_name\":\"Alice\"}"),
            "zero-user-id" => CreateInitData(Now, "{\"id\":0}"),
            "negative-user-id" => CreateInitData(Now, "{\"id\":-12}"),
            "string-user-id" => CreateInitData(Now, "{\"id\":\"73421\"}"),
            "boolean-user-id" => CreateInitData(Now, "{\"id\":true}"),
            "null-user-id" => CreateInitData(Now, "{\"id\":null}"),
            "object-user-id" => CreateInitData(Now, "{\"id\":{}}"),
            "array-user-id" => CreateInitData(Now, "{\"id\":[]}"),
            "invalid-first-name-type" => CreateInitData(Now, "{\"id\":73421,\"first_name\":42}"),
            "oversized-init-data" => new string('a', 16 * 1024 + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown test case.")
        };

        var result = await CreateService(db, Now).AuthenticateTelegramAsync(initData);

        Assert.Null(result);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task AuthenticateTelegramAsync_AcceptsSignedFieldsInAnyQueryOrder()
    {
        using var db = CreateDb();
        var initData = CreateInitData(Now, extraFields: new Dictionary<string, string>
        {
            ["chat_type"] = "private",
            ["chat_instance"] = "987654321",
            ["query_id"] = "AAEAAAE"
        });

        var result = await CreateService(db, Now).AuthenticateTelegramAsync(initData);

        Assert.NotNull(result);
        Assert.Equal(73421L, result.User.TelegramUserId);
    }

    [Fact]
    public async Task AuthenticateTelegramAsync_UsesConfiguredMaximumAgeBoundary()
    {
        using var db = CreateDb();
        var exactBoundary = Now.AddMinutes(-30);

        var result = await CreateService(db, Now, maxAgeMinutes: 30)
            .AuthenticateTelegramAsync(CreateInitData(exactBoundary));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task AuthenticateTelegramAsync_RejectsWhenMaximumAgeIsExceededByOneSecond()
    {
        using var db = CreateDb();
        var justTooOld = Now.AddMinutes(-30).AddSeconds(-1);

        var result = await CreateService(db, Now, maxAgeMinutes: 30)
            .AuthenticateTelegramAsync(CreateInitData(justTooOld));

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateTelegramAsync_AllowsFiveMinutesOfFutureClockSkew()
    {
        using var db = CreateDb();
        var boundary = Now.AddMinutes(5);

        var result = await CreateService(db, Now).AuthenticateTelegramAsync(CreateInitData(boundary));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task AuthenticateTelegramAsync_RejectsWhenBotTokenIsNotConfigured()
    {
        using var db = CreateDb();

        var service = CreateService(db, Now, botToken: " ");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AuthenticateTelegramAsync(CreateInitData(Now)));
    }

    private static AuthService CreateService(
        AppDbContext db,
        DateTimeOffset now,
        int maxAgeMinutes = 30,
        string botToken = BotToken) =>
        new(
            db,
            Options.Create(new TelegramOptions
            {
                BotToken = botToken,
                InitDataMaxAgeMinutes = maxAgeMinutes
            }),
            Options.Create(new JwtOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                Secret = JwtSecret,
                ExpiresHours = 6
            }),
            new FrozenTimeProvider(now),
            NullLogger<AuthService>.Instance);

    private static string CreateInitData(
        DateTimeOffset authDate,
        long telegramUserId = 73421,
        IDictionary<string, string>? extraFields = null) =>
        CreateInitData(authDate.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), DefaultUserJson(telegramUserId), extraFields);

    private static string CreateInitData(
        DateTimeOffset authDate,
        string userJson,
        IDictionary<string, string>? extraFields = null) =>
        CreateInitData(
            authDate.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            userJson,
            extraFields);

    private static string CreateInitData(
        string authDate,
        string userJson,
        IDictionary<string, string>? extraFields = null)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authDate,
            ["query_id"] = "AAEAAAEAAAE",
            ["user"] = userJson
        };
        if (extraFields is not null)
        {
            foreach (var item in extraFields)
                fields[item.Key] = item.Value;
        }

        return SignAndEncode(fields);
    }

    private static string SignAndEncode(Dictionary<string, string> fields)
    {
        var dataCheckString = string.Join(
            "\n",
            fields.OrderBy(field => field.Key, StringComparer.Ordinal)
                .Select(field => $"{field.Key}={field.Value}"));
        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(BotToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString)));
        fields["hash"] = hash;

        // Deliberately reverse the field order; Telegram's canonical check string is sorted by key.
        return string.Join("&", fields.OrderByDescending(field => field.Key, StringComparer.Ordinal)
            .Select(field => $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}"));
    }

    private static string GetField(string initData, string key) => QueryHelpers.ParseQuery(initData)[key].ToString();

    private static string RemoveField(string initData, string key)
    {
        var fields = ParseFields(initData);
        fields.Remove(key);
        return Encode(fields);
    }

    private static string SetField(string initData, string key, string value)
    {
        var fields = ParseFields(initData);
        fields[key] = value;
        return Encode(fields);
    }

    private static Dictionary<string, string> ParseFields(string initData) =>
        QueryHelpers.ParseQuery(initData).ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToString(),
            StringComparer.Ordinal);

    private static string Encode(Dictionary<string, string> fields) =>
        string.Join("&", fields.Select(field => $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}"));

    private static string DefaultUserJson(long id = 73421) => JsonSerializer.Serialize(new
    {
        id,
        first_name = "Alice",
        username = "alice_test"
    });

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AuthServiceTests-{Guid.NewGuid()}")
            .Options);

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
