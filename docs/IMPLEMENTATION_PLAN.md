# ELORA — Порядок разработки

AI-агент должен выполнить работу полностью, а не остановиться после создания макета.

## Этап 1 — проект

1. Создать/проверить ASP.NET Core проект.
2. Настроить Visual Studio.
3. Настроить F5/Ctrl+F5.
4. Настроить `launchSettings.json`.
5. Проверить открытие сайта в браузере.

## Этап 2 — SQLite

1. Создать schema.
2. Создать connection factory.
3. Создать repositories/data access.
4. Добавить seed-данные ELORA.
5. Проверить чтение/запись.

## Этап 3 — Booking

1. Услуги.
2. Мастера.
3. Рабочие часы.
4. Календарь.
5. Свободные слоты.
6. Создание записи.
7. Backend validation.
8. SQLite transaction.
9. Защита double booking.
10. Success page.
11. Cancel / reschedule.

## Этап 4 — Админка

Cookie Authentication.

Разделы:

- Dashboard / записи;
- Services;
- Masters;
- Working Hours;
- Blocked Dates;
- Works;
- Reviews/FAQ при необходимости;
- Telegram settings.

## Этап 5 — Telegram

1. Bot.
2. Привязка chat_id.
3. Подтверждение.
4. Отмена.
5. Перенос.
6. Напоминание.

## Этап 6 — Дизайн

После того как основной сценарий работает:

1. Header.
2. Hero.
3. Services.
4. Works.
5. Pricing.
6. Booking steps.
7. Promo.
8. Reviews.
9. FAQ.
10. Footer.
11. Mobile.

При этом визуальная реализация должна сразу ориентироваться на четыре референса, а не на абстрактный beauty template.

## Этап 7 — QA

Проверить:

- build;
- F5;
- booking;
- double booking;
- cancel;
- reschedule;
- blocked dates;
- working hours;
- Telegram;
- mobile;
- desktop;
- FAQ;
- works filtering;
- admin;
- отсутствие ошибок в Console;
- отсутствие битых изображений;
- отсутствие горизонтального overflow.

## Этап 8 — Publish

1. Выполнить `dotnet publish`.
2. Проверить publish output.
3. Настроить production configuration.
4. Разместить на ASP.NET Core-compatible hosting.
5. Проверить публичный URL.
6. Проверить SQLite persistence.
7. Проверить Telegram.
8. Проверить HTTPS.

Не считать проект завершённым только потому, что он работает на localhost.
