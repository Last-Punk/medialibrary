# Media Library

Розподілена система ведення особистої медіатеки з інтеграцією зовнішніх каталогів.

## Компоненти

- **`api/`** — REST API на ASP.NET Core 8 (C#, .NET 8)
- **`bot/`** — Telegram-бот на Python (aiogram 3)

## Стек

- C# 12 / .NET 9, ASP.NET Core Minimal API, EF Core, SQLite (dev) / PostgreSQL (prod)
- Python 3.11+, aiogram 3, httpx, pydantic v2
- Зовнішні API: TMDB (фільми, серіали), RAWG (ігри)

## Команда

- **Мариніч В. В.** — backend (C#), керівник проєкту
- **Митровка А. Ю.** — frontend (Python bot)

## Документація

- [API Contract](./CONTRACT.md) — специфікація взаємодії між компонентами