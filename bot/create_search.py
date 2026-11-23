from aiogram import F, Router, types
from aiogram.fsm.context import FSMContext
from aiogram.fsm.state import State, StatesGroup
from aiogram.types import CallbackQuery, InlineKeyboardButton, InlineKeyboardMarkup

from api_client import (
    ApiAuthError,
    ApiExternalUnavailableError,
    ApiRateLimitError,
    MediaCreate,
    MediaLibraryClient,
)
from keyboards import get_back_keyboard, get_main_keyboard

router = Router()


class AddMedia(StatesGroup):
    title = State()
    year = State()
    media_type = State()
    genres = State()
    tags = State()
    status = State()


class SearchMedia(StatesGroup):
    query = State()
    results = State()


# --- 1. ДОДАВАННЯ ВРУЧНУ ---
@router.callback_query(F.data == "add_media")
async def process_add_button(callback: CallbackQuery, state: FSMContext):
    await state.set_state(AddMedia.title)
    await callback.message.edit_text("Введи назву (фільму, серіалу чи гри):", reply_markup=get_back_keyboard())
    await callback.answer()


@router.message(AddMedia.title)
async def process_title(message: types.Message, state: FSMContext):
    await state.update_data(title=message.text)
    await message.delete()
    await message.answer(
        f"Назва: <b>{message.text}</b>\nТепер введи рік випуску (цифрами):",
        parse_mode="HTML",
        reply_markup=get_back_keyboard(),
    )
    await state.set_state(AddMedia.year)


@router.message(AddMedia.year)
async def process_year(message: types.Message, state: FSMContext):
    await message.delete()
    if not message.text.isdigit():
        await message.answer("Будь ласка, введи рік цифрами (наприклад, 2024):")
        return
    await state.update_data(year=int(message.text))
    type_kb = InlineKeyboardMarkup(inline_keyboard=[
        [
            InlineKeyboardButton(text="🎬 Фільм", callback_data="type:movie"),
            InlineKeyboardButton(text="📺 Серіал", callback_data="type:series"),
            InlineKeyboardButton(text="🎮 Гра", callback_data="type:game"),
        ],
        [InlineKeyboardButton(text="🔙 Скасувати", callback_data="back_to_main")],
    ])
    await message.answer("Обери тип медіа:", reply_markup=type_kb)
    await state.set_state(AddMedia.media_type)


@router.callback_query(AddMedia.media_type, F.data.startswith("type:"))
async def process_type(callback: CallbackQuery, state: FSMContext):
    media_type = callback.data.split(":")[1]
    await state.update_data(type=media_type)
    await callback.message.edit_text(
        "Введи жанри через кому (або напиши '-' якщо немає):",
        reply_markup=get_back_keyboard(),
    )
    await state.set_state(AddMedia.genres)
    await callback.answer()


@router.message(AddMedia.genres)
async def process_genres(message: types.Message, state: FSMContext):
    genres_input = message.text.strip()
    genres = [g.strip() for g in genres_input.split(",")] if genres_input != "-" else []
    await state.update_data(genres=genres)
    await message.delete()
    await message.answer(
        "Введи теги через кому (або напиши '-' якщо немає):",
        reply_markup=get_back_keyboard(),
    )
    await state.set_state(AddMedia.tags)


@router.message(AddMedia.tags)
async def process_tags(message: types.Message, state: FSMContext):
    tags_input = message.text.strip()
    tags = [t.strip() for t in tags_input.split(",")] if tags_input != "-" else []
    await state.update_data(tags=tags)
    await message.delete()

    status_kb = InlineKeyboardMarkup(inline_keyboard=[
        [
            InlineKeyboardButton(text="📋 У планах", callback_data="status:plan"),
            InlineKeyboardButton(text="▶️ В процесі", callback_data="status:watching"),
        ],
        [
            InlineKeyboardButton(text="✅ Завершено", callback_data="status:completed"),
            InlineKeyboardButton(text="❌ Кинуто", callback_data="status:dropped"),
        ],
    ])
    await message.answer("Обери початковий статус:", reply_markup=status_kb)
    await state.set_state(AddMedia.status)


@router.callback_query(AddMedia.status, F.data.startswith("status:"))
async def process_status_finish(callback: CallbackQuery, state: FSMContext, api: MediaLibraryClient):
    status_val = callback.data.split(":")[1]
    data = await state.get_data()

    new_media = MediaCreate(
        title=data["title"],
        year=data["year"],
        type=data["type"],
        genres=data.get("genres", []),
        tags=data.get("tags", []),
        status=status_val,
    )
    success = await api.add_media_item(new_media)
    await state.clear()

    if success:
        status_dict = {"plan": "У планах", "watching": "В процесі", "completed": "Завершено", "dropped": "Кинуто"}
        type_dict = {"movie": "Фільм", "series": "Серіал", "game": "Гра"}

        status_ua = status_dict.get(new_media.status, new_media.status)
        type_ua = type_dict.get(new_media.type, new_media.type)

        text = (
            "✅ <b>Новий запис успішно додано!</b>\n\n"
            f"Назва: {new_media.title}\n"
            f"Рік: {new_media.year}\n"
            f"Тип: {type_ua}\n"
            f"Статус: {status_ua}"
        )
    else:
        text = "❌ <b>Помилка:</b> Не вдалося зберегти запис."

    await callback.message.edit_text(text, parse_mode="HTML", reply_markup=get_main_keyboard())
    await callback.answer()


