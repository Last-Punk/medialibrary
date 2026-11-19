# MediaLibrary API

The REST API for a personal library of movies, TV series, and games. It provides Telegram-based sign-in, owner-scoped library operations, catalog search through TMDB and RAWG, viewing sessions, genres, tags, and statistics. The API contract is documented in [`../CONTRACT.md`](../CONTRACT.md).

## Technology

- .NET 9 / C# 13 / ASP.NET Core Minimal APIs
- Entity Framework Core 9
- SQLite in Development; PostgreSQL in Production
- Swashbuckle (Swagger/OpenAPI)
- xUnit and EF Core InMemory for tests (`MediaLibrary.Api.Tests`)

## Requirements

- .NET 9 SDK
- OpenSSL if you want to generate random local secrets with the commands below
- Optional: EF Core 9 tooling for creating or applying migrations (`dotnet tool install --global dotnet-ef --version 9.0.0`)

## Local development

From the repository root, restore and test the solution:

```bash
dotnet restore api/MediaLibrary.sln
dotnet test api/MediaLibrary.sln
```

Configure secrets in .NET User Secrets; never commit them or place them in tracked settings files. `Jwt:Secret` is required. `BotAuth:ApiKey` enables the trusted bot-server sign-in endpoint. `Telegram:BotToken` is needed to validate Telegram Web App `initData` during local testing.

```bash
cd api/MediaLibrary.Api
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -hex 32)"
dotnet user-secrets set "BotAuth:ApiKey" "$(openssl rand -hex 32)"
dotnet user-secrets set "Telegram:BotToken" "<token from BotFather>"
dotnet run --launch-profile http
```

