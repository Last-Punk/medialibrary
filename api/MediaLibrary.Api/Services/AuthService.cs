using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MediaLibrary.Api.Configuration;
using MediaLibrary.Core.Data;
using MediaLibrary.Core.Models.Entities;

namespace MediaLibrary.Api.Services;

/// <summary>Result returned after successful Telegram authentication.</summary>
public sealed record TelegramAuthResult(string Token, User User);

/// <summary>
/// Authenticates Telegram Web App or trusted bot-server identities and issues user API JWTs.
/// </summary>
public sealed class AuthService
{
    private const string TelegramWebAppDataKey = "WebAppData";
    private const int MaximumInitDataLength = 16 * 1024;
    private static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly TelegramOptions _telegramOptions;
    private readonly JwtOptions _jwtOptions;
    private readonly BotAuthOptions _botAuthOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;

    /// <summary>Creates the authentication service.</summary>
    public AuthService(
        AppDbContext db,
        IOptions<TelegramOptions> telegramOptions,
        IOptions<JwtOptions> jwtOptions,
        IOptions<BotAuthOptions> botAuthOptions,
        TimeProvider timeProvider,
        ILogger<AuthService> logger)
    {
        _db = db;
        _telegramOptions = telegramOptions.Value;
        _jwtOptions = jwtOptions.Value;
        _botAuthOptions = botAuthOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a Telegram Web App init-data payload and returns a signed API token.
    /// Invalid signatures, malformed payloads, and stale or future auth dates return null.
    /// </summary>
    public async Task<TelegramAuthResult?> AuthenticateTelegramAsync(
        string? initData,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken))
            throw new InvalidOperationException("Telegram bot token is not configured.");

        var now = _timeProvider.GetUtcNow();
        if (!TryValidateInitData(initData, now, out var telegramUser))
        {
            _logger.LogWarning("Rejected invalid Telegram init data.");
            return null;
        }

