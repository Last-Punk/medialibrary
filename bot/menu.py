from aiogram import Router, types, F
from aiogram.filters import CommandStart
from aiogram.types import CallbackQuery
from aiogram.fsm.context import FSMContext
from api_client import MediaLibraryClient
from keyboards import get_main_keyboard, get_back_keyboard

router = Router()

@router.message(CommandStart())
async def cmd_start(message: types.Message, state: FSMContext):
    await state.clear()
    try:
        await message.delete()
    except Exception:
        pass
    await message.answer(
        "🎬 <b>Вітаю у Media Tracker!</b>\nОбери потрібний розділ меню:",
        parse_mode="HTML",
        reply_markup=get_main_keyboard()
    )

@router.callback_query(F.data == "back_to_main")
async def back_to_main(callback: CallbackQuery, state: FSMContext):
    await state.clear()
    await callback.message.edit_text(
        "🎬 <b>Головне меню.</b> Що будемо робити?",
        parse_mode="HTML",
        reply_markup=get_main_keyboard()
    )
    await callback.answer()

@router.callback_query(F.data == "show_stats")
async def process_stats(callback: CallbackQuery, api: MediaLibraryClient):
    stats = await api.get_stats()
    if not stats:
        await callback.message.edit_text("❌ Не вдалося завантажити статистику.", reply_markup=get_back_keyboard())
        await callback.answer()
        return

    by_status = "\n".join([f"  • {s.get('status')}: {s.get('count')}" for s in stats.get("byStatus", [])]) or "  —"
    by_type = "\n".join([f"  • {t.get('type')}: {t.get('count')}" for t in stats.get("byType", [])]) or "  —"
    top_genres = ", ".join([f"{g.get('name')} ({g.get('count')})" for g in stats.get("topGenres", [])]) or "—"

    text = (
        "📊 <b>Статистика медіатеки:</b>\n\n"
        f"<b>Всього записів:</b> {stats.get('total', 0)}\n"
        f"<b>Середня оцінка:</b> ⭐ {stats.get('avgRating') or 0}\n\n"
        f"<b>За статусами:</b>\n{by_status}\n\n"
        f"<b>За типами:</b>\n{by_type}\n\n"
        f"<b>Топ жанрів:</b> {top_genres}"
    )
    await callback.message.edit_text(text, parse_mode="HTML", reply_markup=get_back_keyboard())
    await callback.answer()

@router.callback_query(F.data == "show_dicts")
async def process_dicts(callback: CallbackQuery, api: MediaLibraryClient):
    """
    Обробляє натискання кнопки 'Довідники'.
    Завантажує жанри та теги з бекенду і формує текстовий звіт.
    """
    genres = await api.get_genres()
    tags = await api.get_tags()

    genres_str = ", ".join([g.get("name", "") for g in genres]) or "Порожньо"
    tags_str = ", ".join([f"#{t.get('name', '')}" for t in tags]) or "Порожньо"

    text = (
        "🎭 <b>Довідники бази даних:</b>\n\n"
        f"<b>Жанри:</b>\n{genres_str}\n\n"
        f"<b>Теги:</b>\n{tags_str}"
    )
    await callback.message.edit_text(text, parse_mode="HTML", reply_markup=get_back_keyboard())
    await callback.answer()

@router.callback_query(F.data == "ping_server")
async def process_ping_button(callback: CallbackQuery, api: MediaLibraryClient):
    """
    Обробляє натискання кнопки 'Статус сервера'.
    Викликає тестовий ендпоінт API та виводить результат користувачеві.
    """
    result = await api.test_connection()
    await callback.message.edit_text(f"<b>Статус сервера:</b>\n{result}", parse_mode="HTML", reply_markup=get_back_keyboard())
    await callback.answer()