# --- 2. ПОШУК ТА ІМПОРТ ---
@router.callback_query(F.data == "search_start")
async def search_start(callback: CallbackQuery, state: FSMContext):
    await state.set_state(SearchMedia.query)
    await callback.message.edit_text(
        "🔍 <b>Введи назву фільму, серіалу чи гри для пошуку:</b>",
        parse_mode="HTML",
        reply_markup=get_back_keyboard(),
    )
    await callback.answer()


@router.message(SearchMedia.query)
async def search_execute(message: types.Message, state: FSMContext, api: MediaLibraryClient):
    query = message.text
    await message.delete()
    status_msg = await message.answer("Шукаю у базах даних... ⏳")

    try:
        results = await api.search_external(query=query)
    except ApiExternalUnavailableError:
        await status_msg.edit_text(
            "⚠️ Зовнішні каталоги (TMDB/RAWG) зараз недоступні.\nТи можеш додати запис вручну з головного меню.",
            reply_markup=get_back_keyboard(),
        )
        await state.clear()
        return
    except ApiRateLimitError:
        await status_msg.edit_text(
            "⏳ Забагато запитів. Спробуй повторити пошук за хвилину.",
            reply_markup=get_back_keyboard(),
        )
        await state.clear()
        return
    except ApiAuthError:
        await status_msg.edit_text(
            "❌ Помилка авторизації на сервері. Спробуй пізніше.",
            reply_markup=get_back_keyboard(),
        )
        await state.clear()
        return

    if not results:
        await status_msg.edit_text("Нічого не знайдено 😔", reply_markup=get_back_keyboard())
        await state.clear()
        return

    top_results = results[:5]
    await state.update_data(search_results=top_results)
    await state.set_state(SearchMedia.results)

    text = f"🔍 <b>Результати за запитом «{query}»:</b>\n<i>Натисни кнопку нижче, щоб імпортувати у свою медіатеку:</i>\n\n"
    kb_rows = []

    type_dict = {"movie": "Фільм", "series": "Серіал", "game": "Гра"}
    src_dict = {"Manual": "Вручну", "Local": "БД", "TMDB": "TMDB", "RAWG": "RAWG"}

    for idx, item in enumerate(top_results):
        title = item.get("title", "Без назви")
        year = item.get("year") or "—"

        raw_src = item.get("externalSource") or "Local"
        raw_type = item.get("type", "")

        src_ua = src_dict.get(raw_src, raw_src)
        type_ua = type_dict.get(raw_type, raw_type)

        text += f"{idx + 1}. <b>{title}</b> ({year}) [{type_ua} | {src_ua}]\n"
        kb_rows.append([InlineKeyboardButton(text=f"📥 Імпортувати: {title} ({year})", callback_data=f"import_item:{idx}")])

    kb_rows.append([InlineKeyboardButton(text="🔙 Назад до меню", callback_data="back_to_main")])
    await status_msg.edit_text(text, parse_mode="HTML", reply_markup=InlineKeyboardMarkup(inline_keyboard=kb_rows))


@router.callback_query(SearchMedia.results, F.data.startswith("import_item:"))
async def import_selected_item(callback: CallbackQuery, state: FSMContext, api: MediaLibraryClient):
    idx = int(callback.data.split(":")[1])
    data = await state.get_data()
    results = data.get("search_results", [])

    if idx >= len(results):
        await callback.answer("Помилка вибору", show_alert=True)
        return

    selected = results[idx]
    try:
        status = await api.import_media(selected)
    except ApiExternalUnavailableError:
        await state.clear()
        await callback.message.edit_text(
            "⚠️ Не вдалося підтягнути жанри із зовнішнього каталогу (TMDB/RAWG недоступний). Спробуй пізніше або додай запис вручну.",
            reply_markup=get_main_keyboard(),
        )
        await callback.answer()
        return

    await state.clear()

    if status == "ok":
        msg = f"✅ <b>«{selected.get('title')}»</b> успішно імпортовано до медіатеки!"
    elif status == "duplicate":
        msg = f"⚠️ <b>«{selected.get('title')}»</b> вже є у твоїй медіатеці (409 Conflict)."
    else:
        msg = "❌ Помилка під час імпорту."

    await callback.message.edit_text(msg, parse_mode="HTML", reply_markup=get_main_keyboard())
    await callback.answer()