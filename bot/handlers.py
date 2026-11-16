from datetime import datetime, timezone
from aiogram import Router, types, F
from aiogram.filters import CommandStart
from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton, CallbackQuery
from aiogram.fsm.state import State, StatesGroup
from aiogram.fsm.context import FSMContext
from api_client import MediaLibraryClient, MediaCreate

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

class EditNote(StatesGroup):
    waiting_note = State()

class AddSession(StatesGroup):
    progress = State()
    note = State()


def get_main_keyboard() -> InlineKeyboardMarkup:
    return InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="🎬 Моя медіатека", callback_data="media_list:0:all")],
        [
            InlineKeyboardButton(text="🔍 Пошук", callback_data="search_start"),
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


@router.callback_query(F.data.startswith("media_list:"))
async def show_media_list(callback: CallbackQuery, api: MediaLibraryClient):
    _, offset_str, filter_type = callback.data.split(":")
    offset = int(offset_str)
    limit = 5
    media_type = None if filter_type == "all" else filter_type

    data = await api.get_media_list(media_type=media_type, offset=offset, limit=limit)
    items = data.get("items", [])
    total = data.get("total", 0)

    kb_rows = []

    kb_rows.append([
        InlineKeyboardButton(text="Усі" + (" •" if filter_type == "all" else ""), callback_data="media_list:0:all"),
        InlineKeyboardButton(text="🎬 Фільми" + (" •" if filter_type == "movie" else ""), callback_data="media_list:0:movie"),
        InlineKeyboardButton(text="📺 Серіали" + (" •" if filter_type == "series" else ""), callback_data="media_list:0:series"),
        InlineKeyboardButton(text="🎮 Ігри" + (" •" if filter_type == "game" else ""), callback_data="media_list:0:game")
    ])
    
    status_dict = {
        "plan": "у планах",
        "watching": "в процесі",
        "completed": "завершено",
        "dropped": "кинуто"
    }

    if not items:
        text = "📂 <b>У цій категорії поки немає записів.</b>"
    else:
        text = f"🎬 <b>Твоя медіатека (Всього: {total}):</b>\n<i>Натисни на запис для деталей та редагування:</i>"
        for item in items:
            year_s = f" ({item.get('year')})" if item.get("year") else ""
            
            raw_status = item.get("status", "plan")
            status_ua = status_dict.get(raw_status, raw_status)
            
            btn_text = f"📌 {item.get('title')}{year_s} [{status_ua}]"
            kb_rows.append([InlineKeyboardButton(text=btn_text, callback_data=f"media_detail:{item.get('id')}")])

    nav_row = []
    if offset >= limit:
        nav_row.append(InlineKeyboardButton(text="⬅️ Попередня", callback_data=f"media_list:{offset - limit}:{filter_type}"))
    if offset + limit < total:
        nav_row.append(InlineKeyboardButton(text="Наступна ➡️", callback_data=f"media_list:{offset + limit}:{filter_type}"))
    if nav_row:
        kb_rows.append(nav_row)

    kb_rows.append([InlineKeyboardButton(text="🔙 Назад до меню", callback_data="back_to_main")])

    await callback.message.edit_text(text, parse_mode="HTML", reply_markup=InlineKeyboardMarkup(inline_keyboard=kb_rows))
    await callback.answer()


@router.callback_query(F.data.startswith("media_detail:"))
async def show_media_detail(callback: CallbackQuery, api: MediaLibraryClient):
    media_id = int(callback.data.split(":")[1])
    item = await api.get_media_by_id(media_id)

    if not item:
        await callback.message.edit_text("❌ Запис не знайдено.", reply_markup=get_back_keyboard())
        await callback.answer()
        return

    genres = ", ".join(item.get("genres") or []) or "—"
    tags = ", ".join(item.get("tags") or []) or "—"
    rating = f"⭐ {item.get('rating')}/10" if item.get("rating") else "Не оцінено"
    notes = item.get("notes") or "Немає нотаток"

    status_dict = {"plan": "У планах", "watching": "В процесі", "completed": "Завершено", "dropped": "Кинуто"}
    type_dict = {"movie": "Фільм", "series": "Серіал", "game": "Гра"}
    
    status_ua = status_dict.get(item.get('status'), item.get('status'))
    type_ua = type_dict.get(item.get('type'), item.get('type'))
    
    text = (
        f"📌 <b>{item.get('title')}</b> ({item.get('year') or '—'})\n\n"
        f"<b>Тип:</b> {type_ua}\n"
        f"<b>Статус:</b> {status_ua}\n"
        f"<b>Оцінка:</b> {rating}\n"
        f"<b>Жанри:</b> {genres}\n"
        f"<b>Теги:</b> {tags}\n"
        f"<b>Нотатки:</b> {notes}"
    )

    kb = InlineKeyboardMarkup(inline_keyboard=[
        [
            InlineKeyboardButton(text="🔄 Статус", callback_data=f"edit_status_menu:{media_id}"),
            InlineKeyboardButton(text="⭐ Оцінити", callback_data=f"edit_rating_menu:{media_id}")
        ],
        [
            InlineKeyboardButton(text="📝 Нотатка", callback_data=f"edit_note:{media_id}"),
            InlineKeyboardButton(text="📖 Сесії перегляду", callback_data=f"sessions_list:{media_id}")
        ],
        [InlineKeyboardButton(text="🗑 Видалити запис", callback_data=f"delete_media:{media_id}")],
        [InlineKeyboardButton(text="🔙 До списку медіа", callback_data="media_list:0:all")]
    ])

    await callback.message.edit_text(text, parse_mode="HTML", reply_markup=kb)
    await callback.answer()


@router.callback_query(F.data.startswith("edit_status_menu:"))
async def edit_status_menu(callback: CallbackQuery):
    media_id = int(callback.data.split(":")[1])
    kb = InlineKeyboardMarkup(inline_keyboard=[
        [
            InlineKeyboardButton(text="📋 У планах", callback_data=f"set_status:{media_id}:plan"),
            InlineKeyboardButton(text="▶️ В процесі", callback_data=f"set_status:{media_id}:watching")
        ],
        [
            InlineKeyboardButton(text="✅ Завершено", callback_data=f"set_status:{media_id}:completed"),
            InlineKeyboardButton(text="❌ Кинуто", callback_data=f"set_status:{media_id}:dropped")
        ],
        [InlineKeyboardButton(text="🔙 Назад до картки", callback_data=f"media_detail:{media_id}")]
    ])
    await callback.message.edit_text("Обери новий статус:", reply_markup=kb)
    await callback.answer()

@router.callback_query(F.data.startswith("set_status:"))
async def set_status_action(callback: CallbackQuery, api: MediaLibraryClient):
    _, media_id_s, new_status = callback.data.split(":")
    media_id = int(media_id_s)
    await api.update_media_item(media_id, {"status": new_status})
    callback.data = f"media_detail:{media_id}"
    await show_media_detail(callback, api)

@router.callback_query(F.data.startswith("edit_rating_menu:"))
async def edit_rating_menu(callback: CallbackQuery):
    media_id = int(callback.data.split(":")[1])
    row1 = [InlineKeyboardButton(text=str(i), callback_data=f"set_rating:{media_id}:{i}") for i in range(1, 6)]
    row2 = [InlineKeyboardButton(text=str(i), callback_data=f"set_rating:{media_id}:{i}") for i in range(6, 11)]
    kb = InlineKeyboardMarkup(inline_keyboard=[
        row1, row2,
        [InlineKeyboardButton(text="🔙 Назад до картки", callback_data=f"media_detail:{media_id}")]
    ])
    await callback.message.edit_text("Обери оцінку від 1 до 10:", reply_markup=kb)
    await callback.answer()

@router.callback_query(F.data.startswith("set_rating:"))
async def set_rating_action(callback: CallbackQuery, api: MediaLibraryClient):
    _, media_id_s, rating_s = callback.data.split(":")
    media_id = int(media_id_s)
    await api.update_media_item(media_id, {"rating": int(rating_s)})
    callback.data = f"media_detail:{media_id}"
    await show_media_detail(callback, api)

@router.callback_query(F.data.startswith("edit_note:"))
async def edit_note_start(callback: CallbackQuery, state: FSMContext):
    media_id = int(callback.data.split(":")[1])
    await state.update_data(media_id=media_id)
    await state.set_state(EditNote.waiting_note)
    kb = InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="🔙 Скасувати", callback_data=f"media_detail:{media_id}")]
    ])
    await callback.message.edit_text("📝 Введи новий текст нотатки:", reply_markup=kb)
    await callback.answer()

