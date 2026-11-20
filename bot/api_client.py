import httpx
from typing import List, Optional, Dict, Any
from pydantic import BaseModel

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


class MediaLibraryClient:
    """
    Асинхронний HTTP-клієнт для взаємодії з бекендом MediaLibrary.Api (C#).
    
    Attributes:
        base_url (str): Базова URL-адреса API сервера (наприклад, http://localhost:5021).
    """
    def __init__(self, base_url: str = "http://localhost:5021"):
        self.base_url = base_url

    async def test_connection(self) -> str:
        """
        Перевіряє доступність API сервера.
        
        Returns:
            str: Повідомлення про успішний статус або опис помилки.
        """
        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(f"{self.base_url}/swagger/index.html", timeout=5.0)
                if response.status_code == 200:
                    return "✅ Зв'язок із сервером встановлено!"
                return f"⚠️ Сервер відповів зі статусом: {response.status_code}"
            except Exception as e:
                return f"❌ Сервер недоступний: {e}"

    async def get_media_list(
        self,
        media_type: Optional[str] = None,
        status: Optional[str] = None,
        search: Optional[str] = None,
        offset: int = 0,
        limit: int = 5
    ) -> Dict[str, Any]:
        """GET /api/media — список медіатеки з фільтрами та пагінацією"""
        params: Dict[str, Any] = {"offset": offset, "limit": limit}
        if media_type:
            params["type"] = media_type
        if status:
            params["status"] = status
        if search:
            params["search"] = search

        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(f"{self.base_url}/api/media", params=params)
                response.raise_for_status()
                data = response.json()
                if isinstance(data, dict):
                    return data
                return {"total": len(data), "offset": offset, "limit": limit, "items": data}
            except Exception as e:
                print(f"Помилка GET /api/media: {e}")
                return {"total": 0, "offset": offset, "limit": limit, "items": []}

    async def get_media_by_id(self, media_id: int) -> Optional[Dict[str, Any]]:
        """
        Отримує детальну інформацію про один медіа-запис за його ідентифікатором.

        Args:
            media_id (int): Унікальний ID медіа-запису.

        Returns:
            Optional[Dict[str, Any]]: Словник із даними запису або None, якщо його не знайдено.
        """
        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(f"{self.base_url}/api/media/{media_id}")
                if response.status_code == 404:
                    return None
                response.raise_for_status()
                return response.json()
            except Exception as e:
                print(f"Помилка GET /api/media/{media_id}: {e}")
                return None

    async def add_media_item(self, media_data: MediaCreate) -> bool:
        """POST /api/media — створ вручну"""
        async with httpx.AsyncClient() as client:
            try:
                payload = media_data.model_dump(exclude_none=True)
                response = await client.post(f"{self.base_url}/api/media", json=payload)
                response.raise_for_status()
                return True
            except Exception as e:
                print(f"Помилка POST /api/media: {e}")
                return False

    async def update_media_item(self, media_id: int, update_data: Dict[str, Any]) -> bool:
        """PUT /api/media/{id} — оновити поля запису"""
        async with httpx.AsyncClient() as client:
            try:
                response = await client.put(f"{self.base_url}/api/media/{media_id}", json=update_data)
                response.raise_for_status()
                return True
            except Exception as e:
                print(f"Помилка PUT /api/media/{media_id}: {e}")
                return False

    async def delete_media_item(self, media_id: int) -> bool:
        """DELETE /api/media/{id} — видалити запис"""
        async with httpx.AsyncClient() as client:
            try:
                response = await client.delete(f"{self.base_url}/api/media/{media_id}")
                return response.status_code in (200, 204)
            except Exception as e:
                print(f"Помилка DELETE /api/media/{media_id}: {e}")
                return False

    async def search_external(self, query: str, media_type: Optional[str] = None) -> List[Dict[str, Any]]:
        """GET /api/search — пошук (БД + TMDB + RAWG)"""
        params: Dict[str, Any] = {"query": query}
        if media_type:
            params["type"] = media_type

        async with httpx.AsyncClient(timeout=15.0) as client:
            try:
                response = await client.get(f"{self.base_url}/api/search", params=params)
                response.raise_for_status()
                data = response.json()
                return data.get("results", []) if isinstance(data, dict) else []
            except Exception as e:
                print(f"Помилка GET /api/search: {e}")
                return []

    async def import_media(self, item_data: Dict[str, Any]) -> str:
        """POST /api/search/import — імпортувати знайдене"""
        async with httpx.AsyncClient(timeout=15.0) as client:
            try:
                response = await client.post(f"{self.base_url}/api/search/import", json=item_data)
                if response.status_code == 409:
                    return "duplicate"
                response.raise_for_status()
                return "ok"
            except Exception as e:
                print(f"Помилка POST /api/search/import: {e}")
                return "error"

    async def get_genres(self) -> List[Dict[str, Any]]:
        """GET /api/genres — список жанрів"""
        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(f"{self.base_url}/api/genres")
                response.raise_for_status()
                return response.json().get("genres", [])
            except Exception as e:
                print(f"Помилка GET /api/genres: {e}")
                return []

    async def get_tags(self) -> List[Dict[str, Any]]:
        """
        Отримує список усіх доступних тегів.
        
        Returns:
            List[Dict[str, Any]]: Список словників із даними тегів.
        """
        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(f"{self.base_url}/api/tags")
                response.raise_for_status()
                return response.json().get("tags", [])
            except Exception as e:
                print(f"Помилка GET /api/tags: {e}")
                return []

    async def get_stats(self) -> Optional[Dict[str, Any]]:
        """GET /api/stats — статистика медіатеки"""
        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(f"{self.base_url}/api/stats")
                response.raise_for_status()
                return response.json()
            except Exception as e:
                print(f"Помилка GET /api/stats: {e}")
                return None

    async def get_sessions(self, media_id: int) -> List[Dict[str, Any]]:
        """GET /api/media/{id}/sessions — список сесій"""
        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(f"{self.base_url}/api/media/{media_id}/sessions")
                response.raise_for_status()
                return response.json().get("sessions", [])
            except Exception as e:
                print(f"Помилка GET /api/media/{media_id}/sessions: {e}")
                return []

    async def add_session(self, media_id: int, date_iso: str, progress: str, note: str) -> bool:
        """POST /api/media/{id}/sessions — додати сесію перегляду"""
        payload = {
            "date": date_iso,
            "progress": progress,
            "note": note
        }
        async with httpx.AsyncClient() as client:
            try:
                response = await client.post(f"{self.base_url}/api/media/{media_id}/sessions", json=payload)
                response.raise_for_status()
                return True
            except Exception as e:
                print(f"Помилка POST /api/media/{media_id}/sessions: {e}")
                return False