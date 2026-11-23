import asyncio
import logging
import os

from aiogram import Bot, Dispatcher
from aiogram.types import ErrorEvent
from dotenv import load_dotenv

from api_client import (
    ApiAuthError,
    ApiExternalUnavailableError,
    ApiRateLimitError,
    MediaLibraryClient,
)
from create_search import router as create_search_router
from media import router as media_router
from menu import router as menu_router
from middlewares import AuthMiddleware

load_dotenv()

BOT_TOKEN = os.getenv("BOT_TOKEN", "").strip()
API_BASE_URL = os.getenv("API_BASE_URL", "http://localhost:5021").strip()
BOT_API_KEY = os.getenv("BOT_API_KEY", "").strip()

logger = logging.getLogger(__name__)


async def _reply_user_error(event: ErrorEvent, text: str) -> None:
    """Акуратно показує користувачеві повідомлення про помилку API."""
    if event.update.callback_query:
        await event.update.callback_query.answer(text, show_alert=True)
    elif event.update.message:
        await event.update.message.answer(text)


async def main():
    logging.basicConfig(level=logging.INFO)

    if not BOT_TOKEN:
        logger.error("Токен BOT_TOKEN не знайдено! Перевір файл .env")
        return

    if not BOT_API_KEY:
        logger.error("Секрет BOT_API_KEY не знайдено! Додай його у файл .env (має збігатися з BotAuth:ApiKey на API).")
        return

    if len(BOT_API_KEY.encode("utf-8")) < 32:
        logger.warning("BOT_API_KEY коротший за 32 UTF-8 байти — API відхилить такий ключ.")

    if BOT_API_KEY == BOT_TOKEN:
        logger.error("BOT_API_KEY не може збігатися з BOT_TOKEN! Згенеруй окремий секрет.")
        return

    bot = Bot(token=BOT_TOKEN)
    dp = Dispatcher()

    api_client = MediaLibraryClient(base_url=API_BASE_URL, bot_api_key=BOT_API_KEY)
    auth_middleware = AuthMiddleware(api_client)
    dp.message.outer_middleware(auth_middleware)
    dp.callback_query.outer_middleware(auth_middleware)

    @dp.error()
    async def api_error_handler(event: ErrorEvent) -> bool:
        exc = event.exception
        if isinstance(exc, ApiRateLimitError):
            await _reply_user_error(event, "⏳ Забагато запитів. Будь ласка, спробуй ще раз за хвилину.")
            return True

        if isinstance(exc, ApiExternalUnavailableError):
            await _reply_user_error(
                event,
                "⚠️ Зовнішній каталог (TMDB/RAWG) зараз недоступний. Спробуй пізніше або додай запис вручну.",
            )
            return True

        if isinstance(exc, ApiAuthError):
            logger.error("Помилка авторизації API: status=%s code=%s", exc.status_code, exc.code)
            await _reply_user_error(event, "❌ Помилка авторизації на сервері. Спробуй пізніше.")
            return True

        return False

    dp.include_router(menu_router)
    dp.include_router(media_router)
    dp.include_router(create_search_router)

    logger.info("Бот успішно запущений (API: %s) і готовий до роботи!", API_BASE_URL)
    await dp.start_polling(bot)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nБота зупинено (Ctrl+C).")