@router.message(EditNote.waiting_note)
async def edit_note_finish(message: types.Message, state: FSMContext, api: MediaLibraryClient):
    data = await state.get_data()
    media_id = data["media_id"]
    await message.delete()
    await api.update_media_item(media_id, {"notes": message.text})
    await state.clear()
    kb = InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="📌 Повернутися до картки", callback_data=f"media_detail:{media_id}")]
    ])
    await message.answer("✅ Нотатку оновлено!", reply_markup=kb)

@router.callback_query(F.data.startswith("delete_media:"))
async def delete_media_action(callback: CallbackQuery, api: MediaLibraryClient):
    media_id = int(callback.data.split(":")[1])
    success = await api.delete_media_item(media_id)
    text = "🗑 <b>Запис успішно видалено!</b>" if success else "❌ Не вдалося видалити запис."
    kb = InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="🎬 До медіатеки", callback_data="media_list:0:all")],
        [InlineKeyboardButton(text="🔙 Головне меню", callback_data="back_to_main")]
    ])
    await callback.message.edit_text(text, parse_mode="HTML", reply_markup=kb)
    await callback.answer()


@router.callback_query(F.data.startswith("sessions_list:"))
async def show_sessions(callback: CallbackQuery, api: MediaLibraryClient):
    media_id = int(callback.data.split(":")[1])
    sessions = await api.get_sessions(media_id)

    if not sessions:
        text = "📖 <b>Історія перегляду порожня.</b>"
    else:
        text = "📖 <b>Історія перегляду (Сесії):</b>\n\n"
        for s in sessions:
            date_short = str(s.get("date", ""))[:10]
            text += f"• <b>[{s.get('progress')}]</b> ({date_short}) — {s.get('note')}\n"

    kb = InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="➕ Додати сесію", callback_data=f"session_add:{media_id}")],
        [InlineKeyboardButton(text="🔙 Назад до картки", callback_data=f"media_detail:{media_id}")]
    ])
    await callback.message.edit_text(text, parse_mode="HTML", reply_markup=kb)
    await callback.answer()

