from aiogram import Router, types, F
from aiogram.filters import CommandStart
from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton, CallbackQuery
from api_client import MediaLibraryClient


router = Router()


def get_main_keyboard():
    """Головне меню"""
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="🎬 Моя медіатека", callback_data="show_media")],
        [InlineKeyboardButton(text="➕ Додати медіа", callback_data="add_media")],
        [InlineKeyboardButton(text="⚙️ Статус сервера", callback_data="ping_server")]
    ])

def get_back_keyboard():
    """Кнопка повернення до головного меню"""
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="🔙 Назад до меню", callback_data="back_to_main")]
    ])
    

@router.message(CommandStart())
async def cmd_start(message: types.Message):
    """Викликається лише один раз при введенні /start"""
    await message.delete() 
    
    await message.answer(
        "Привіт! Я бот медіабібліотеки.\nОбери дію з меню нижче:",
        reply_markup=get_main_keyboard()
    )

@router.callback_query(F.data == "back_to_main")
async def back_to_main(callback: CallbackQuery):
    """Обробка кнопки 'Назад'"""
    await callback.message.edit_text(
        "Головне меню. Що будемо робити?",
        reply_markup=get_main_keyboard()
    )
    await callback.answer()
    
@router.callback_query(F.data == "ping_server")
async def process_ping_button(callback: CallbackQuery, api: MediaLibraryClient):
    """Перевірка сервера через кнопку"""
    await callback.message.edit_text("Перевіряю зв'язок із сервером... ⏳")

    result = await api.test_connection()
    
    await callback.message.edit_text(
        f"<b>Статус сервера:</b>\n{result}",
        parse_mode="HTML",
        reply_markup=get_back_keyboard()
    )
    await callback.answer()

@router.callback_query(F.data == "show_media")
async def process_media_button(callback: CallbackQuery, api: MediaLibraryClient):
    """Показ медіатеки через кнопку"""
    await callback.message.edit_text("Завантажую твою медіатеку... ⏳")
    media_list = await api.get_media_list()
    
    if not media_list:
        text = "Список порожній або сервер наразі недоступний."
    else:
        text = "🎬 <b>Твоя медіатека:</b>\n\n"
        for item in media_list:
            year_str = f" ({item.year})" if item.year else ""
            text += f"• {item.title}{year_str} [{item.type}]\n"
            
    await callback.message.edit_text(
        text, 
        parse_mode="HTML",
        reply_markup=get_back_keyboard()
    )
    await callback.answer()
    
@router.callback_query(F.data == "add_media")
async def process_add_button(callback: CallbackQuery):
    """Заглушка для кнопки 'Додати медіа'"""
    await callback.message.edit_text(
        "Тут буде меню додавання нового фільму чи гри! 🛠",
        reply_markup=get_back_keyboard()
    )
    await callback.answer()