# ELORA — хостинг, выкладка, боевой сайт

Вынесено из `MEMORY.md`, чтобы тот оставался коротким. Здесь всё про боевой сайт.

## Адрес и доступы
`https://elora2026.runasp.net`, HTTPS с 24.09.2026 (Let's Encrypt `CN=*.runasp.net`, до 22.12.2026), перенаправление `http → https` включено 25.09.2026.
FTP `site93502.siteasp.net`, **логин `site93502` латиницей** — панель показывает кириллицей `сайт93502`, но FTP с ним отвечает `530 Authentication error`, латинский работает. Креды — `tools/_ftp.env` (`ELORA_FTP_HOST/USER/PASSWORD`), читаются через `tools/_ftp_creds.py`.
**Пароль FTP, пароль панели и пароль админки — не одно и то же.** Пароль панели входит на FTP, но в админку сайта не пускает (проверено 25.09.2026: форма отвечает «Неверный логин или пароль»). **Логин боевой админки — `elora`** (в `AdminUsers` одна запись; приложение по умолчанию сеет `admin`, поэтому проверки без явного логина стучатся не туда). Пароль админки хранится только у владельца, локально не сохраняется; проверить вход: `ELORA_ADMIN_PASSWORD=… node tools/check_admin_login.js https://elora2026.runasp.net`.
`Site:PublicUrl` — только для ссылок в Telegram и Mini App, в публикации должен быть https.

## Устройство хостинга
- Папка приложения — `/wwwroot` внутри FTP. Пул 32-битный → публикация `-r win-x86 --self-contained false`.
- `web.config`: `hostingModel="inprocess"`, `stdoutLogEnabled="false"`.
- **Логов приложения на хостинге нет и получить нельзя.** При `inprocess` stdout не пишется вовсе, а переключение на `outofprocess` + `stdoutLogEnabled="true"` (проверено 25.09.2026) даёт **502**: файлы `logs/stdout_*` создаются нулевыми, приложение не поднимается. Диагноз на боевом ставится не по логу, а по тому, что видно владельцу (карточка в админском боте) и по базе (`tools/diag_client_notify.py`, `tools/_diag_prod_db.py`).
- Папку `wwwroot/_check` и прочие локальные приманки в публикацию не кладём.

## Как выкладывать
1. `bash tools/publish_for_host.sh` — пишет `Site:PublicUrl`, проверяет, что не уехали `.env`, ключ `nvapi-`, база и непустые токены ботов. Папка для загрузки — `_upload_elora`.
2. `python tools/sync_ftp.py _upload_elora --dry` — прочитать список заливки и **удалений** (`KEEP = App_Data, logs, .well-known`, там `acme-challenge/web.config`).
3. `python tools/sync_ftp.py _upload_elora` — заливка. Креды берёт из `ELORA_FTP_*`; удобно `set -a; . tools/_ftp.env; set +a` (значение пароля не попадает в командную строку).
4. `python tools/verify_deploy.py` — качает `.dll`, `site.js`, `web.config` и сверяет `sha256`.

Грабли:
- **Размер файла как признак изменения обманывает.** Пересобранный `ELORA.Web.dll` того же размера (1 117 184 б) не уехал, а выкладка сказала «залить: 0». Теперь `sync_ftp.py` держит sha256-манифест `tools/_upload_state.json` (вне папки публикации, её чистит publish) и флаг `--force <файл>`.
- Занятую сборку скрипт обходит сам: перезалив `web.config` → пауза 30 с → до 6 попыток; в логе это «сборка занята работающим приложением — перезапускаю пул».
- После перезапуска пула сайт отвечает не сразу — пара секунд.
- `ftp.nlst()` отдаёт имена без пути, поэтому в `verify_deploy.py` нужен `mlsd`.
- **В Release `.cshtml` в публикацию не попадают** — они компилируются в `.dll`. Поэтому правка вида на боевом требует пересборки, а живое применение `.cshtml` (runtime compilation) работает только на стенде.

## Проверки боевого
`tools/verify_pages.js` (все страницы, три ширины, `overflow=0`, битые картинки), `tools/verify_live_site.js` (рисуется ли на самом деле: страницы, вопрос AI-консультанту, мобильная вёрстка; отчёт в `_live/verify_live.txt`), `tools/verify_legal_fixes.js`, `tools/check_master_photos.js`, `tools/check_miniapp_fullscreen.js`, `tools/check_admin_login.js`.
Все понимают `ELORA_BASE` (по умолчанию — боевой адрес). Прогон в консоль может ничего не печатать из-за буферизации Chrome — итог читать из файла отчёта.
**Разрушительные проверки** (`check_reviews_demo.js` удаляет отзывы через админку) на боевом не запускаются: есть защита по адресу, обход только `ELORA_ALLOW_LIVE=1`.
Шаги панели и разборы — `DEPLOY_FREE.md`, `MONSTERASP_STEPS.md`, `OWNER_CHECKLIST_STEPS.md`.