@router.callback_query(F.data.startswith("session_add:"))
async def session_add_start(callback: CallbackQuery, state: FSMContext):
    media_id = int(callback.data.split(":")[1])
    await state.update_data(media_id=media_id)
    await state.set_state(AddSession.progress)
    kb = InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="🔙 Скасувати", callback_data=f"sessions_list:{media_id}")]
    ])
    await callback.message.edit_text("Вкажи прогрес (наприклад: <code>S1</code> або <code>Серія 5</code>):", parse_mode="HTML", reply_markup=kb)
    await callback.answer()

@router.message(AddSession.progress)
async def session_add_progress(message: types.Message, state: FSMContext):
    await state.update_data(progress=message.text)
    await message.delete()
    await state.set_state(AddSession.note)
    await message.answer("Тепер введи короткий коментар (наприклад: <i>Переглянув 1 сезон</i>):", parse_mode="HTML")

@router.message(AddSession.note)
async def session_add_finish(message: types.Message, state: FSMContext, api: MediaLibraryClient):
    data = await state.get_data()
    media_id = data["media_id"]
    progress = data["progress"]
    note = message.text
    await message.delete()

    now_iso = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    await api.add_session(media_id, now_iso, progress, note)
    await state.clear()

    kb = InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="📖 До історії сесій", callback_data=f"sessions_list:{media_id}")]
    ])
    await message.answer("✅ Сесію додано!", reply_markup=kb)


@router.callback_query(F.data == "search_start")
async def search_start(callback: CallbackQuery, state: FSMContext):
    await state.set_state(SearchMedia.query)
    await callback.message.edit_text(
        "🔍 <b>Введи назву фільму, серіалу чи гри для пошуку :</b>",
        parse_mode="HTML",
        reply_markup=get_back_keyboard()
    )
    await callback.answer()

@router.message(SearchMedia.query)
async def search_execute(message: types.Message, state: FSMContext, api: MediaLibraryClient):
    query = message.text
    await message.delete()
    status_msg = await message.answer("Шукаю у базах даних... ⏳")

    results = await api.search_external(query=query)
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
    status = await api.import_media(selected)
    await state.clear()

    if status == "ok":
        msg = f"✅ <b>«{selected.get('title')}»</b> успішно імпортовано до медіатеки!"
    elif status == "duplicate":
        msg = f"⚠️ <b>«{selected.get('title')}»</b> вже є у твоїй медіатеці (409 Conflict)."
    else:
        msg = "❌ Помилка під час імпорту."

    await callback.message.edit_text(msg, parse_mode="HTML", reply_markup=get_main_keyboard())
    await callback.answer()

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
        reply_markup=get_back_keyboard()
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
            InlineKeyboardButton(text="🎮 Гра", callback_data="type:game")
        ],
        [InlineKeyboardButton(text="🔙 Скасувати", callback_data="back_to_main")]
    ])
    await message.answer("Обери тип медіа:", reply_markup=type_kb)
    await state.set_state(AddMedia.media_type)

@router.callback_query(AddMedia.media_type, F.data.startswith("type:"))
async def process_type(callback: CallbackQuery, state: FSMContext):
    media_type = callback.data.split(":")[1]
    await state.update_data(type=media_type)
    await callback.message.edit_text(
        "Введи жанри через кому (або напиши '-' якщо немає):", 
        reply_markup=get_back_keyboard()
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
        reply_markup=get_back_keyboard()
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
            InlineKeyboardButton(text="▶️ В процесі", callback_data="status:watching")
        ],
        [
            InlineKeyboardButton(text="✅ Завершено", callback_data="status:completed"),
            InlineKeyboardButton(text="❌ Кинуто", callback_data="status:dropped")
        ]
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
        status=status_val
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
    result = await api.test_connection()
    await callback.message.edit_text(f"<b>Статус сервера:</b>\n{result}", parse_mode="HTML", reply_markup=get_back_keyboard())
    await callback.answer()