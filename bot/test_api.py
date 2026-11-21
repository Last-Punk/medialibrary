import base64
import json
import time

import httpx
import pytest
from pydantic import ValidationError

from api_client import (
    ApiAuthError,
    ApiExternalUnavailableError,
    ApiRateLimitError,
    MediaCreate,
    MediaLibraryClient,
)

TEST_BOT_API_KEY = "test-bot-api-key-with-at-least-32-utf8-bytes"


def _make_jwt(sub: int, tid: int, exp_offset_seconds: int = 3600) -> str:
    """Формує валідний за структурою JWT для тестів (без перевірки підпису на клієнті)."""
    header = base64.urlsafe_b64encode(json.dumps({"alg": "HS256", "typ": "JWT"}).encode()).rstrip(b"=").decode()
    payload = base64.urlsafe_b64encode(
        json.dumps({"sub": str(sub), "tid": str(tid), "exp": int(time.time()) + exp_offset_seconds}).encode()
    ).rstrip(b"=").decode()
    return f"{header}.{payload}.signature"


def _patch_httpx_client(monkeypatch, handler):
    """Підміняє httpx.AsyncClient на тестовий MockTransport."""
    transport = httpx.MockTransport(handler)
    real_async_client = httpx.AsyncClient

    def mocked_client(*args, **kwargs):
        kwargs["transport"] = transport
        return real_async_client(*args, **kwargs)

    monkeypatch.setattr(httpx, "AsyncClient", mocked_client)


def _auth_ok_response(telegram_user_id: int, token: str) -> httpx.Response:
    return httpx.Response(
        200,
        json={
            "token": token,
            "user": {
                "id": telegram_user_id % 1000 + 1,
                "telegramId": telegram_user_id,
                "username": None,
                "firstName": None,
            },
        },
    )


def test_media_create_valid_and_defaults():
    """Перевіряємо створення валідної моделі та значення за замовчуванням."""
    media = MediaCreate(
        title="S.T.A.L.K.E.R. 2: Heart of Chornobyl",
        year=2024,
        type="game",
        genres=["Action", "RPG"],
        tags=["stalker"],
    )
    assert media.title == "S.T.A.L.K.E.R. 2: Heart of Chornobyl"
    assert media.year == 2024
    assert media.type == "game"
    assert media.genres == ["Action", "RPG"]
    assert media.tags == ["stalker"]
    assert media.status == "plan"
    assert media.rating is None


def test_media_create_missing_required_fields():
    """Перевіряємо помилку валідації при відсутності обов'язкових полів."""
    with pytest.raises(ValidationError):
        MediaCreate(type="movie")


@pytest.mark.asyncio
async def test_get_genres_authenticates_caches_jwt_and_never_sends_bot_key_to_api(monkeypatch):
    """Перший виклик отримує JWT, другий бере з кешу; X-Bot-Api-Key йде лише в /api/auth/bot."""
    expected = [{"id": 1, "name": "Action"}, {"id": 2, "name": "RPG"}]
    token = _make_jwt(sub=1, tid=73421)
    auth_calls = 0

    def handler(request: httpx.Request) -> httpx.Response:
        nonlocal auth_calls
        if request.url.path == "/api/auth/bot":
            auth_calls += 1
            assert request.method == "POST"
            assert request.headers.get("X-Bot-Api-Key") == TEST_BOT_API_KEY
            assert json.loads(request.content.decode("utf-8")) == {"telegramUserId": 73421}
            return _auth_ok_response(73421, token)

        assert request.method == "GET"
        assert request.url.path == "/api/genres"
        assert request.headers.get("Authorization") == f"Bearer {token}"
        assert "X-Bot-Api-Key" not in request.headers
        return httpx.Response(200, json={"genres": expected})

    _patch_httpx_client(monkeypatch, handler)
    base_client = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY)
    user_api = base_client.for_user(73421)

    assert await user_api.get_genres() == expected
    assert await user_api.get_genres() == expected
    assert auth_calls == 1


@pytest.mark.asyncio
async def test_tokens_are_isolated_per_telegram_user(monkeypatch):
    """Різні Telegram-користувачі отримують і використовують окремі JWT-токени."""
    tokens = {
        111: _make_jwt(sub=1, tid=111),
        222: _make_jwt(sub=2, tid=222),
    }
    seen_bearer_tokens = []

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/api/auth/bot":
            body = json.loads(request.content.decode("utf-8"))
            uid = body["telegramUserId"]
            return _auth_ok_response(uid, tokens[uid])

        assert request.url.path == "/api/tags"
        seen_bearer_tokens.append(request.headers.get("Authorization"))
        return httpx.Response(200, json={"tags": []})

    _patch_httpx_client(monkeypatch, handler)
    base_client = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY)

    await base_client.for_user(111).get_tags()
    await base_client.for_user(222).get_tags()

    assert seen_bearer_tokens == [f"Bearer {tokens[111]}", f"Bearer {tokens[222]}"]


