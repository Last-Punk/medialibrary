# API Contract — Media Library

**Version:** 1.0<br>
**Date:** 2026-10-26<br>
**Parties:** V. V. Marynich (C# API) ↔ A. Yu. Mytrovka (Python bot)

> This is an English translation of the existing contract. Its API semantics are unchanged. Any future contract change requires approval from both parties and must be reflected in both components.

---

## General rules

- **Development base URL:** `http://localhost:5000`
- **Production base URL:** to be assigned after deployment
- **Format:** JSON (`Content-Type: application/json`)
- **Encoding:** UTF-8
- **Dates:** ISO 8601 UTC (for example, `2026-10-15T10:30:00Z`)
- **Pagination:** `?offset=0&limit=20` (`limit` defaults to `20` and has a maximum of `100`)
- **Filters:** supplied as query parameters

---

## Data types

| Field | Format | Example |
|---|---|---|
| `id` | integer | `42` |
| `year` | nullable integer | `2010` or `null` |
| `rating` | nullable integer, 1–10 | `9` or `null` |
| `type` | enum string | `"movie"` \| `"series"` \| `"game"` |
| `status` | enum string | `"plan"` \| `"watching"` \| `"completed"` \| `"dropped"` |
| `posterUrl` | nullable URL string | `"https://image.tmdb.org/..."` |
| `createdAt` | ISO 8601 timestamp | `"2026-10-15T10:30:00Z"` |
| `externalSource` | nullable string | `"TMDB"` \| `"RAWG"` \| `"Manual"` |

---

## Models

### MediaItem

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
  "notes": "Watched it again — a masterpiece",
  "isManualEntry": false,
  "genres": ["Sci-Fi", "Thriller"],
  "tags": ["favorite", "nolan"],
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

## Endpoints

### Media

#### `GET /api/media` — list library items

All query parameters are optional:

| Parameter | Type | Description |
|---|---|---|
| `type` | string | `movie` \| `series` \| `game` |
| `status` | string | `plan` \| `watching` \| `completed` \| `dropped` |
| `genre` | string | Genre name |
| `minRating` | integer | Minimum rating |
| `search` | string | Substring to match in a title |
| `offset` | integer | Number of items to skip; defaults to `0` |
| `limit` | integer | Page size; defaults to `20` |

**Response 200:**

```json
{
  "total": 87,
  "offset": 0,
  "limit": 20,
  "items": [ /* MediaItem array */ ]
}
```

#### `GET /api/media/{id}` — get item details

**Response 200:** `MediaItem`

**Response 404:**

```json
{ "error": "Media not found", "code": "MEDIA_NOT_FOUND" }
```

#### `POST /api/media` — create a manual item

**Request:**

```json
{
  "title": "My home movie",
  "year": 2024,
  "type": "movie",
  "genres": ["Drama"],
  "tags": ["original"],
  "rating": 8,
  "status": "completed",
  "notes": "Filmed with friends"
}
```

**Response 201:** `MediaItem` with its new `id`

**Response 400:**

```json
{ "error": "Title is required", "code": "VALIDATION_ERROR" }
```

#### `PUT /api/media/{id}` — update an item

All request fields are optional:

```json
{
  "title": "New title",
  "rating": 10,
  "status": "completed",
  "notes": "Updated note",
  "genres": ["Sci-Fi"],
  "tags": ["favorite"]
}
```

**Response 200:** updated `MediaItem`

**Response 404:** `MEDIA_NOT_FOUND`

#### `DELETE /api/media/{id}` — delete an item

**Response 204:** no response body

**Response 404:** `MEDIA_NOT_FOUND`

### Search

#### `GET /api/search` — search local data and catalogs

| Parameter | Type | Required | Description |
|---|---|---|---|
| `query` | string | yes | Search query |
| `type` | string | no | `movie` \| `series` \| `game`; if omitted, search all types |

Search order and behavior:

1. Search the **local database** (`Title.Contains(query)`).
2. Search **TMDB** when `type` is `movie`, `series`, or is omitted.
3. Search **RAWG** when `type` is `game` or is omitted.
4. Combine the results and remove duplicate `externalId` values.

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

**Response 200 when no results are found:**

```json
{ "results": [] }
```

#### `POST /api/search/import` — import a search result

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

Import behavior:

1. Check whether an item with the same `externalId` and `externalSource` already exists.
2. Return **409 Conflict** if it exists.
3. Otherwise create a `MediaItem` with `isManualEntry: false`.
4. Fetch genres from TMDB or RAWG using a separate catalog request.

**Response 201:** created `MediaItem`

**Response 409:**

```json
{ "error": "Media already exists", "code": "MEDIA_DUPLICATE" }
```

### Dictionaries

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

#### `GET /api/tags`

**Response 200:**

```json
{
  "tags": [
    { "id": 1, "name": "favorite" }
  ]
}
```

### Statistics

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

### Viewing history

#### `GET /api/media/{id}/sessions`

**Response 200:**

```json
{
  "sessions": [
    {
      "id": 1,
      "mediaItemId": 42,
      "date": "2026-10-15T20:00:00Z",
      "note": "Watched season 1",
      "progress": "S1"
    }
  ]
}
```

#### `POST /api/media/{id}/sessions`

**Request:**

```json
{
  "date": "2026-10-15T20:00:00Z",
  "note": "Watched season 2",
  "progress": "S2"
}
```

**Response 201:** created viewing session

---

## Error codes

| HTTP status | Code | Description |
|---|---|---|
| 400 | `VALIDATION_ERROR` | Invalid request data |
| 404 | `MEDIA_NOT_FOUND` | Media item not found |
| 409 | `MEDIA_DUPLICATE` | Duplicate `externalId` |
| 500 | `INTERNAL_ERROR` | Internal error |
| 503 | `EXTERNAL_API_UNAVAILABLE` | TMDB or RAWG is unavailable |

---

## Contract history

| Date | Version | Change |
|---|---|---|
| 2026-10-15 | 1.0 | Initial version |
