# API authentication and ownership

The API uses Telegram Web App `initData` only to authenticate the caller. Do not accept a Telegram ID or owner ID supplied separately by a client as proof of identity.

## Development setup

From the API project directory, store the bot token and a randomly generated JWT key in .NET User Secrets. Do not commit these values or put them in `appsettings*.json`:

```bash
cd api/MediaLibrary.Api
dotnet user-secrets set "Telegram:BotToken" "<token from BotFather>"
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -hex 32)"
```

`Jwt:Secret` must contain at least 32 UTF-8 bytes. Defaults are `Jwt:Issuer=MediaLibrary.Api`, `Jwt:Audience=MediaLibrary.Bot`, and a 24-hour token lifetime. The lifetime can be configured from 1 to 720 hours. Telegram init data is accepted for at most 1,440 minutes by default (configurable from 1 to 10,080 minutes).

For production, supply secrets through the deployment's secret manager/environment, for example `Telegram__BotToken` and `Jwt__Secret`. Never log or expose these values. Configure the database connection separately as `ConnectionStrings__Default`.

## Sign in

Send the original, unmodified `Telegram.WebApp.initData` string to the public login endpoint:

```http
POST /api/auth/telegram
Content-Type: application/json

{"initData":"query_id=...&user=...&auth_date=...&hash=..."}
```

The API validates Telegram's HMAC-SHA-256 signature using the configured bot token, rejects duplicate/missing fields, malformed users, expired data and timestamps more than five minutes in the future, then creates or updates the local user. Successful responses contain a JWT in `token` and the authenticated user summary. The JWT subject (`sub`) is the internal user ID; `tid` carries the Telegram user ID.

Invalid or stale data returns `401` with code `INVALID_INIT_DATA`. Do not parse and reserialize the init-data query string before sending it.

## Authenticated API calls

Every media, search, genre, session, and statistics endpoint requires the returned JWT:

```http
Authorization: Bearer <token>
```

Swagger UI exposes the `Bearer` scheme through its **Authorize** button. The API returns `401` with code `UNAUTHORIZED` for missing or invalid credentials.

The authenticated subject is the only source of the owner ID. Media list filters, local search, create/update/delete, imports, viewing sessions, genres, and statistics are scoped to that owner. Attempting to read, update, delete, or add sessions to another user's media returns the existing `404 MEDIA_NOT_FOUND` shape. Imported external IDs are deduplicated within an owner's library, so different users may import the same catalog item independently.

## Rate limits

The default fixed-window limits are 100 API requests per 60 seconds per authenticated user (or per caller IP when anonymous), plus 10 Telegram login attempts per 60 seconds per caller IP. Both windows, permit limits, and queue limits are configurable under `RateLimiting`. Rejections return `429` with code `RATE_LIMITED`.

`CONTRACT.md` has not been changed. Update that file only after its owner approves the contract changes.
