import asyncio
import base64
import json
import logging
import time
from dataclasses import dataclass
from typing import Any, Dict, List, Optional

import httpx
from pydantic import BaseModel

logger = logging.getLogger(__name__)


class ApiError(Exception):
    """Базова помилка взаємодії з MediaLibrary.Api."""

    def __init__(self, message: str, status_code: Optional[int] = None, code: Optional[str] = None):
        super().__init__(message)
        self.status_code = status_code
        self.code = code


class ApiAuthError(ApiError):
    """Помилка автентифікації бота або користувача (401 / 503 BOT_AUTH_UNAVAILABLE)."""


class ApiRateLimitError(ApiError):
    """Перевищено ліміт запитів до API (429 RATE_LIMITED)."""


class ApiExternalUnavailableError(ApiError):
    """Зовнішній каталог TMDB або RAWG недоступний (503 EXTERNAL_API_UNAVAILABLE)."""


class MediaCreate(BaseModel):
    """
    Модель для створення нового медіа-запису.
    Використовується при відправці POST-запитів на сервер.
    """

    title: str
    year: Optional[int] = None
    type: str
    genres: List[str] = []
    tags: List[str] = []
    rating: Optional[int] = None
    status: str = "plan"
    notes: Optional[str] = None


@dataclass(frozen=True)
class _CachedSession:
    """Кешований JWT-токен конкретного Telegram-користувача."""

    token: str
    expires_at: float
    user: Optional[Dict[str, Any]] = None


