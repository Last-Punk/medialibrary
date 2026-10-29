from aiogram import Router, types
from aiogram.filters import CommandStart, Command
from api_client import MediaLibraryClient

# Створюємо роутер — це збирач наших команд
router = Router()

@router.message(CommandStart())
async def cmd_start(message: types.Message):
    """Обробка команди /start"""
    await message.answer(
        "Привіт! Я бот медіабібліотеки.\n"
        "Напиши /ping для перевірки сервера колеги."
    )

@router.message(Command("ping"))
async def cmd_ping(message: types.Message, api: MediaLibraryClient):
    """Обробка команди /ping з доступом до клієнта"""
    await message.answer("Перевіряю зв'язок із сервером...")
    
    # Звертаємося до сервера
    result = await api.test_connection()
    
    await message.answer(result)

@router.message(Command("media"))
async def cmd_media(message: types.Message, api: MediaLibraryClient):
    """Обробка команди /media"""
    # Повідомляємо користувача, що процес пішов
    await message.answer("Завантажую твою медіатеку...")
    
    # Викликаємо наш новий метод
    media_list = await api.get_media_list()
    
    if not media_list:
        await message.answer("Список порожній або сервер наразі недоступний.")
        return
        
    # Формуємо красивий текст відповіді
    text = "🎬 <b>Твоя медіатека:</b>\n\n"
    for item in media_list:
        year_str = f" ({item.year})" if item.year else ""
        text += f"• {item.title}{year_str} [{item.type}]\n"
        
    # Відправляємо результат (parse_mode="HTML" дозволяє використовувати теги <b>)
    await message.answer(text, parse_mode="HTML")