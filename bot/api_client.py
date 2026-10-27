import httpx
import asyncio
from pydantic import BaseModel

class MediaCreate(BaseModel):
    title: str
    author: str

# === API КЛІЄНТ ===
class MediaLibraryClient:
    def __init__(self, base_url: str = "http://localhost:5000"):
        self.base_url = base_url

    async def test_connection(self):
        """Асинхронна перевірка зв'язку з API"""
        async with httpx.AsyncClient() as client:
            try:
                response = await client.get(f"{self.base_url}/swagger/index.html")
                if response.status_code == 200:
                    return "Зв'язок із сервером встановлено!"
                return f"Помилка: {response.status_code}"
            except httpx.RequestError as e:
                return f"Сервер ще не працює або недоступний: {e}"

    async def create_media(self, media_data: MediaCreate):
        """Асинхронний POST-запит з використанням моделі Pydantic"""
        url = f"{self.base_url}/api/media"
        async with httpx.AsyncClient() as client:
            try:
                response = await client.post(url, json=media_data.model_dump())
                response.raise_for_status()
                return response.json()
            except httpx.HTTPStatusError as e:
                print(f"Помилка на сервері: {e}")
                return None
            except httpx.RequestError as e:
                print(f"Помилка з'єднання: {e}")
                return None

async def main():
    api = MediaLibraryClient()
    print(await api.test_connection())

    # Приклад, як створювати дані через Pydantic:
    # new_media = MediaCreate(title="Матриця", author="Вачовскі")
    # await api.create_media(new_media)

if __name__ == "__main__":
    asyncio.run(main())