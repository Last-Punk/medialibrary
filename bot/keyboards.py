from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton

def get_main_keyboard() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="🎬 Моя медіатека", callback_data="media_list:0:all")],
        [
            InlineKeyboardButton(text="🔍 Пошук медіа", callback_data="search_start"),
            InlineKeyboardButton(text="➕ Додати вручну", callback_data="add_media")
        ],
        [
            InlineKeyboardButton(text="📊 Статистика", callback_data="show_stats"),
            InlineKeyboardButton(text="🎭 Жанри й Теги", callback_data="show_dicts")
        ],
        [InlineKeyboardButton(text="⚙️ Статус сервера", callback_data="ping_server")]
    ])

def get_back_keyboard() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="🔙 Назад до меню", callback_data="back_to_main")]
    ])