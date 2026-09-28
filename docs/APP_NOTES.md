# ELORA — приложение: админка, клиент, право, картинки

Вынесено из `MEMORY.md`, чтобы тот оставался коротким.

## AI-консультант
`#faq` на главной (классы `.ask*`, внутри 4 вопроса из базы), отправка `wwwroot/js/assistant.js`, сервер `Services/Ai/` + `POST /api/chat`, промпт из БД, кэш 5 мин. Ключ — `ELORA.Web/.env` или переменные панели; цепочка моделей — навык `nvidia-nim-model-chain`. Ограничитель 12 запросов/мин на IP, лимит сообщения 800 символов.
Проверка вёрстки по снимку — `tools/check_ai_section.js`: у неё **свой** адрес (`ELORA_CHECK_URL`, по умолчанию `http://localhost:8099/_check/`), `ELORA_BASE` она не слушает. Если снимок не поднят, теперь говорит об этом сразу. Живой консультант проверяется через `tools/verify_live_site.js` (он задаёт вопрос и ждёт настоящий ответ).

## «Мои записи» — клиент без регистрации
`/my` + пункт в меню. Регистрации нет: при записи сервер выдаёт токен управления, `Helpers/MyBookings.cs` держит токены в cookie `elora_my_bookings` (HttpOnly, `SameSite=Lax`, год, максимум 20). «Забыть этот браузер» стирает куку, записи не отменяются. **Память привязана к браузеру, а не к человеку.**

## Админка
- Пароль боевой админки — только в панели хостинга; `SeedAdmin` пишет хеш один раз при пустой `AdminUsers`, логин по умолчанию `admin` (`ELORA_ADMIN_LOGIN`), на стенде — `elora`. Аварийный сброс — `ELORA_ADMIN_RESET_PASSWORD`. Блокировки за неудачные входы нет, только предупреждение в логе.
- `/admin/bookings`: «Открыть» → карточка `Pages/Admin/Booking.cshtml` (`@page "/admin/bookings/{id:int}"`) — данные клиента, ник ссылкой, кнопка на страницу клиента. Удалять только закрытые записи; `/admin/masters`, `/admin/catalog` — только когда нет записей. **У каждой разрушительной кнопки две независимые защиты**: скрыть там, где сервер откажет, и перепроверить в обработчике.
- `/admin/schedule`: закрытие даты предупреждает про активные записи (`TempData["FlashWarning"]` → `alert--info`). Флеши: `Flash`, `FlashError`, `FlashWarning`.
- `/admin/settings`: контакты, «Оператор данных» и ИНН (видны на `/privacy`), токены ботов, Chat ID, обе кнопки меню, «Сделать копию сейчас». Форма контактов перезаписывает все поля сразу. Чек-лист Telegram — 4 строки, включая **токен клиентского бота**.
- Дашборд (`Pages/Admin/Index.cshtml` + `TelegramService.BlockReason()`) предупреждает **только про канал администратора**: выключены уведомления / нет Chat ID / нет ни одного токена (админский токен не обязателен — карточку отправит клиентский бот). Про отсутствие клиентского бота дашборд молчит, хотя подтверждения клиентам тогда не уйдут — владелец узнаёт об этом от клиента. Предложено добавить отдельную плашку.
- **Mini App админа открывается на полный экран:** `wwwroot/js/telegram-app.js` (`ready`, `expand`, `requestFullscreen`, событие `fullscreenFailed`, цвета шапки и фона), CSS-блок `html.is-telegram` в `site.css` (`--tg-safe-area-inset-*`, `--tg-viewport-stable-height`), `viewport-fit=cover` в `_AdminLayout.cshtml` и `Admin/Login.cshtml`. Проверка — `tools/check_miniapp_fullscreen.js`: три сценария (страница входа внутри Telegram, обычный браузер, панель внутри Telegram с переносом cookie), считает отступы 71px / 63px + 74px и высоту 700px. Без пароля админки третий сценарий помечается ПРОПУСК, а не ошибкой.
- Вход из Mini App (`OnPostTelegramAsync`) проверяет подпись `initData` ключом админского бота и сверяет Telegram id с Chat ID администратора; данные живут сутки (`WebAppDataMaxAge`).

## Право, картинки, копии
`AUDIT_LEGAL_AND_SECURITY.md`, `IMAGE_SOURCES.md`: хостинг в Германии при сборе имени и телефона = нарушение локализации (ч. 5 ст. 18 152-ФЗ); владельцу — реальные контакты и реквизиты, переезд в Россию, Роскомнадзор, замена демо-отзывов. Копии — `DatabaseBackupService` (`VACUUM INTO`, `App_Data/backups/`, последние 14). Демо-контакты `+7 (999) 123-45-67` оставлены кликабельными по решению владельца.
**Фото мастеров свои с 25.09.2026** (`NEWfoto` → `wwwroot/images/masters/`, имена как в `Masters.PhotoPath`), нарезка под контейнер `1 / 1.1` — `tools/prepare_master_photos.py`, проверка — `tools/check_master_photos.js`, стоковые в `tools/_masters_backup_2026-09-25/`.

## Проверки приложения (стенд)
Стендовые базы: `tools/_test_db_53` (боевой контент + стендовый админ + фикстуры), `_test_db_54` (фото и награды очищены). Скрипты: `verify_legal_fixes.js`, `check_reviews_demo.js`, `check_miniapp_fullscreen.js`, `check_admin_settings.js`, `check_admin_edits.js`, `check_admin_delete.js`, `check_my_bookings.js`, `check_reported_bugs.js` (нужен `ELORA_STAND_LOG`), `check_initials_fallback.js`, `check_master_photos.js`.
Общее место для доступа: `tools/_admin_creds.js` — переменные `ELORA_ADMIN_LOGIN/PASSWORD`, иначе `tools/_stand_admin.txt` (**строка 1 — логин, строка 2 — пароль**) или пароль панели из `_ftp.env`. Печатается только источник, не значение.
