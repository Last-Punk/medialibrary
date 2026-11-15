# MediaLibrary.Api

REST API особистої медіатеки (фільми, серіали, ігри) з інтеграцією зовнішніх каталогів TMDB і RAWG. Контракт взаємодії з клієнтом (Telegram-ботом) зафіксований у [`../CONTRACT.md`](../CONTRACT.md).

## Стек

- .NET 9 / C# 13, ASP.NET Core Minimal API
- EF Core 9 — SQLite (development) або PostgreSQL (production), провайдер обирається середовищем
- Swashbuckle (Swagger/OpenAPI)
- xUnit + EF Core InMemory (тести, проєкт `MediaLibrary.Api.Tests`)

## Вимоги

- .NET SDK 9.0+
- (Опціонально) `dotnet-ef` для роботи з міграціями: `dotnet tool install --global dotnet-ef`

## Запуск локально

```bash
cd api/MediaLibrary.Api
dotnet restore

# Схема БД застосовується автоматично на старті застосунку (Database.Migrate):
# для Development береться набір міграцій з проєкту MediaLibrary.Migrations.Sqlite.
dotnet run

# Лише коли модель змінилася — нова міграція чи ручне оновлення локальної БД:
dotnet ef migrations add <MigrationName> --project ../MediaLibrary.Migrations.Sqlite --startup-project .
dotnet ef database update --project ../MediaLibrary.Migrations.Sqlite --startup-project .
```

Дев-profіль (`Properties/launchSettings.json`, `ASPNETCORE_ENVIRONMENT=Development`) слухає `http://localhost:5021`. Для повної відповідності контракту (base URL `http://localhost:5000`):

```bash
dotnet run --urls http://localhost:5000
```

Рядок підключення development середовища — `ConnectionStrings:Default` у `appsettings.Development.json` (`Data Source=medialibrary.db`).

Запуск тестів:

```bash
cd api
dotnet test MediaLibrary.Api.Tests
```

## Конфігурація

Усі параметри читаються з конфігурації (`appsettings*.json` або змінні середовища, де `:` замінюється на `__`). Секрети в репозиторій не комітяться; `appsettings.Production.json` у `.gitignore`.

| Ключ | Змінна середовища | Призначення |
|---|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | SQLite (`Data Source=...`) або PostgreSQL (`Host=...;Database=...;Username=...;Password=...`) |
| `Tmdb:ApiKey` | `Tmdb__ApiKey` | API-ключ TMDB (v3). Без ключа зовнішній пошук фільмів/серіалів пропускається |
| `Tmdb:BaseUrl` | `Tmdb__BaseUrl` | Веб-адреса TMDB API (default `https://api.themoviedb.org/3/`) |
| `Tmdb:ImageBaseUrl` | `Tmdb__ImageBaseUrl` | База для постерів (default `https://image.tmdb.org/t/p/w500`) |
| `Rawg:ApiKey` | `Rawg__ApiKey` | API-ключ RAWG. Без ключа пошук ігор пропускається |
| `Rawg:BaseUrl` | `Rawg__BaseUrl` | Веб-адреса RAWG API (default `https://api.rawg.io/api/`) |
| `ASPNETCORE_ENVIRONMENT` | — | `Development` → SQLite, `Production` → PostgreSQL |

Режим без ключів — штатний для розробки: пошук працює лише по локальній бібліотеці, помилки зовнішніх провайдерів логуються як попередження й не валять запит (`GET /api/search` завжди відповідає 200).

## Swagger

- UI: `http://localhost:5021/swagger` (або відповідний порт запуску)
- OpenAPI-документ: `/swagger/v1/swagger.json`
- Експортована копія специфікації для контрактних звірок: [`./swagger.json`](./swagger.json)

## Архітектура

```
MediaLibrary.Api/                 HTTP-шар і бізнес-логіка
  Endpoints/                      мінімальні ендпоінти, мапінг IResult, try/catch → { error, code }
  Services/                       MediaService (CRUD, сесії, імпорт, валідація),
                                  TmdbService/RawgService (HTTP-клієнти TMDB/RAWG),
                                  SearchAggregatorService (конвейер «БД → TMDB → RAWG»)
MediaLibrary.Core/                доменна модель (Entities) + AppDbContext:
                                  MediaItems, Genres, Tags, ViewingSessions
MediaLibrary.Migrations.Sqlite/   EF-міграції під SQLite (Development)
MediaLibrary.Migrations.Postgres/ EF-міграції під PostgreSQL (Production)
```

- **Middleware-конвеєр:** `ErrorHandlingMiddleware` йде першим після Swagger — підхоплює винятки без власного обробника й гарантує єдиний формат `{ error, code }`.
- **DI:** `MediaService`/`SearchAggregatorService` — scoped; `TmdbService`/`RawgService` — типізовані `HttpClient` (`AddHttpClient`).
- **Міграції:** міграції згенеровані окремо під кожен провайдер (в окремих збірках, щоб уникнути циклічних посилань і конфліктів наборів — через `MigrationsAssembly` у реєстрації `DbContext`); на старті застосунок виконує `Database.Migrate()`, тож схема цільової БД завжди відповідає моделі.
- **Модель даних:** `MediaItem` ↔ `Genre`/`Tag` — many-to-many (join-таблиці `MediaItemGenre`, `MediaItemTag`); `MediaItem` → `ViewingSession` — one-to-many із каскадним видаленням.
- **Дедуплікація пошуку:** за парою `ExternalSource + ExternalId` (без урахування регістру); локальний запис має пріоритет над зовнішнім близнюком.