@pytest.mark.asyncio
async def test_401_reauthenticates_and_retries_once(monkeypatch):
    """При 401 клієнт отримує новий JWT і повторює початковий запит рівно один раз."""
    old_token = _make_jwt(sub=1, tid=73421)
    new_token = _make_jwt(sub=1, tid=73421, exp_offset_seconds=7200)
    auth_calls = 0
    media_calls = 0

    def handler(request: httpx.Request) -> httpx.Response:
        nonlocal auth_calls, media_calls
        if request.url.path == "/api/auth/bot":
            auth_calls += 1
            return _auth_ok_response(73421, old_token if auth_calls == 1 else new_token)

        media_calls += 1
        if request.headers.get("Authorization") == f"Bearer {old_token}":
            return httpx.Response(401, json={"error": "Unauthorized", "code": "UNAUTHORIZED"})

        assert request.headers.get("Authorization") == f"Bearer {new_token}"
        return httpx.Response(200, json={"id": 42, "title": "Inception"})

    _patch_httpx_client(monkeypatch, handler)
    user_api = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY).for_user(73421)

    item = await user_api.get_media_by_id(42)
    assert item == {"id": 42, "title": "Inception"}
    assert auth_calls == 2
    assert media_calls == 2


@pytest.mark.asyncio
async def test_401_does_not_retry_infinitely(monkeypatch):
    """Якщо повторний запит знову повертає 401, клієнт піднімає ApiAuthError."""
    token = _make_jwt(sub=1, tid=73421)
    media_calls = 0

    def handler(request: httpx.Request) -> httpx.Response:
        nonlocal media_calls
        if request.url.path == "/api/auth/bot":
            return _auth_ok_response(73421, token)
        media_calls += 1
        return httpx.Response(401, json={"error": "Unauthorized", "code": "UNAUTHORIZED"})

    _patch_httpx_client(monkeypatch, handler)
    user_api = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY).for_user(73421)

    with pytest.raises(ApiAuthError):
        await user_api.get_stats()

    assert media_calls == 2


@pytest.mark.asyncio
async def test_rate_limit_and_external_unavailable_errors(monkeypatch):
    """Перевіряємо обробку 429 RATE_LIMITED та 503 EXTERNAL_API_UNAVAILABLE."""
    token = _make_jwt(sub=1, tid=73421)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/api/auth/bot":
            return _auth_ok_response(73421, token)
        if request.url.path == "/api/stats":
            return httpx.Response(429, json={"error": "Too many requests", "code": "RATE_LIMITED"})
        if request.url.path == "/api/search":
            return httpx.Response(
                503,
                json={"error": "TMDB or RAWG is unavailable", "code": "EXTERNAL_API_UNAVAILABLE"},
            )
        return httpx.Response(200, json={})

    _patch_httpx_client(monkeypatch, handler)
    user_api = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY).for_user(73421)

    with pytest.raises(ApiRateLimitError):
        await user_api.get_stats()

    with pytest.raises(ApiExternalUnavailableError):
        await user_api.search_external("Inception")


@pytest.mark.asyncio
async def test_get_media_by_id_not_found(monkeypatch):
    """Перевіряємо повернення None при статусі 404."""
    token = _make_jwt(sub=1, tid=73421)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/api/auth/bot":
            return _auth_ok_response(73421, token)
        assert request.url.path == "/api/media/999"
        return httpx.Response(404)

    _patch_httpx_client(monkeypatch, handler)
    user_api = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY).for_user(73421)
    assert await user_api.get_media_by_id(999) is None


@pytest.mark.asyncio
async def test_add_media_item_success(monkeypatch):
    """Перевіряємо створення медіа-запису та відсутність None у JSON payload."""
    token = _make_jwt(sub=1, tid=73421)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/api/auth/bot":
            return _auth_ok_response(73421, token)
        assert request.method == "POST"
        assert request.url.path == "/api/media"
        body = request.content.decode("utf-8")
        assert '"notes"' not in body
        return httpx.Response(201, json={"id": 10})

    _patch_httpx_client(monkeypatch, handler)
    user_api = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY).for_user(73421)
    payload = MediaCreate(title="Frostpunk", year=2018, type="game")
    assert await user_api.add_media_item(payload) is True


@pytest.mark.asyncio
async def test_import_media_duplicate_and_ok(monkeypatch):
    """Перевіряємо обробку 409 Conflict як 'duplicate' та 201 Created як 'ok'."""
    token = _make_jwt(sub=1, tid=73421)

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/api/auth/bot":
            return _auth_ok_response(73421, token)
        assert request.url.path == "/api/search/import"
        if b"DuplicateTitle" in request.content:
            return httpx.Response(409)
        return httpx.Response(201, json={"id": 5})

    _patch_httpx_client(monkeypatch, handler)
    user_api = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY).for_user(73421)

    assert await user_api.import_media({"title": "DuplicateTitle"}) == "duplicate"
    assert await user_api.import_media({"title": "NewTitle"}) == "ok"


@pytest.mark.asyncio
async def test_add_session_network_error(monkeypatch):
    """Перевіряємо коректне повернення False при помилці з'єднання."""
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("Connection refused", request=request)

    _patch_httpx_client(monkeypatch, handler)
    user_api = MediaLibraryClient("http://localhost:5021", bot_api_key=TEST_BOT_API_KEY).for_user(73421)
    assert await user_api.add_session(1, "2026-11-20T12:00:00Z", "S1", "Тест") is False