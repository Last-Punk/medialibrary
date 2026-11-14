import asyncio

import logging
import os
from aiogram import Bot, Dispatcher
from dotenv import load_dotenv


from api_client import MediaLibraryClient



from menu import router as menu_router
from media import router as media_router
from create_search import router as create_search_router

load_dotenv()
BOT_TOKEN = os.getenv("BOT_TOKEN")

async def main():
    logging.basicConfig(level=logging.INFO)

    if not BOT_TOKEN:
        print(" Помилка: Токен бота не знайдено! Перевір файл .env")
        return

    bot = Bot(token=BOT_TOKEN)
    dp = Dispatcher()

    api = MediaLibraryClient("http://localhost:5021")

    # Реєструємо роутери
    dp.include_router(menu_router)
    dp.include_router(media_router)
    dp.include_router(create_search_router)

    print(" Бот успішно запущений і готовий до роботи!")

    await dp.start_polling(bot, api=api)

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n Бота зупинено (Ctrl+C).")