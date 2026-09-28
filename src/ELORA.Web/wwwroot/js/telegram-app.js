// Панель управления, открытая из Telegram как Mini App.
//
// Зачем отдельный файл: панель работает и в обычном браузере, и внутри Telegram. Внутри
// ей нужен полный экран — иначе половину высоты занимает шапка Telegram, а на телефоне
// от панели остаётся полоска. Полный экран даёт метод `requestFullscreen()` (Bot API 8.0);
// на старых клиентах его нет, поэтому есть запасной `expand()` — развернуть на максимум
// доступной высоты.
//
// Признак «мы в Mini App» — непустой `initData`: Telegram передаёт его только когда
// страница открыта именно как мини-приложение. В обычном браузере (и в браузере внутри
// Telegram, открытом по ссылке) `initData` пустой, и трогать разметку нельзя.
(function () {
    'use strict';

    function color(name, fallback) {
        var value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        return value || fallback;
    }

    function setup(app) {
        if (!app || !app.initData) return;

        // Класс на <html>: отступы под статус-бар считаются от корня страницы, иначе фон
        // под врезками остаётся белым. Появляется он после загрузки скрипта Telegram —
        // до этого момента страница успевает отрисоваться, и это нормально: отступы
        // добавляются к уже нарисованному, ничего не прыгает.
        document.documentElement.classList.add('is-telegram');

        if (typeof app.ready === 'function') app.ready();
        if (typeof app.expand === 'function') app.expand();

        // В полноэкранном режиме шапка прозрачная, и её цвет Telegram использует, чтобы
        // подобрать контрастный цвет статус-бара. Берём цвета темы, а не константы:
        // палитра меняется в CSS, и дублировать её здесь нельзя.
        if (typeof app.setHeaderColor === 'function') app.setHeaderColor(color('--shell', '#F7EFE9'));
        if (typeof app.setBackgroundColor === 'function') app.setBackgroundColor(color('--page', '#EFE4DB'));

        if (typeof app.requestFullscreen !== 'function' || app.isFullscreen) return;

        // Полный экран умеет не каждый клиент: если не вышло — остаётся expand().
        // Причину пишем в консоль: молчаливый отказ выглядит как «скрипт не сработал».
        if (typeof app.onEvent === 'function') {
            app.onEvent('fullscreenFailed', function (event) {
                console.warn('ELORA: полноэкранный режим не включился —', event && event.error);
            });
        }

        try {
            app.requestFullscreen();
        } catch (error) {
            console.warn('ELORA: полноэкранный режим недоступен —', error);
        }
    }

    if (window.Telegram && window.Telegram.WebApp) {
        setup(window.Telegram.WebApp);
        return;
    }

    // Скрипт Telegram подключаем сами: панель — обычная страница, и если её открыли
    // не из Telegram, тянуть сторонний файл незачем.
    var script = document.createElement('script');
    script.src = 'https://telegram.org/js/telegram-web-app.js';
    script.onload = function () { setup(window.Telegram && window.Telegram.WebApp); };
    script.onerror = function () { /* открыто в браузере — полноэкранный режим не нужен */ };
    document.head.appendChild(script);
})();
