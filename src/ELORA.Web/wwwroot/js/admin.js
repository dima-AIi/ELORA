/* ==========================================================================
   ELORA Admin — небольшие улучшения интерфейса.
   Подтверждение опасных действий + сохранение позиции при перезагрузке.
   ========================================================================== */
(function () {
    'use strict';

    // Формы с data-confirm спрашивают подтверждение перед отправкой.
    document.addEventListener('submit', function (event) {
        var form = event.target.closest('form[data-confirm]');
        if (!form) return;

        var message = form.getAttribute('data-confirm') || 'Подтвердите действие';
        if (!window.confirm(message)) {
            event.preventDefault();
        }
    });

    // Плавное раскрытие details без «дёрганья» страницы.
    document.querySelectorAll('.admin-details').forEach(function (details) {
        details.addEventListener('toggle', function () {
            if (!details.open) return;
            var summary = details.querySelector('.admin-details__summary');
            if (!summary) return;

            var rect = summary.getBoundingClientRect();
            if (rect.top < 0 || rect.bottom > window.innerHeight) {
                summary.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        });
    });
})();
