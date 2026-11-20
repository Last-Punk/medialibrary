import httpx
import pytest
from pydantic import ValidationError

from api_client import MediaCreate, MediaLibraryClient


def _patch_httpx_client(monkeypatch, handler):
    """Підміняє httpx.AsyncClient на тестовий MockTransport."""
    transport = httpx.MockTransport(handler)
    real_async_client = httpx.AsyncClient

    def mocked_client(*args, **kwargs):
        kwargs["transport"] = transport
        return real_async_client(*args, **kwargs)

    monkeypatch.setattr(httpx, "AsyncClient", mocked_client)


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
async def test_get_genres_success(monkeypatch):
    """Перевіряємо отримання та парсинг списку жанрів."""
    expected = [{"id": 1, "name": "Action"}, {"id": 2, "name": "RPG"}]

    def handler(request: httpx.Request) -> httpx.Response:
        assert request.method == "GET"
        assert request.url.path == "/api/genres"
        return httpx.Response(200, json={"genres": expected})

    _patch_httpx_client(monkeypatch, handler)
    client = MediaLibraryClient("http://localhost:5021")
    result = await client.get_genres()
    assert result == expected


@pytest.mark.asyncio
async def test_get_media_by_id_not_found(monkeypatch):
    """Перевіряємо повернення None при статусі 404."""
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.path == "/api/media/999"
        return httpx.Response(404)

    _patch_httpx_client(monkeypatch, handler)
    client = MediaLibraryClient("http://localhost:5021")
    item = await client.get_media_by_id(999)
    assert item is None


@pytest.mark.asyncio
async def test_add_media_item_success(monkeypatch):
    """Перевіряємо створення медіа-запису та відсутність None у JSON payload."""
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.method == "POST"
        assert request.url.path == "/api/media"
        body = request.content.decode("utf-8")
        assert '"notes"' not in body
        return httpx.Response(201, json={"id": 10})

    _patch_httpx_client(monkeypatch, handler)
    client = MediaLibraryClient("http://localhost:5021")
    payload = MediaCreate(title="Frostpunk", year=2018, type="game")
    ok = await client.add_media_item(payload)
    assert ok is True


@pytest.mark.asyncio
async def test_import_media_duplicate_and_ok(monkeypatch):
    """Перевіряємо обробку 409 Conflict як 'duplicate' та 200/201 як 'ok'."""
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.path == "/api/search/import"
        if b"DuplicateTitle" in request.content:
            return httpx.Response(409)
        return httpx.Response(200)

    _patch_httpx_client(monkeypatch, handler)
    client = MediaLibraryClient("http://localhost:5021")

    assert await client.import_media({"title": "DuplicateTitle"}) == "duplicate"
    assert await client.import_media({"title": "NewTitle"}) == "ok"


@pytest.mark.asyncio
async def test_add_session_network_error(monkeypatch):
    """Перевіряємо коректне повернення False при помилці з'єднання."""
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("Connection refused", request=request)

    _patch_httpx_client(monkeypatch, handler)
    client = MediaLibraryClient("http://localhost:5021")
    ok = await client.add_session(1, "2026-11-20T12:00:00Z", "S1", "Тест")
    assert ok is False