## Ендпоінти

| Метод | Шлях | Призначення | Успіх |
|---|---|---|---|
| GET | `/api/media` | Список із фільтрами `type`, `status`, `genre`, `minRating`, `search` + пагінація `offset`/`limit` (≤ 100) | 200 `{ total, offset, limit, items }` |
| GET | `/api/media/{id}` | Деталі запису | 200 / 404 |
| POST | `/api/media` | Створення вручну (`isManualEntry: true`) | 201 / 400 |
| PUT | `/api/media/{id}` | Часткове оновлення (null-поля не зачіпаються) | 200 / 400 / 404 |
| DELETE | `/api/media/{id}` | Видалення (каскадом сесії) | 204 / 404 |
| GET | `/api/media/{id}/sessions` | Історія перегляду, новіші першими | 200 `{ sessions }` / 404 |
| POST | `/api/media/{id}/sessions` | Додати сесію (`date` опційна — default: поточний UTC) | 201 / 404 |
| GET | `/api/search?query=...&type=...` | Пошук у БД + TMDB + RAWG з дедуплікацією | 200 `{ results }` / 400 |
| POST | `/api/search/import` | Імпорт знайденого в бібліотеку | 201 / 400 / 409 |
| GET | `/api/stats` | Агрегати: `total`, `byStatus`, `byType`, `avgRating`, `topGenres` (≤ 5) | 200 |
| GET | `/api/genres` | Довідник жанрів, відсортований за `name` | 200 `{ genres }` |

### Приклади

Створити запис:

```bash
curl -X POST http://localhost:5000/api/media \
  -H "Content-Type: application/json" \
  -d '{"title":"Inception","type":"movie","year":2010,"genres":["Sci-Fi"],"rating":9,"status":"completed"}'
```

Відповідь `201 Created` (фрагмент):

```json
{
  "id": 1,
  "title": "Inception",
  "type": "movie",
  "status": "completed",
  "rating": 9,
  "genres": ["Sci-Fi"],
  "isManualEntry": true,
  "createdAt": "2026-11-15T10:24:31Z",
  "updatedAt": null
}
```

Пошук із фільтром типу:

```bash
curl "http://localhost:5000/api/search?query=inception&type=movie"
```

```json
{ "results": [ { "externalId": "27205", "externalSource": "TMDB", "title": "Inception", "year": 2010, "type": "movie", "posterUrl": "https://...", "overview": "..." } ] }
```

Імпорт дубліката:

```bash
curl -X POST http://localhost:5000/api/search/import -H "Content-Type: application/json" \
  -d '{"externalId":"27205","externalSource":"TMDB","title":"Inception","type":"movie"}'
```

```json
{ "error": "Media already exists", "code": "MEDIA_DUPLICATE" }
```

## Помилки

Формат тіла для всіх клієнтських і серверних помилок:

```json
{ "error": "<текст>", "code": "<код>" }
```

| HTTP | code | Коли |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Невалідні дані (порожній/відсутній `title`, `type` поза переліком, `rating` поза 1–10, `year` поза 1900–2100, пустий `query`…) |
| 404 | `MEDIA_NOT_FOUND` | Запис або медіа для сесій не знайдено |
| 409 | `MEDIA_DUPLICATE` | Дублікат за `externalId` + `externalSource` при імпорті (без урахування регістру джерела) |
| 500 | `INTERNAL_ERROR` | Непередбачена помилка (деталі — в логах, у відповідь не потрапляють) |

## Контрактні правила

- `type`: `movie` | `series` | `game`; `status`: `plan` | `watching` | `completed` | `dropped` — **завжди lowercase**, парсинг вхідних значень регістронезалежний.
- Дати — ISO 8601 UTC із суфіксом `Z` (`2026-11-15T10:24:31Z`).
- Пагінація: `offset` ≥ 0, `limit` за замовчуванням 20, максимум 100.
- Специфікація: [`../CONTRACT.md`](../CONTRACT.md); будь-яка зміна контракту — за згодою обох сторін.

## Деплой

Production-збірка використовує PostgreSQL; провайдер БД перемикається за `ASPNETCORE_ENVIRONMENT` (див. `Program.cs`):

- `Production` → Npgsql (`ConnectionStrings__Default` = PostgreSQL-рядок);
- будь-що інше → SQLite.

Корінь `api/` містить `Dockerfile` (multi-stage, SDK 9.0 → ASP.NET 9.0 runtime, порт 8080):

```bash
cd api
docker build -f Dockerfile -t medialibrary-api .
docker run -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ConnectionStrings__Default="Host=<host>;Database=medialibrary;Username=<user>;Password=<password>" \
  medialibrary-api
```

> Контекст збірки — каталог `api/` (Dockerfile збирає кілька проєктів: Api, Core, обидві збірки міграцій).

Для хостингу на Render або аналогічному PaaS: тип сервісу — Web Service (Docker), порт контейнера 8080, змінні середовища — `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__Default`, за потреби `Tmdb__ApiKey`/`Rawg__ApiKey`. Схему PostgreSQL контейнер застосовує сам на старті (`Database.Migrate()`, збірка `MediaLibrary.Migrations.Postgres`) — окремий крок розгортання БД не потрібен. За бажання схему можна оновити й вручну:

```bash
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__Default="Host=<host>;Database=medialibrary;Username=<user>;Password=<password>" \
dotnet ef database update --project MediaLibrary.Migrations.Postgres --startup-project MediaLibrary.Api
```