The Development HTTP profile listens on `http://localhost:5021`. Open Swagger UI at [`http://localhost:5021/swagger`](http://localhost:5021/swagger); the OpenAPI document is available at `/swagger/v1/swagger.json`. The checked-in [`swagger.json`](./swagger.json) is a generated snapshot, not the source of truth. Regenerate it after endpoint, response-status, or security metadata changes and before release.

The Development database uses SQLite (`Data Source=medialibrary.db`). The API applies the active provider's existing migrations automatically at startup. **Do not run `dotnet ef database update` as part of normal local setup.** If a schema change requires a new migration, create and apply it deliberately. The design-time factory selects its provider from `DOTNET_ENVIRONMENT` first, then `ASPNETCORE_ENVIRONMENT`, so set both when generating or applying migrations. From `api/MediaLibrary.Api`, use these commands for SQLite:

```bash
DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <MigrationName> --project ../MediaLibrary.Migrations.Sqlite --startup-project .
DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ../MediaLibrary.Migrations.Sqlite --startup-project .
```

For PostgreSQL migrations, set both variables to `Production`, target `../MediaLibrary.Migrations.Postgres`, and provide `ConnectionStrings__Default` when applying them.

The API contract lists `http://localhost:5000` as its development base URL. To use that port instead of the launch profile's `5021`, run the API with `dotnet run --urls http://localhost:5000`.

To build and test from the `api/` directory instead, run `dotnet restore`, `dotnet build`, and `dotnet test` against `MediaLibrary.sln`.

## Authentication and ownership

- `POST /api/auth/telegram` validates Telegram Web App `initData` and returns a user-scoped JWT.
- `POST /api/auth/bot` exchanges the trusted bot server's `X-Bot-Api-Key` and Telegram user ID for the same user-scoped JWT format.
- Protected API requests require `Authorization: Bearer <token>`.
- The JWT subject identifies the local owner. Library reads and writes are scoped to that owner; another user's media is reported as `404 MEDIA_NOT_FOUND`.

See [Authentication and ownership](./AUTHENTICATION.md) for request examples, secret requirements, validation behavior, error codes, and security details.

## API endpoints

| Method | Path | Purpose | Success |
|---|---|---|---|
| `POST` | `/api/auth/telegram` | Sign in with Telegram Web App `initData` | `200` |
| `POST` | `/api/auth/bot` | Trusted bot-server sign-in exchange | `200` |
| `GET` | `/api/media` | List owned items; supports type, status, genre, minimum-rating, search, and pagination filters | `200 { total, offset, limit, items }` |
| `GET` | `/api/media/{id}` | Get one owned item | `200` / `404` |
| `POST` | `/api/media` | Create a manual item | `201` / `400` |
| `PUT` | `/api/media/{id}` | Partially update an owned item; omitted or null fields are unchanged | `200` / `400` / `404` |
| `DELETE` | `/api/media/{id}` | Delete an item and its viewing sessions | `204` / `404` |
| `GET` | `/api/media/{id}/sessions` | List viewing sessions, newest first | `200 { sessions }` / `404` |
| `POST` | `/api/media/{id}/sessions` | Add a viewing session; date defaults to the current UTC time | `201` / `404` |
| `GET` | `/api/search?query=…&type=…` | Search the local library and external catalogs | `200 { results }` / `400` / `503` |
| `POST` | `/api/search/import` | Import a catalog result into the caller's library; fetches genres when omitted | `201` / `400` / `409` / `503` |
| `GET` | `/api/genres` | List genres used in the caller's library, sorted by name | `200 { genres }` |
| `GET` | `/api/tags` | List tags used in the caller's library, sorted by name | `200 { tags }` |
| `GET` | `/api/stats` | Get owner-scoped totals, status/type counts, average rating, and top genres | `200` |

Search checks the local database first, followed by TMDB for movies and series and RAWG for games. Results are deduplicated by `ExternalSource + ExternalId` without case sensitivity; a local match takes priority over its external catalog match. If any selected provider is unavailable or its API key is missing, the request returns `503 EXTERNAL_API_UNAVAILABLE` instead of partial results. When an import request omits genres or supplies an empty list, the API fetches them from the matching catalog before saving the item.

## Request examples

Set `API_BASE` to the local URL and use a user JWT returned by one of the sign-in endpoints:

```bash
API_BASE=http://localhost:5021
TOKEN="<user JWT>"
```

Create a manual media item:

```bash
curl -X POST "$API_BASE/api/media" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title":"Inception","type":"movie","year":2010,"genres":["Sci-Fi"],"rating":9,"status":"completed"}'
```

Search for a movie:

```bash
curl "$API_BASE/api/search?query=inception&type=movie" \
  -H "Authorization: Bearer $TOKEN"
```

Import a catalog result. The result is owner-scoped, and a duplicate `ExternalSource + ExternalId` pair in that owner's library returns `409 MEDIA_DUPLICATE`:

```bash
curl -X POST "$API_BASE/api/search/import" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"externalId":"27205","externalSource":"TMDB","title":"Inception","type":"movie"}'
```

## Configuration

Values can be provided through `appsettings*.json`, .NET User Secrets, or environment variables. In environment variables, replace `:` with `__`. Keep secret values in User Secrets or the deployment host's secret manager.

| Configuration key | Environment variable | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | Required. Defaults to SQLite in Development; Production uses PostgreSQL. |
| `Jwt:Secret` | `Jwt__Secret` | Required; at least 32 UTF-8 bytes. |
| `Jwt:Issuer` | `Jwt__Issuer` | Defaults to `MediaLibrary.Api`. |
| `Jwt:Audience` | `Jwt__Audience` | Defaults to `MediaLibrary.Bot`. |
| `Jwt:ExpiresHours` | `Jwt__ExpiresHours` | Defaults to 24; allowed range is 1–720 hours. |
| `Telegram:BotToken` | `Telegram__BotToken` | Used to validate Web App `initData`; required in Production. |
| `Telegram:InitDataMaxAgeMinutes` | `Telegram__InitDataMaxAgeMinutes` | Defaults to 1,440 minutes; allowed range is 1–10,080 minutes. |
| `BotAuth:ApiKey` | `BotAuth__ApiKey` | Enables bot-server sign-in; required in Production, at least 32 UTF-8 bytes, and distinct from `Jwt:Secret`. |
| `Tmdb:ApiKey` | `Tmdb__ApiKey` | Optional configuration; required for TMDB movie/TV search and genre imports. If missing, requests that use TMDB return `503 EXTERNAL_API_UNAVAILABLE`. |
| `Tmdb:BaseUrl` | `Tmdb__BaseUrl` | Defaults to `https://api.themoviedb.org/3/`. |
| `Tmdb:ImageBaseUrl` | `Tmdb__ImageBaseUrl` | Defaults to `https://image.tmdb.org/t/p/w500`. |
| `Rawg:ApiKey` | `Rawg__ApiKey` | Optional configuration; required for RAWG game search and genre imports. If missing, requests that use RAWG return `503 EXTERNAL_API_UNAVAILABLE`. |
| `Rawg:BaseUrl` | `Rawg__BaseUrl` | Defaults to `https://api.rawg.io/api/`. |
| `ASPNETCORE_ENVIRONMENT` | — | `Development` selects SQLite; `Production` selects PostgreSQL. |

The application can start in Development without TMDB or RAWG keys, but search and genre imports that require an unconfigured or unavailable catalog return `503 EXTERNAL_API_UNAVAILABLE`. Local library CRUD and statistics remain available.

## Rate limits

Defaults are configured under `RateLimiting`:

- 100 API requests per 60 seconds per authenticated user, or per IP for anonymous requests.
- 10 Telegram Web App sign-in attempts per 60 seconds per IP.
- 100 bot-server sign-in attempts per 60 seconds per IP.

The permit limits and window durations are configurable. Rate-limit rejections return `429 RATE_LIMITED`.

## Data, errors, and contract rules

- Media types: `movie`, `series`, `game`; statuses: `plan`, `watching`, `completed`, `dropped`. Input matching is case-insensitive; responses use lowercase values.
- Timestamps are ISO 8601 UTC values with the `Z` suffix.
- Pagination defaults to `offset=0` and `limit=20`; `offset` must be non-negative and the maximum limit is 100.
- API errors use `{ "error": "…", "code": "…" }`.

| HTTP status | Code | Description |
|---|---|---|
| `400` | `VALIDATION_ERROR` | Invalid request data, including unsupported types, out-of-range values, or an empty search query |
| `401` | `UNAUTHORIZED` / `INVALID_INIT_DATA` | Missing or invalid authentication credentials or Telegram `initData` |
| `404` | `MEDIA_NOT_FOUND` | The requested item or its viewing-session parent was not found for the caller |
| `409` | `MEDIA_DUPLICATE` | The caller already has the same external item |
| `429` | `RATE_LIMITED` | A configured request limit was exceeded |
| `500` | `INTERNAL_ERROR` | Unexpected server error; details are logged, not returned |
| `503` | `EXTERNAL_API_UNAVAILABLE` | TMDB or RAWG is unavailable or its API key is not configured |
| `503` | `BOT_AUTH_UNAVAILABLE` | Bot-server sign-in is disabled because its Development key is not configured |

## Architecture

```text
MediaLibrary.Api/                 HTTP endpoints, authentication, and application services
  Endpoints/                       Minimal API routes and HTTP response mapping
  Services/                        Authentication, media CRUD, sessions, search and statistics
MediaLibrary.Core/                 Domain entities and AppDbContext
  Entities                         Media items, genres, tags, and viewing sessions
MediaLibrary.Migrations.Sqlite/    EF Core migrations for Development
MediaLibrary.Migrations.Postgres/  EF Core migrations for Production
```

- **Request pipeline:** Swagger, error handling, authentication, rate limiting, and authorization run before endpoint handlers.
- **Dependency injection:** `MediaService` and `SearchAggregatorService` are scoped; TMDB and RAWG use DI-managed `HttpClient` instances.
- **Migrations:** SQLite and PostgreSQL migrations live in separate assemblies. Startup applies the migrations for the active provider.
- **Data model:** `MediaItem` and `Genre`/`Tag` use many-to-many relationships; `MediaItem` and `ViewingSession` use a one-to-many relationship with cascading delete.

## Deployment status and Docker

The production service has not been deployed, so there is no production Swagger URL to publish. Add a verified production URL here after deployment. The API's Dockerfile is in `api/`; it builds the .NET 9 application and listens on port `8080`.

To build a container image from the API directory:

```bash
cd api
docker build -f Dockerfile -t medialibrary-api .
```

For a future Production deployment, configure `ASPNETCORE_ENVIRONMENT=Production`, a PostgreSQL `ConnectionStrings__Default`, and secrets for `Jwt__Secret`, `Telegram__BotToken`, and `BotAuth__ApiKey` in the host's secret manager. The JWT secret and bot key must be distinct and each at least 32 UTF-8 bytes. Optional catalog keys are `Tmdb__ApiKey` and `Rawg__ApiKey`. The container applies the PostgreSQL migrations at startup, so a separate migration step is not required for the normal deployment flow.

For a local container smoke test, substitute real values from a local secret store:

```bash
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ConnectionStrings__Default="Host=<host>;Database=medialibrary;Username=<user>;Password=<password>" \
  -e Jwt__Secret="$JWT_SECRET" \
  -e Telegram__BotToken="$TELEGRAM_BOT_TOKEN" \
  -e BotAuth__ApiKey="$BOT_AUTH_API_KEY" \
  medialibrary-api
```

The Docker build context must be the `api/` directory because the Dockerfile copies multiple API, Core, and migration projects. For a hosted web service, expose container port `8080` and provide the configuration values through the host's environment/secret manager. Do not commit production secrets or put them in tracked settings files.

## Related documentation

- [Repository overview and architecture](../README.md)
- [API contract](../CONTRACT.md)
- [Authentication and ownership](./AUTHENTICATION.md)
