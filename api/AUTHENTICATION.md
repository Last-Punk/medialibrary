# API authentication and ownership

The API supports two sign-in paths that resolve to the same local user by `TelegramUserId`: Telegram Web App `initData` and the trusted server-side bot integration. Do not accept a Telegram ID or owner ID supplied separately by a public client as proof of identity.

## Development setup

From the API project directory, store the Telegram bot token, a randomly generated JWT key, and a separate bot-to-API shared key in .NET User Secrets. Do not commit these values or put them in `appsettings*.json`:

```bash
cd api/MediaLibrary.Api
dotnet user-secrets set "Telegram:BotToken" "<token from BotFather>"
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -hex 32)"
dotnet user-secrets set "BotAuth:ApiKey" "$(openssl rand -hex 32)"
```

`Jwt:Secret` and `BotAuth:ApiKey` must each contain at least 32 UTF-8 bytes and must not be the same secret. Defaults are `Jwt:Issuer=MediaLibrary.Api`, `Jwt:Audience=MediaLibrary.Bot`, and a 24-hour token lifetime. The lifetime can be configured from 1 to 720 hours. Telegram init data is accepted for at most 1,440 minutes by default (configurable from 1 to 10,080 minutes).

For production, supply secrets through the deployment's secret manager/environment, for example `Telegram__BotToken`, `Jwt__Secret`, and `BotAuth__ApiKey`. `BotAuth:ApiKey` is required in Production. Never log or expose these values. Configure the database connection separately as `ConnectionStrings__Default`.

## Telegram Web App sign-in

Send the original, unmodified `Telegram.WebApp.initData` string to the public login endpoint:

```http
POST /api/auth/telegram
Content-Type: application/json

{"initData":"query_id=...&user=...&auth_date=...&hash=..."}
```

The API validates Telegram's HMAC-SHA-256 signature using the configured bot token, rejects duplicate/missing fields, malformed users, expired data and timestamps more than five minutes in the future, then creates or updates the local user. Successful responses contain a JWT in `token` and the authenticated user summary. The JWT subject (`sub`) is the internal user ID; `tid` carries the Telegram user ID.

Invalid or stale data returns `401` with code `INVALID_INIT_DATA`. Do not parse and reserialize the init-data query string before sending it.

## Trusted bot-server sign-in

Only the bot server may call `POST /api/auth/bot`. The bot server must first obtain a genuine Telegram update and use its Telegram user ID; never forward an ID supplied by a browser or treat a bot command's arbitrary text/callback data as a verified identity. Keep this request server-to-server over HTTPS, keep the shared key only in server-side secret storage, and do not expose this endpoint through the Web App.

```http
POST /api/auth/bot
X-Bot-Api-Key: <BotAuth:ApiKey>
Content-Type: application/json

{"telegramUserId":73421}
```

The API compares `X-Bot-Api-Key` to the configured shared secret using a fixed-time comparison. A missing or incorrect credential returns `401` with code `UNAUTHORIZED`. In Development, an unset key disables the route with `503 BOT_AUTH_UNAVAILABLE`; startup validation requires a key in Production and rejects configured keys shorter than 32 UTF-8 bytes. Requests are rate-limited by caller IP.

After the credential is verified, the API creates or finds the same local user used by Web App sign-in and returns the same user-scoped JWT format. This route only asserts the Telegram user ID: when it creates an account, optional profile fields remain empty; for an existing user, the bot sign-in preserves the profile already stored. Do not share the server key with clients; callers use the returned JWT, not the server key, for normal API requests.

Swagger UI documents `X-Bot-Api-Key` for this endpoint and `Bearer` JWTs for authenticated API operations. The bot key belongs only in the bot server's configuration and `BotAuth:ApiKey` on the API host. Rotate both copies together if it is exposed.

## Authenticated API calls

Every media, search, genre, session, and statistics endpoint requires the returned JWT:

```http
Authorization: Bearer <token>
```

Swagger UI exposes the `Bearer` scheme through its **Authorize** button. The API returns `401` with code `UNAUTHORIZED` for missing or invalid credentials.

The authenticated subject is the only source of the owner ID. Media list filters, local search, create/update/delete, imports, viewing sessions, genres, and statistics are scoped to that owner. Attempting to read, update, delete, or add sessions to another user's media returns the existing `404 MEDIA_NOT_FOUND` shape. Imported external IDs are deduplicated within an owner's library, so different users may import the same catalog item independently.

## Rate limits

The default fixed-window limits are 100 API requests per 60 seconds per authenticated user (or per caller IP when anonymous), 10 Telegram Web App login attempts per 60 seconds per caller IP, and 100 bot-server login attempts per 60 seconds per caller IP. Permit limits, window durations, and queue limits are configurable under `RateLimiting`. Rejections return `429` with code `RATE_LIMITED`.

`CONTRACT.md` has not been changed. Update that file only after its owner approves the contract changes.