class MediaLibraryClient:
    """
    Асинхронний HTTP-клієнт для взаємодії з бекендом MediaLibrary.Api (C#).

    Виконує обмін (X-Bot-Api-Key + telegramUserId) -> user-scoped JWT через
    POST /api/auth/bot, кешує токени окремо для кожного користувача та додає
    заголовок Authorization: Bearer <token> до захищених запитів.
    """

    _DEFAULT_TOKEN_TTL_SECONDS = 23 * 3600
    _EXPIRY_LEEWAY_SECONDS = 60

    def __init__(self, base_url: str = "http://localhost:5021", bot_api_key: str = ""):
        self.base_url = base_url.rstrip("/")
        self._bot_api_key = bot_api_key
        self._token_cache: Dict[int, _CachedSession] = {}
        self._auth_locks: Dict[int, asyncio.Lock] = {}

    def for_user(self, telegram_user_id: int) -> "UserMediaLibraryClient":
        """Повертає обгортку клієнта, прив'язану до конкретного Telegram-користувача."""
        if telegram_user_id <= 0:
            raise ValueError("telegram_user_id має бути додатним числом.")
        return UserMediaLibraryClient(self, telegram_user_id)

    def get_cached_user(self, telegram_user_id: int) -> Optional[Dict[str, Any]]:
        """Повертає кешовані дані профілю користувача (id, telegramId, username, firstName)."""
        cached = self._token_cache.get(telegram_user_id)
        return cached.user if cached else None

    def invalidate_token(self, telegram_user_id: int) -> None:
        """Видаляє кешований JWT-токен для вказаного користувача."""
        self._token_cache.pop(telegram_user_id, None)

    @classmethod
    def _extract_jwt_expiration(cls, token: str) -> float:
        """Зчитує claim 'exp' із JWT payload без додаткових залежностей."""
        fallback = time.time() + cls._DEFAULT_TOKEN_TTL_SECONDS
        parts = token.split(".")
        if len(parts) != 3:
            return fallback

        try:
            payload_b64 = parts[1]
            padding = "=" * (-len(payload_b64) % 4)
            decoded = base64.urlsafe_b64decode(payload_b64 + padding)
            payload = json.loads(decoded.decode("utf-8"))
            exp = payload.get("exp")
            if isinstance(exp, (int, float)) and exp > 0:
                return float(exp)
        except Exception:
            return fallback

        return fallback

    @staticmethod
    def _parse_error_payload(response: httpx.Response) -> tuple[str, Optional[str]]:
        """Витягує поля error та code із JSON-відповіді API."""
        try:
            data = response.json()
            if isinstance(data, dict):
                return (
                    str(data.get("error") or response.reason_phrase or "API error"),
                    data.get("code"),
                )
        except Exception:
            pass
        return (response.reason_phrase or f"HTTP {response.status_code}", None)

    def _is_valid(self, cached: Optional[_CachedSession]) -> bool:
        return cached is not None and time.time() < (cached.expires_at - self._EXPIRY_LEEWAY_SECONDS)

    async def authenticate_user(
        self,
        telegram_user_id: int,
        *,
        force_refresh: bool = False,
        failed_token: Optional[str] = None,
    ) -> str:
        """
        Отримує JWT для Telegram-користувача з кешу або через POST /api/auth/bot.
        Секрет X-Bot-Api-Key передається лише в цей ендпоінт і ніколи не логується.
        """
        if telegram_user_id <= 0:
            raise ValueError("telegram_user_id має бути додатним числом.")

        if not force_refresh:
            cached = self._token_cache.get(telegram_user_id)
            if self._is_valid(cached):
                return cached.token  # type: ignore[union-attr]

        lock = self._auth_locks.setdefault(telegram_user_id, asyncio.Lock())
        async with lock:
            cached = self._token_cache.get(telegram_user_id)
            if self._is_valid(cached):
                # Якщо інший паралельний запит уже оновив токен, не робимо зайвий виклик /api/auth/bot.
                if not force_refresh or (failed_token is not None and cached.token != failed_token):
                    return cached.token  # type: ignore[union-attr]

            if not self._bot_api_key:
                raise ApiAuthError("Ключ BOT_API_KEY не налаштовано на боці бота.", code="BOT_AUTH_MISCONFIGURED")

            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.post(
                    f"{self.base_url}/api/auth/bot",
                    headers={"X-Bot-Api-Key": self._bot_api_key},
                    json={"telegramUserId": telegram_user_id},
                )

            if response.status_code == 429:
                msg, code = self._parse_error_payload(response)
                raise ApiRateLimitError(msg, status_code=429, code=code or "RATE_LIMITED")

            if response.status_code == 503:
                msg, code = self._parse_error_payload(response)
                logger.error("Помилка /api/auth/bot: 503 (%s)", code)
                raise ApiAuthError(msg, status_code=503, code=code or "BOT_AUTH_UNAVAILABLE")

            if response.status_code != 200:
                msg, code = self._parse_error_payload(response)
                logger.error("Помилка /api/auth/bot: HTTP %s (%s)", response.status_code, code)
                raise ApiAuthError(msg, status_code=response.status_code, code=code or "UNAUTHORIZED")

            data = response.json()
            token = data.get("token") if isinstance(data, dict) else None
            if not isinstance(token, str) or not token.strip():
                raise ApiAuthError("API повернув порожній JWT-токен.", status_code=500, code="INVALID_TOKEN_RESPONSE")

            user_info = data.get("user") if isinstance(data.get("user"), dict) else None
            expires_at = self._extract_jwt_expiration(token)
            self._token_cache[telegram_user_id] = _CachedSession(
                token=token,
                expires_at=expires_at,
                user=user_info,
            )
            return token

    async def _authorized_request(
        self,
        telegram_user_id: int,
        method: str,
        path: str,
        *,
        timeout: float = 10.0,
        params: Optional[Dict[str, Any]] = None,
        json_body: Optional[Dict[str, Any]] = None,
    ) -> httpx.Response:
        """
        Виконує запит до захищеного ендпоінта з заголовком Authorization: Bearer <token>.
        При 401 один раз реавторизує користувача та повторює запит.
        """
        token = await self.authenticate_user(telegram_user_id)

        for attempt in range(2):
            headers = {"Authorization": f"Bearer {token}"}
            async with httpx.AsyncClient(timeout=timeout) as client:
                response = await client.request(
                    method,
                    f"{self.base_url}{path}",
                    headers=headers,
                    params=params,
                    json=json_body,
                )

            if response.status_code == 401 and attempt == 0:
                token = await self.authenticate_user(
                    telegram_user_id,
                    force_refresh=True,
                    failed_token=token,
                )
                continue

            if response.status_code == 401:
                self.invalidate_token(telegram_user_id)
                msg, code = self._parse_error_payload(response)
                raise ApiAuthError(msg, status_code=401, code=code or "UNAUTHORIZED")

            if response.status_code == 429:
                msg, code = self._parse_error_payload(response)
                raise ApiRateLimitError(msg, status_code=429, code=code or "RATE_LIMITED")

            if response.status_code == 503:
                msg, code = self._parse_error_payload(response)
                if code == "EXTERNAL_API_UNAVAILABLE":
                    raise ApiExternalUnavailableError(msg, status_code=503, code=code)

            return response

        raise ApiAuthError("Не вдалося виконати авторизований запит.", status_code=401, code="UNAUTHORIZED")

    async def test_connection(self, telegram_user_id: int) -> str:
        """Перевіряє доступність API сервера та працездатність бот-авторизації."""
        try:
            await self.authenticate_user(telegram_user_id, force_refresh=True)
            return "✅ Зв'язок із сервером та авторизація працюють!"
        except ApiRateLimitError:
            return "⏳ Сервер тимчасово обмежив кількість запитів (429). Спробуй за хвилину."
        except ApiAuthError as e:
            if e.code == "BOT_AUTH_UNAVAILABLE":
                return "⚠️ Сервер доступний, але на API не налаштовано BotAuth:ApiKey (503)."
            return f"⚠️ Сервер доступний, але авторизація бота не пройшла ({e.code or e.status_code})."
        except httpx.HTTPError as e:
            return f"❌ Сервер недоступний: {e}"

    async def get_media_list(
        self,
        telegram_user_id: int,
        media_type: Optional[str] = None,
        status: Optional[str] = None,
        search: Optional[str] = None,
        offset: int = 0,
        limit: int = 5,
    ) -> Dict[str, Any]:
        """GET /api/media — список медіатеки користувача з фільтрами та пагінацією."""
        params: Dict[str, Any] = {"offset": offset, "limit": limit}
        if media_type:
            params["type"] = media_type
        if status:
            params["status"] = status
        if search:
            params["search"] = search

        try:
            response = await self._authorized_request(telegram_user_id, "GET", "/api/media", params=params)
            response.raise_for_status()
            data = response.json()
            if isinstance(data, dict):
                return data
            return {"total": len(data), "offset": offset, "limit": limit, "items": data}
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка GET /api/media: %s", e)
            return {"total": 0, "offset": offset, "limit": limit, "items": []}

    async def get_media_by_id(self, telegram_user_id: int, media_id: int) -> Optional[Dict[str, Any]]:
        """GET /api/media/{id} — деталі одного медіа-запису користувача."""
        try:
            response = await self._authorized_request(telegram_user_id, "GET", f"/api/media/{media_id}")
            if response.status_code == 404:
                return None
            response.raise_for_status()
            return response.json()
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка GET /api/media/%s: %s", media_id, e)
            return None

    async def add_media_item(self, telegram_user_id: int, media_data: MediaCreate) -> bool:
        """POST /api/media — створити запис вручну."""
        try:
            payload = media_data.model_dump(exclude_none=True)
            response = await self._authorized_request(telegram_user_id, "POST", "/api/media", json_body=payload)
            response.raise_for_status()
            return True
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка POST /api/media: %s", e)
            return False

    async def update_media_item(
        self,
        telegram_user_id: int,
        media_id: int,
        update_data: Dict[str, Any],
    ) -> bool:
        """PUT /api/media/{id} — оновити поля запису."""
        try:
            response = await self._authorized_request(
                telegram_user_id,
                "PUT",
                f"/api/media/{media_id}",
                json_body=update_data,
            )
            response.raise_for_status()
            return True
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка PUT /api/media/%s: %s", media_id, e)
            return False

    async def delete_media_item(self, telegram_user_id: int, media_id: int) -> bool:
        """DELETE /api/media/{id} — видалити запис."""
        try:
            response = await self._authorized_request(telegram_user_id, "DELETE", f"/api/media/{media_id}")
            return response.status_code in (200, 204)
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка DELETE /api/media/%s: %s", media_id, e)
            return False

    async def search_external(
        self,
        telegram_user_id: int,
        query: str,
        media_type: Optional[str] = None,
    ) -> List[Dict[str, Any]]:
        """GET /api/search — пошук у локальній БД користувача + TMDB + RAWG."""
        params: Dict[str, Any] = {"query": query}
        if media_type:
            params["type"] = media_type

        try:
            response = await self._authorized_request(
                telegram_user_id,
                "GET",
                "/api/search",
                timeout=15.0,
                params=params,
            )
            response.raise_for_status()
            data = response.json()
            return data.get("results", []) if isinstance(data, dict) else []
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка GET /api/search: %s", e)
            return []

    async def import_media(self, telegram_user_id: int, item_data: Dict[str, Any]) -> str:
        """POST /api/search/import — імпортувати знайдений запис у медіатеку користувача."""
        try:
            response = await self._authorized_request(
                telegram_user_id,
                "POST",
                "/api/search/import",
                timeout=15.0,
                json_body=item_data,
            )
            if response.status_code == 409:
                return "duplicate"
            response.raise_for_status()
            return "ok"
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка POST /api/search/import: %s", e)
            return "error"

    async def get_genres(self, telegram_user_id: int) -> List[Dict[str, Any]]:
        """GET /api/genres — список жанрів користувача."""
        try:
            response = await self._authorized_request(telegram_user_id, "GET", "/api/genres")
            response.raise_for_status()
            return response.json().get("genres", [])
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка GET /api/genres: %s", e)
            return []

    async def get_tags(self, telegram_user_id: int) -> List[Dict[str, Any]]:
        """GET /api/tags — список тегів користувача."""
        try:
            response = await self._authorized_request(telegram_user_id, "GET", "/api/tags")
            response.raise_for_status()
            return response.json().get("tags", [])
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка GET /api/tags: %s", e)
            return []

    async def get_stats(self, telegram_user_id: int) -> Optional[Dict[str, Any]]:
        """GET /api/stats — статистика медіатеки користувача."""
        try:
            response = await self._authorized_request(telegram_user_id, "GET", "/api/stats")
            response.raise_for_status()
            return response.json()
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка GET /api/stats: %s", e)
            return None

    async def get_sessions(self, telegram_user_id: int, media_id: int) -> List[Dict[str, Any]]:
        """GET /api/media/{id}/sessions — список сесій перегляду."""
        try:
            response = await self._authorized_request(
                telegram_user_id,
                "GET",
                f"/api/media/{media_id}/sessions",
            )
            response.raise_for_status()
            return response.json().get("sessions", [])
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка GET /api/media/%s/sessions: %s", media_id, e)
            return []

    async def add_session(
        self,
        telegram_user_id: int,
        media_id: int,
        date_iso: str,
        progress: str,
        note: str,
    ) -> bool:
        """POST /api/media/{id}/sessions — додати сесію перегляду."""
        payload = {
            "date": date_iso,
            "progress": progress,
            "note": note,
        }
        try:
            response = await self._authorized_request(
                telegram_user_id,
                "POST",
                f"/api/media/{media_id}/sessions",
                json_body=payload,
            )
            response.raise_for_status()
            return True
        except (ApiAuthError, ApiRateLimitError, ApiExternalUnavailableError):
            raise
        except Exception as e:
            logger.warning("Помилка POST /api/media/%s/sessions: %s", media_id, e)
            return False


