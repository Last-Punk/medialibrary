from typing import Any, Awaitable, Callable, Dict

from aiogram import BaseMiddleware
from aiogram.types import TelegramObject

from api_client import MediaLibraryClient


class AuthMiddleware(BaseMiddleware):
    """
    Передає в обробники клієнт API, прив'язаний до автентичного Telegram user ID
    із поточного оновлення (message.from_user.id або callback_query.from_user.id).
    """

    def __init__(self, api_client: MediaLibraryClient):
        self._api_client = api_client

    async def __call__(
        self,
        handler: Callable[[TelegramObject, Dict[str, Any]], Awaitable[Any]],
        event: TelegramObject,
        data: Dict[str, Any],
    ) -> Any:
        user = getattr(event, "from_user", None)
        if user is None or user.is_bot or user.id <= 0:
            return None

        data["api"] = self._api_client.for_user(user.id)
        return await handler(event, data)