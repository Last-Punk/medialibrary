import asyncio
import os
import logging
from aiogram import Bot, Dispatcher
from dotenv import load_dotenv


from api_client import MediaLibraryClient
from handlers import router


load_dotenv()
TOKEN = os.getenv("BOT_TOKEN")

async def main():
    logging.basicConfig(level=logging.INFO)
    
    bot = Bot(token=TOKEN)
    dp = Dispatcher()
    

    api = MediaLibraryClient()
    dp["api"] = api


    dp.include_router(router)

    try:
        print("Бот запущений! Натисни Ctrl+C для зупинки.")
        await dp.start_polling(bot)
    finally:
        await bot.session.close()

if __name__ == "__main__":
    asyncio.run(main())