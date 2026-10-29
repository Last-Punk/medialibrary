import asyncio
import os
import logging
from aiogram import Bot, Dispatcher
from dotenv import load_dotenv

# Імпортуємо наш API клієнт та новий роутер
from api_client import MediaLibraryClient
from handlers import router

# Завантажуємо токен з .env
load_dotenv()
TOKEN = os.getenv("BOT_TOKEN")

async def main():
    logging.basicConfig(level=logging.INFO)
    
    bot = Bot(token=TOKEN)
    dp = Dispatcher()
    
    # Ініціалізуємо клієнт та передаємо його в диспетчер
    api = MediaLibraryClient()
    dp["api"] = api

    # ПІДКЛЮЧАЄМО НАШІ КОМАНДИ
    dp.include_router(router)

    try:
        print("Бот запущений! Натисни Ctrl+C для зупинки.")
        await dp.start_polling(bot)
    finally:
        await bot.session.close()

if __name__ == "__main__":
    asyncio.run(main())