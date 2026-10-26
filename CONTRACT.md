# API Contract — Media Library

**Версія:** 1.0  
**Дата:** 2026-10-26  
**Сторони:** Мариніч В. В. (C# API) ↔ Митровка А. Ю. (Python bot)

> ⚠️ Будь-яка зміна цього файлу — **тільки за згодою обох сторін**.  
> Зміна контракту = зміни в обох компонентах.

---

## Загальні правила

- **Base URL (dev):** `http://localhost:5000`
- **Base URL (prod):** визначається після деплою
- **Формат:** JSON (`Content-Type: application/json`)
- **Кодування:** UTF-8
- **Дати:** ISO 8601 UTC (`2026-10-15T10:30:00Z`)
- **Пагінація:** `?offset=0&limit=20` (default `limit=20`, max `100`)
- **Фільтри:** передаються як query-параметри

---

## Типи даних

| Тип | Формат | Приклад |
|---|---|---|
| `id` | int | `42` |
| `year` | int? (nullable) | `2010` або `null` |
| `rating` | int? (1–10, nullable) | `9` або `null` |
| `type` | enum string | `"movie"` \| `"series"` \| `"game"` |
| `status` | enum string | `"plan"` \| `"watching"` \| `"completed"` \| `"dropped"` |
| `posterUrl` | string? (URL) | `"https://image.tmdb.org/..."` |
| `createdAt` | ISO 8601 | `"2026-10-15T10:30:00Z"` |
| `externalSource` | string? | `"TMDB"` \| `"RAWG"` \| `"Manual"` |

---

## Моделі

### MediaItem (повна)

```json
{
  "id": 42,
  "title": "Inception",
  "originalTitle": "Inception",
  "year": 2010,
  "type": "movie",
  "posterUrl": "https://image.tmdb.org/t/p/w500/...",
  "overview": "A thief who steals corporate secrets...",
  "externalId": "27205",
  "externalSource": "TMDB",
  "rating": 9,
  "status": "completed",
  "notes": "Переглянув вдруге — шедевр",
  "isManualEntry": false,
  "genres": ["Sci-Fi", "Thriller"],
  "tags": ["улюблене", "нуolan"],
  "createdAt": "2026-10-15T10:30:00Z",
  "updatedAt": "2026-10-15T11:00:00Z"
}
```

### SearchResult

```json
{
  "externalId": "27205",
  "externalSource": "TMDB",
  "title": "Inception",
  "year": 2010,
  "type": "movie",
  "posterUrl": "https://image.tmdb.org/t/p/w500/...",
  "overview": "A thief who steals corporate secrets..."
}
```

### Error

```json
{
  "error": "Media not found",
  "code": "MEDIA_NOT_FOUND"
}
```

---

## Ендпоінти

### 📺 Медіа

---

#### `GET /api/media` — список медіатеки

**Query-параметри (усі опційні):**

| Параметр | Тип | Опис |
|---|---|---|
| `type` | string | `movie` \| `series` \| `game` |
| `status` | string | `plan` \| `watching` \| `completed` \| `dropped` |
| `genre` | string | Назва жанру |
| `minRating` | int | Мінімальна оцінка |
| `search` | string | Підрядок у назві |
| `offset` | int | Зсув (default 0) |
| `limit` | int | Розмір сторінки (default 20) |

**Response 200:**

```json
{
  "total": 87,
  "offset": 0,
  "limit": 20,
  "items": [ /* масив MediaItem */ ]
}
```

---

#### `GET /api/media/{id}` — деталі

**Response 200:** `MediaItem`

**Response 404:**

```json
{ "error": "Media not found", "code": "MEDIA_NOT_FOUND" }
```

---

#### `POST /api/media` — створити вручну

**Request:**

```json
{
  "title": "Мій домашній фільм",
  "year": 2024,
  "type": "movie",
  "genres": ["Drama"],
  "tags": ["власне"],
  "rating": 8,
  "status": "completed",
  "notes": "Зняв з друзями"
}
```

**Response 201:** `MediaItem` (з новим `id`)

**Response 400:**

```json
{ "error": "Title is required", "code": "VALIDATION_ERROR" }
```

---

#### `PUT /api/media/{id}` — оновити

**Request** (усі поля опційні):

```json
{
  "title": "Нова назва",
  "rating": 10,
  "status": "completed",
  "notes": "Оновлена нотатка",
  "genres": ["Sci-Fi"],
  "tags": ["улюблене"]
}
```

**Response 200:** оновлений `MediaItem`

**Response 404:** `MEDIA_NOT_FOUND`

---

#### `DELETE /api/media/{id}` — видалити

**Response 204:** без тіла

**Response 404:** `MEDIA_NOT_FOUND`

---

### 🔍 Пошук

---

#### `GET /api/search` — пошук (БД + TMDB + RAWG)

**Query-параметри:**

| Параметр | Тип | Обов'язковий | Опис |
|---|---|---|---|
| `query` | string | ✅ | Пошуковий запит |
| `type` | string | ❌ | `movie` \| `series` \| `game` (якщо не вказано — усі) |

**Логіка:**

1. Спочатку шукає в **локальній БД** (`Title.Contains(query)`).
2. Потім — у **TMDB** (якщо `type` = `movie`/`series`/не вказано).
3. Потім — у **RAWG** (якщо `type` = `game`/не вказано).
4. Об'єднує результати, видаляє дублікати за `externalId`.

**Response 200:**

```json
{
  "results": [
    {
      "externalId": "27205",
      "externalSource": "TMDB",
      "title": "Inception",
      "year": 2010,
      "type": "movie",
      "posterUrl": "https://...",
      "overview": "..."
    }
  ]
}
```

**Response 200 (нічого не знайдено):**

```json
{ "results": [] }
```

---

#### `POST /api/search/import` — імпортувати знайдене

**Request:**

```json
{
  "externalId": "27205",
  "externalSource": "TMDB",
  "title": "Inception",
  "year": 2010,
  "type": "movie",
  "posterUrl": "https://...",
  "overview": "..."
}
```

**Логіка:**

1. Перевіряє, чи вже немає запису з таким `externalId` + `externalSource`.
2. Якщо є — повертає **409 Conflict**.
3. Якщо немає — створює `MediaItem` з `isManualEntry: false`.
4. **Підтягує жанри** з TMDB/RAWG (окремий запит до джерела).

**Response 201:** створений `MediaItem`

**Response 409:**

```json
{ "error": "Media already exists", "code": "MEDIA_DUPLICATE" }
```

---

### 🎭 Довідники

---

#### `GET /api/genres`

**Response 200:**

```json
{
  "genres": [
    { "id": 1, "name": "Sci-Fi" },
    { "id": 2, "name": "Thriller" }
  ]
}
```

---

#### `GET /api/tags`

**Response 200:**

```json
{
  "tags": [
    { "id": 1, "name": "улюблене" }
  ]
}
```

---

### 📊 Статистика

---

#### `GET /api/stats`

**Response 200:**

```json
{
  "total": 87,
  "byStatus": [
    { "status": "completed", "count": 60 },
    { "status": "watching", "count": 15 }
  ],
  "byType": [
    { "type": "movie", "count": 50 },
    { "type": "game", "count": 20 },
    { "type": "series", "count": 17 }
  ],
  "avgRating": 7.8,
  "topGenres": [
    { "name": "Sci-Fi", "count": 25 }
  ]
}
```

---

### 📖 Історія перегляду

---

#### `GET /api/media/{id}/sessions`

**Response 200:**

```json
{
  "sessions": [
    {
      "id": 1,
      "mediaItemId": 42,
      "date": "2026-10-15T20:00:00Z",
      "note": "Переглянув 1 сезон",
      "progress": "S1"
    }
  ]
}
```

---

#### `POST /api/media/{id}/sessions`

**Request:**

```json
{
  "date": "2026-10-15T20:00:00Z",
  "note": "Переглянув 2 сезон",
  "progress": "S2"
}
```

**Response 201:** створена сесія

---

## Коди помилок

| Код | Значення | Коли |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Невалідні дані |
| 404 | `MEDIA_NOT_FOUND` | Запис не знайдено |
| 409 | `MEDIA_DUPLICATE` | Дублікат `externalId` |
| 500 | `INTERNAL_ERROR` | Внутрішня помилка |
| 503 | `EXTERNAL_API_UNAVAILABLE` | TMDB/RAWG не відповідає |

---

## Зміни контракту

| Дата | Версія | Що змінилось |
|---|---|---|
| 2026-10-15 | 1.0 | Початкова версія |