class UserMediaLibraryClient:
    """
    Легка обгортка над MediaLibraryClient, прив'язана до telegram_user_id
    із поточного Telegram-апдейту. Передається в хендлери як `api`.
    """

    def __init__(self, client: MediaLibraryClient, telegram_user_id: int):
        self._client = client
        self.telegram_user_id = telegram_user_id

    async def test_connection(self) -> str:
        return await self._client.test_connection(self.telegram_user_id)

    async def get_media_list(
        self,
        media_type: Optional[str] = None,
        status: Optional[str] = None,
        search: Optional[str] = None,
        offset: int = 0,
        limit: int = 5,
    ) -> Dict[str, Any]:
        return await self._client.get_media_list(
            self.telegram_user_id,
            media_type=media_type,
            status=status,
            search=search,
            offset=offset,
            limit=limit,
        )

    async def get_media_by_id(self, media_id: int) -> Optional[Dict[str, Any]]:
        return await self._client.get_media_by_id(self.telegram_user_id, media_id)

    async def add_media_item(self, media_data: MediaCreate) -> bool:
        return await self._client.add_media_item(self.telegram_user_id, media_data)

    async def update_media_item(self, media_id: int, update_data: Dict[str, Any]) -> bool:
        return await self._client.update_media_item(self.telegram_user_id, media_id, update_data)

    async def delete_media_item(self, media_id: int) -> bool:
        return await self._client.delete_media_item(self.telegram_user_id, media_id)

    async def search_external(self, query: str, media_type: Optional[str] = None) -> List[Dict[str, Any]]:
        return await self._client.search_external(self.telegram_user_id, query=query, media_type=media_type)

    async def import_media(self, item_data: Dict[str, Any]) -> str:
        return await self._client.import_media(self.telegram_user_id, item_data)

    async def get_genres(self) -> List[Dict[str, Any]]:
        return await self._client.get_genres(self.telegram_user_id)

    async def get_tags(self) -> List[Dict[str, Any]]:
        return await self._client.get_tags(self.telegram_user_id)

    async def get_stats(self) -> Optional[Dict[str, Any]]:
        return await self._client.get_stats(self.telegram_user_id)

    async def get_sessions(self, media_id: int) -> List[Dict[str, Any]]:
        return await self._client.get_sessions(self.telegram_user_id, media_id)

    async def add_session(self, media_id: int, date_iso: str, progress: str, note: str) -> bool:
        return await self._client.add_session(self.telegram_user_id, media_id, date_iso, progress, note)