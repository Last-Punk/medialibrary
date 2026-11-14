from datetime import datetime, timezone
from aiogram import Router, types, F
from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton, CallbackQuery
from aiogram.fsm.state import State, StatesGroup
from aiogram.fsm.context import FSMContext
from api_client import MediaLibraryClient
from keyboards import get_main_keyboard, get_back_keyboard

router = Router()

class EditNote(StatesGroup):
    waiting_note = State()

class AddSession(StatesGroup):
    progress = State()
    note = State()

# --- 1. СПИСОК МЕДІАТЕКИ ---
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

    status_dict = {"plan": "у планах", "watching": "в процесі", "completed": "завершено", "dropped": "кинуто"}

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


# --- 2. КАРТКА МЕДІА ---
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


# --- 3. ЗМІНА СТАТУСУ ---
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
    
    # Виправляємо баг зависання через frozen callback
    fake_callback = callback.model_copy(update={"data": f"media_detail:{media_id}"})
    await show_media_detail(fake_callback, api)


# --- 4. ЗМІНА ОЦІНКИ ---
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
    
    # Виправляємо баг зависання через frozen callback
    fake_callback = callback.model_copy(update={"data": f"media_detail:{media_id}"})
    await show_media_detail(fake_callback, api)


# --- 5. НОТАТКИ ---
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


# --- 6. ВИДАЛЕННЯ ---
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


# --- 7. ІСТОРІЯ СЕСІЙ ---
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
    
    # Виправляємо баг відсутності валідації
    success = await api.add_session(media_id, now_iso, progress, note)
    await state.clear()

    kb = InlineKeyboardMarkup(inline_keyboard=[
        [InlineKeyboardButton(text="📖 До історії сесій", callback_data=f"sessions_list:{media_id}")]
    ])
    
    if success:
        await message.answer("✅ Сесію успішно додано!", reply_markup=kb)
    else:
        await message.answer("❌ Помилка: Не вдалося зберегти сесію. Можливо, сервер недоступний.", reply_markup=kb)