        var user = await UpsertUserAsync(telegramUser, now, updateProfile: true, cancellationToken);
        _logger.LogInformation("Authenticated application user {UserId} through Telegram Web App.", user.Id);
        return new TelegramAuthResult(CreateToken(user, now), user);
    }

    /// <summary>Gets whether the trusted bot-to-API credential is configured.</summary>
    public bool IsBotAuthenticationConfigured => !string.IsNullOrWhiteSpace(_botAuthOptions.ApiKey);

    /// <summary>
    /// Authenticates the trusted bot server and issues a user-scoped JWT for a Telegram account.
    /// Invalid server credentials return null. Profile details are preserved because the bot
    /// identity bridge only asserts the Telegram user ID from a trusted Telegram update.
    /// </summary>
    public async Task<TelegramAuthResult?> AuthenticateBotAsync(
        string? apiKey,
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        if (!HasValidBotApiKey(apiKey))
        {
            _logger.LogWarning("Rejected bot authentication with invalid server credentials.");
            return null;
        }

        if (telegramUserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(telegramUserId), "Telegram user ID must be positive.");

        var now = _timeProvider.GetUtcNow();
        var user = await UpsertUserAsync(
            new TelegramIdentity(telegramUserId, null, null),
            now,
            updateProfile: false,
            cancellationToken);

        _logger.LogInformation("Authenticated application user {UserId} through the trusted bot server.", user.Id);
        return new TelegramAuthResult(CreateToken(user, now), user);
    }

    private bool HasValidBotApiKey(string? apiKey)
    {
        if (!IsBotAuthenticationConfigured || string.IsNullOrEmpty(apiKey))
            return false;

        var expected = Encoding.UTF8.GetBytes(_botAuthOptions.ApiKey);
        var supplied = Encoding.UTF8.GetBytes(apiKey);
        return supplied.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private async Task<User> UpsertUserAsync(
        TelegramIdentity telegramUser,
        DateTimeOffset now,
        bool updateProfile,
        CancellationToken cancellationToken)
    {
        var user = await _db.Users.SingleOrDefaultAsync(
            item => item.TelegramUserId == telegramUser.Id,
            cancellationToken);

        if (user is null)
        {
            user = new User
            {
                TelegramUserId = telegramUser.Id,
                Username = updateProfile ? telegramUser.Username : null,
                FirstName = updateProfile ? telegramUser.FirstName : null,
                CreatedAtUtc = now.UtcDateTime,
                LastSeenAtUtc = now.UtcDateTime
            };

            _db.Users.Add(user);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent first authentication may win the unique TelegramUserId insert.
                _db.Entry(user).State = EntityState.Detached;
                user = await _db.Users.SingleOrDefaultAsync(
                    item => item.TelegramUserId == telegramUser.Id,
                    cancellationToken);

                if (user is null)
                    throw;

                if (updateProfile)
                    UpdateUserProfile(user, telegramUser, now);
                else
                    UpdateLastSeen(user, now);

                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            if (updateProfile)
                UpdateUserProfile(user, telegramUser, now);
            else
                UpdateLastSeen(user, now);

            await _db.SaveChangesAsync(cancellationToken);
        }

        return user;
    }

    private bool TryValidateInitData(
        string? initData,
        DateTimeOffset now,
        out TelegramIdentity telegramUser)
    {
        telegramUser = default;
        if (string.IsNullOrWhiteSpace(initData) || initData.Length > MaximumInitDataLength)
            return false;

        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> fields;
        try
        {
            fields = QueryHelpers.ParseQuery(initData);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (fields.Count == 0 || fields.Any(field => field.Value.Count != 1))
            return false;

        if (!TryGetSingleValue(fields, "hash", out var hash)
            || !TryGetSingleValue(fields, "auth_date", out var authDateValue)
            || !TryGetSingleValue(fields, "user", out var userJson))
        {
            return false;
        }

        var dataCheckString = string.Join(
            "\n",
            fields
                .Where(field => !string.Equals(field.Key, "hash", StringComparison.Ordinal))
                .Select(field => new KeyValuePair<string, string>(field.Key, field.Value[0] ?? string.Empty))
                .OrderBy(field => field.Key, StringComparer.Ordinal)
                .Select(field => $"{field.Key}={field.Value}"));

        if (!HasValidHash(dataCheckString, hash))
            return false;

        if (!long.TryParse(
                authDateValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var authDateUnixSeconds))
        {
            return false;
        }

        DateTimeOffset authDate;
        try
        {
            authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var maximumAge = TimeSpan.FromMinutes(_telegramOptions.InitDataMaxAgeMinutes);
        if (_telegramOptions.InitDataMaxAgeMinutes <= 0
            || authDate < now.Subtract(maximumAge)
            || authDate > now.Add(MaximumFutureClockSkew))
        {
            return false;
        }

        return TryParseTelegramUser(userJson, out telegramUser);
    }

    private bool HasValidHash(string dataCheckString, string suppliedHash)
    {
        if (suppliedHash.Length > 128)
            return false;

        var botToken = Encoding.UTF8.GetBytes(_telegramOptions.BotToken);
        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes(TelegramWebAppDataKey), botToken);
        var expectedHash = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));

        byte[] suppliedHashBytes;
        try
        {
            suppliedHashBytes = Convert.FromHexString(suppliedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHashBytes);
    }

    private static bool TryParseTelegramUser(string userJson, out TelegramIdentity telegramUser)
    {
        telegramUser = default;
        try
        {
            using var document = JsonDocument.Parse(userJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.Number
                || !idElement.TryGetInt64(out var id)
                || id <= 0
                || !TryGetOptionalString(root, "username", out var username)
                || !TryGetOptionalString(root, "first_name", out var firstName))
            {
                return false;
            }

            telegramUser = new TelegramIdentity(id, username, firstName);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetOptionalString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property))
            return true;

        if (property.ValueKind == JsonValueKind.Null)
            return true;

        if (property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString();
        return true;
    }

    private static bool TryGetSingleValue(
        IReadOnlyDictionary<string, Microsoft.Extensions.Primitives.StringValues> fields,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!fields.TryGetValue(key, out var values) || values.Count != 1)
            return false;

        value = values[0] ?? string.Empty;
        return value.Length > 0;
    }

    private static void UpdateUserProfile(User user, TelegramIdentity telegramUser, DateTimeOffset now)
    {
        user.Username = telegramUser.Username;
        user.FirstName = telegramUser.FirstName;
        UpdateLastSeen(user, now);
    }

    private static void UpdateLastSeen(User user, DateTimeOffset now) =>
        user.LastSeenAtUtc = now.UtcDateTime;

    private string CreateToken(User user, DateTimeOffset now)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim("tid", user.TelegramUserId.ToString(CultureInfo.InvariantCulture))
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddHours(_jwtOptions.ExpiresHours).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private readonly record struct TelegramIdentity(long Id, string? Username, string? FirstName);
}
