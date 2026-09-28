/* ==========================================================================
   ELORA — сценарий онлайн-записи.
   Шаги: Услуга → Мастер → Дата → Время → Имя → Телефон → Telegram → Подтверждение.
   Свободное время всегда запрашивается у сервера, а не считается в браузере.
   ========================================================================== */
(function () {
    'use strict';

    var root = document.querySelector('.booking-layout');
    if (!root) return;

    var catalog = [];
    try {
        catalog = JSON.parse(document.getElementById('catalogData').textContent || '[]');
    } catch (e) {
        catalog = [];
    }

    var config = window.ELORA_BOOKING || {};

    var state = {
        step: 1,
        maxStep: 1,
        service: null,
        master: null,
        date: null,
        time: null,
        name: '',
        phone: '',
        telegram: '',
        comment: ''
    };

    var el = {
        steps: document.getElementById('bookingSteps'),
        error: document.getElementById('bookingError'),
        panels: Array.prototype.slice.call(document.querySelectorAll('.booking-panel')),
        categoryFilter: document.getElementById('categoryFilter'),
        serviceChoices: document.getElementById('serviceChoices'),
        serviceEmpty: document.getElementById('serviceEmpty'),
        toStep2: document.getElementById('toStep2'),
        masterChoices: document.getElementById('masterChoices'),
        masterEmpty: document.getElementById('masterEmpty'),
        toStep3: document.getElementById('toStep3'),
        dateStrip: document.getElementById('dateStrip'),
        dateEmpty: document.getElementById('dateEmpty'),
        toStep4: document.getElementById('toStep4'),
        slotsGrid: document.getElementById('slotsGrid'),
        slotsEmpty: document.getElementById('slotsEmpty'),
        slotsHint: document.getElementById('slotsHint'),
        toStep5: document.getElementById('toStep5'),
        nameField: document.getElementById('nameField'),
        clientName: document.getElementById('clientName'),
        toStep6: document.getElementById('toStep6'),
        phoneField: document.getElementById('phoneField'),
        clientPhone: document.getElementById('clientPhone'),
        toStep7: document.getElementById('toStep7'),
        clientTelegram: document.getElementById('clientTelegram'),
        telegramHint: document.getElementById('telegramHint'),
        toStep8: document.getElementById('toStep8'),
        clientComment: document.getElementById('clientComment'),
        consent: document.getElementById('consent'),
        submit: document.getElementById('submitBooking'),
        sumService: document.getElementById('sumService'),
        sumMaster: document.getElementById('sumMaster'),
        sumDate: document.getElementById('sumDate'),
        sumTime: document.getElementById('sumTime'),
        sumDuration: document.getElementById('sumDuration'),
        sumPrice: document.getElementById('sumPrice')
    };

    /* ---------- Вспомогательные ---------- */

    function token() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function showError(message) {
        if (!message) {
            el.error.classList.remove('is-visible');
            el.error.textContent = '';
            return;
        }
        el.error.textContent = message;
        el.error.classList.add('is-visible');
        el.error.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    function get(url) {
        return fetch(url, { headers: { 'Accept': 'application/json' } })
            .then(function (response) {
                if (!response.ok) throw new Error('HTTP ' + response.status);
                return response.json();
            });
    }

    function post(url, body) {
        return fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
                'RequestVerificationToken': token()
            },
            body: JSON.stringify(body)
        }).then(function (response) {
            return response.json().catch(function () { return { ok: false, error: 'Ошибка сервера' }; })
                .then(function (data) {
                    if (!response.ok && data && !data.error) data.error = 'Ошибка сервера';
                    return data;
                });
        });
    }

    function setLoading(button, loading) {
        if (!button) return;
        button.classList.toggle('is-loading', loading);
        button.disabled = loading;
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /* ---------- Навигация по шагам ---------- */

    function goTo(step) {
        state.step = step;
        if (step > state.maxStep) state.maxStep = step;

        el.panels.forEach(function (panel) {
            panel.classList.toggle('is-active', Number(panel.dataset.step) === step);
        });

        Array.prototype.forEach.call(el.steps.children, function (chip) {
            var index = Number(chip.dataset.stepChip);
            chip.classList.toggle('is-active', index === step);
            chip.classList.toggle('is-done', index < step);
        });

        var panel = el.panels.filter(function (p) { return Number(p.dataset.step) === step; })[0];
        if (panel) {
            var top = panel.getBoundingClientRect().top + window.pageYOffset - 120;
            window.scrollTo({ top: top, behavior: 'smooth' });
        }
    }

    /* ---------- Сводка ---------- */

    function renderSummary() {
        el.sumService.textContent = state.service ? state.service.name : '—';
        el.sumMaster.textContent = state.master ? state.master.name : '—';
        el.sumDate.textContent = state.date ? state.date.label : '—';
        el.sumTime.textContent = state.time ? state.time : '—';
        el.sumDuration.textContent = state.service ? state.service.durationLabel : '—';
        el.sumPrice.textContent = state.service ? state.service.priceLabel : '—';
    }

    /* ---------- Шаг 1. Услуга ---------- */

    function clearService() {
        state.service = null;
        state.master = null;
        state.date = null;
        state.time = null;
        el.toStep2.disabled = true;
        el.toStep3.disabled = true;
        el.toStep4.disabled = true;
        el.toStep5.disabled = true;
        Array.prototype.forEach.call(el.serviceChoices.children, function (item) {
            item.classList.remove('is-selected');
        });
        renderSummary();
    }

    function filterServices() {
        var categoryId = el.categoryFilter.value;
        var visible = 0;
        var selectedIsVisible = false;

        Array.prototype.forEach.call(el.serviceChoices.children, function (button) {
            var match = !categoryId || button.dataset.categoryId === categoryId;
            button.hidden = !match;
            if (match) visible++;
            if (match && button.classList.contains('is-selected')) selectedIsVisible = true;
        });

        el.serviceEmpty.hidden = visible > 0;

        // Выбранная услуга могла уехать в другое направление. Оставлять её выбранной
        // нельзя: кнопки не видно, а «Далее» ведёт на шаг мастера по невидимой услуге.
        if (state.service && !selectedIsVisible) clearService();
    }

    el.categoryFilter.addEventListener('change', filterServices);

    el.serviceChoices.addEventListener('click', function (event) {
        var button = event.target.closest('[data-service-id]');
        if (!button) return;

        Array.prototype.forEach.call(el.serviceChoices.children, function (item) {
            item.classList.toggle('is-selected', item === button);
        });

        state.service = catalog.filter(function (s) { return s.id === Number(button.dataset.serviceId); })[0] || null;
        el.toStep2.disabled = !state.service;

        // Смена услуги сбрасывает всё, что зависело от неё.
        state.master = null;
        state.date = null;
        state.time = null;
        el.toStep3.disabled = true;
        el.toStep4.disabled = true;
        el.toStep5.disabled = true;
        renderSummary();
    });

    el.toStep2.addEventListener('click', function () {
        if (!state.service) return;
        showError('');
        loadMasters();
        goTo(2);
    });

    /* ---------- Шаг 2. Мастер ---------- */

    function loadMasters() {
        el.masterChoices.innerHTML = '<div class="empty-state">Загружаем мастеров…</div>';
        el.masterEmpty.hidden = true;
        el.toStep3.disabled = true;

        get('/api/masters?serviceId=' + state.service.id)
            .then(function (masters) {
                if (!masters || !masters.length) {
                    el.masterChoices.innerHTML = '';
                    el.masterEmpty.hidden = false;
                    return;
                }

                el.masterChoices.innerHTML = masters.map(function (master) {
                    var photo = master.photoPath
                        ? '<img class="choice__avatar" src="' + escapeHtml(master.photoPath) + '" alt="" loading="lazy" />'
                        : '<span class="choice__avatar" aria-hidden="true"></span>';

                    return '<button type="button" class="choice choice--master" data-master-id="' + master.id + '">'
                        + photo
                        + '<span>'
                        + '<span class="choice__name">' + escapeHtml(master.name) + '</span>'
                        + (master.specialization ? '<span class="choice__meta">' + escapeHtml(master.specialization) + '</span>' : '')
                        + '</span>'
                        + '</button>';
                }).join('');
            })
            .catch(function () {
                el.masterChoices.innerHTML = '';
                showError('Не удалось загрузить мастеров. Обновите страницу.');
            });
    }

    el.masterChoices.addEventListener('click', function (event) {
        var button = event.target.closest('[data-master-id]');
        if (!button) return;

        Array.prototype.forEach.call(el.masterChoices.children, function (item) {
            item.classList.toggle('is-selected', item === button);
        });

        var id = Number(button.dataset.masterId);
        state.master = { id: id, name: button.querySelector('.choice__name').textContent };
        el.toStep3.disabled = false;

        state.date = null;
        state.time = null;
        el.toStep4.disabled = true;
        el.toStep5.disabled = true;
        renderSummary();
    });

    el.toStep3.addEventListener('click', function () {
        if (!state.master) return;
        showError('');
        loadDates();
        goTo(3);
    });

    /* ---------- Шаг 3. Дата ---------- */

    function loadDates() {
        el.dateStrip.innerHTML = '<div class="empty-state">Загружаем свободные дни…</div>';
        el.dateEmpty.hidden = true;
        el.toStep4.disabled = true;

        get('/api/dates?serviceId=' + state.service.id + '&masterId=' + state.master.id)
            .then(function (dates) {
                if (!dates || !dates.length) {
                    el.dateStrip.innerHTML = '';
                    el.dateEmpty.hidden = false;
                    return;
                }

                el.dateStrip.innerHTML = dates.map(function (date) {
                    return '<button type="button" class="date-chip" data-date="' + escapeHtml(date.value) + '"'
                        + ' data-label="' + escapeHtml(date.label) + '">'
                        + '<span class="date-chip__dow">' + escapeHtml(date.weekday) + '</span>'
                        + '<span class="date-chip__day">' + date.day + '</span>'
                        + '<span class="date-chip__mon">' + escapeHtml(date.monthShort) + '</span>'
                        + '</button>';
                }).join('');
            })
            .catch(function () {
                el.dateStrip.innerHTML = '';
                showError('Не удалось загрузить даты. Обновите страницу.');
            });
    }

    el.dateStrip.addEventListener('click', function (event) {
        var chip = event.target.closest('[data-date]');
        if (!chip) return;

        Array.prototype.forEach.call(el.dateStrip.children, function (item) {
            item.classList.toggle('is-selected', item === chip);
        });

        state.date = { value: chip.dataset.date, label: chip.dataset.label };
        el.toStep4.disabled = false;

        state.time = null;
        el.toStep5.disabled = true;
        renderSummary();
    });

    el.toStep4.addEventListener('click', function () {
        if (!state.date) return;
        showError('');
        loadSlots();
        goTo(4);
    });

    /* ---------- Шаг 4. Время ---------- */

    function loadSlots() {
        el.slotsGrid.innerHTML = '<div class="empty-state">Ищем свободное время…</div>';
        el.slotsEmpty.hidden = true;
        el.slotsHint.textContent = 'Свободное время на ' + state.date.label.toLowerCase() + '.';
        el.toStep5.disabled = true;

        get('/api/slots?serviceId=' + state.service.id
            + '&masterId=' + state.master.id
            + '&date=' + encodeURIComponent(state.date.value))
            .then(function (result) {
                if (!result.ok) {
                    el.slotsGrid.innerHTML = '';
                    el.slotsEmpty.textContent = result.error || 'На этот день всё занято. Выберите другую дату.';
                    el.slotsEmpty.hidden = false;
                    return;
                }

                if (!result.slots || !result.slots.length) {
                    el.slotsGrid.innerHTML = '';
                    el.slotsEmpty.hidden = false;
                    return;
                }

                el.slotsGrid.innerHTML = result.slots.map(function (slot) {
                    return '<button type="button" class="slot" data-time="' + escapeHtml(slot.time) + '">'
                        + escapeHtml(slot.time) + '</button>';
                }).join('');
            })
            .catch(function () {
                el.slotsGrid.innerHTML = '';
                showError('Не удалось получить свободное время. Обновите страницу.');
            });
    }

    el.slotsGrid.addEventListener('click', function (event) {
        var button = event.target.closest('[data-time]');
        if (!button) return;

        Array.prototype.forEach.call(el.slotsGrid.children, function (item) {
            item.classList.toggle('is-selected', item === button);
        });

        state.time = button.dataset.time;
        el.toStep5.disabled = false;
        renderSummary();
    });

    el.toStep5.addEventListener('click', function () {
        if (!state.time) return;
        showError('');
        goTo(5);
    });

    /* ---------- Шаг 5. Имя ---------- */

    el.clientName.value = state.name;

    el.toStep6.addEventListener('click', function () {
        var value = el.clientName.value.trim();
        if (value.length < 2) {
            el.nameField.classList.add('has-error');
            el.clientName.focus();
            return;
        }
        el.nameField.classList.remove('has-error');
        state.name = value;
        showError('');
        goTo(6);
    });

    el.clientName.addEventListener('input', function () {
        if (el.clientName.value.trim().length >= 2) el.nameField.classList.remove('has-error');
    });

    el.clientName.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') { event.preventDefault(); el.toStep6.click(); }
    });

    /* ---------- Шаг 6. Телефон ---------- */

    function digits(value) {
        return (value || '').replace(/\D/g, '');
    }

    /* Маска российского номера.
       Правило одно: в поле всегда лежит «+7 (XXX) XXX-XX-XX», а из значения мы читаем
       только национальные цифры (те, что после +7), максимум десять.
       Раньше код при ровно десяти цифрах дописывал спереди семёрку — и стирание превращалось
       в ад: убираешь одну цифру, маска тут же возвращает «7», номер сам мутирует, а стереть
       его до конца и ввести заново нельзя. Теперь код страны снимается всегда, когда строка
       начинается с нашей же маски «+7», поэтому стирание честно удаляет цифру.

       Восьмёрка в начале — тоже код страны, и снимается сразу: иначе привычное «8999…»
       превращалось бы в номер +7 (899) 900-00-01. Побочный эффект: первое нажатие «8»
       ничего не показывает — восьмёрка уходит в код страны, а номер начинается со следующей
       цифры. */
    function nationalDigits(raw) {
        var only = digits(raw);
        if (!only) return '';

        if (/^\s*\+?7/.test(raw)) {
            // Строка начинается с «+7» — это наша маска, а не цифра номера.
            only = only.slice(1);
        } else if (only[0] === '8' || (only[0] === '7' && only.length === 11)) {
            // «8XXXXXXXXXX» и «7XXXXXXXXXX» — номер с кодом страны.
            only = only.slice(1);
        }

        return only.slice(0, 10);
    }

    function formatPhone(national) {
        if (!national) return '';
        var out = '+7';
        if (national.length) out += ' (' + national.slice(0, 3);
        if (national.length > 3) out += ') ' + national.slice(3, 6);
        if (national.length > 6) out += '-' + national.slice(6, 8);
        if (national.length > 8) out += '-' + national.slice(8, 10);
        return out;
    }

    /** Сколько цифр стоит в строке до позиции caret — чтобы вернуть каретку на место. */
    function digitsBefore(value, caret) {
        return digits(value.slice(0, caret)).length;
    }

    /** Позиция в отформатированной строке, после которой набрано count цифр. */
    function caretAfterDigits(value, count) {
        var seen = 0;
        for (var i = 0; i < value.length; i++) {
            if (/\d/.test(value[i])) {
                seen++;
                if (seen === count) return i + 1;
            }
        }
        return value.length;
    }

    function applyPhoneMask() {
        var before = el.clientPhone.value;
        var caret = el.clientPhone.selectionStart;
        if (caret === null) caret = before.length;

        var national = nationalDigits(before);
        var formatted = formatPhone(national);

        if (formatted !== before) {
            var wanted = digitsBefore(before, caret);
            // Если каретка стояла на самом конце — оставляем её на конце: иначе после
            // добавления скобок и дефисов курсор прыгал бы в середину.
            if (caret >= before.length) {
                el.clientPhone.value = formatted;
                el.clientPhone.setSelectionRange(formatted.length, formatted.length);
            } else {
                el.clientPhone.value = formatted;
                var position = caretAfterDigits(formatted, wanted);
                el.clientPhone.setSelectionRange(position, position);
            }
        }

        if (national.length === 10) el.phoneField.classList.remove('has-error');
    }

    el.clientPhone.addEventListener('input', applyPhoneMask);
    el.clientPhone.addEventListener('focus', applyPhoneMask);

    el.clientPhone.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') { event.preventDefault(); el.toStep7.click(); }
    });

    el.toStep7.addEventListener('click', function () {
        if (nationalDigits(el.clientPhone.value).length !== 10) {
            el.phoneField.classList.add('has-error');
            el.clientPhone.focus();
            return;
        }
        el.phoneField.classList.remove('has-error');
        // Отправляем то, что видит клиент: сервер всё равно нормализует номер сам.
        state.phone = formatPhone(nationalDigits(el.clientPhone.value));
        el.clientPhone.value = state.phone;
        showError('');
        goTo(7);
    });

    /* ---------- Шаг 7. Telegram ---------- */

    /* Ник приводится к виду «@nick». Правило то же, что на сервере
       (Helpers/TelegramNick.cs): поле не должно принимать «что угодно» — иначе в карточке
       клиента окажется «@мой телеграм», и написать человеку будет нельзя.
       Всё, что ником не является, поле очищает: пусто честнее мусора. */
    function normalizeTelegramNick(raw) {
        var text = (raw || '').trim();
        if (!text) return '';

        var prefixes = ['https://t.me/', 'http://t.me/', 'https://telegram.me/',
            'http://telegram.me/', 't.me/', 'telegram.me/'];
        for (var i = 0; i < prefixes.length; i++) {
            if (text.toLowerCase().indexOf(prefixes[i]) === 0) {
                text = text.slice(prefixes[i].length);
                break;
            }
        }

        var cut = text.search(/[?/\s]/);
        if (cut >= 0) text = text.slice(0, cut);

        text = text.replace(/^@+/, '');

        if (text.length < 3 || text.length > 32) return '';
        if (!/^[A-Za-z0-9_]+$/.test(text)) return '';
        if (/^[0-9_]/.test(text)) return '';
        if (text.indexOf('__') >= 0 || text.slice(-1) === '_') return '';

        return '@' + text;
    }

    function applyTelegramNick() {
        var raw = el.clientTelegram.value.trim();
        if (!raw) {
            el.clientTelegram.value = '';
            setTelegramHint(false);
            return '';
        }

        var nick = normalizeTelegramNick(raw);
        el.clientTelegram.value = nick;
        setTelegramHint(raw.length > 0 && !nick);
        return nick;
    }

    function setTelegramHint(rejected) {
        if (!el.telegramHint) return;
        el.telegramHint.textContent = rejected
            ? 'Это не похоже на ник Telegram — поле оставили пустым. Ник выглядит так: @anna'
            : 'Можно пропустить — запись всё равно сохранится.';
        el.telegramHint.classList.toggle('field__hint--warn', rejected);
    }

    el.clientTelegram.addEventListener('blur', applyTelegramNick);
    el.clientTelegram.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') { event.preventDefault(); el.toStep8.click(); }
    });

    el.toStep8.addEventListener('click', function () {
        state.telegram = applyTelegramNick();
        showError('');
        goTo(8);
    });

    /* ---------- Шаг 8. Подтверждение ---------- */

    el.submit.addEventListener('click', function () {
        if (!el.consent.checked) {
            showError('Подтвердите согласие с правилами записи.');
            return;
        }

        state.comment = el.clientComment.value.trim();

        var payload = {
            serviceId: state.service.id,
            masterId: state.master.id,
            date: state.date.value,
            time: state.time,
            name: state.name,
            phone: state.phone,
            telegram: state.telegram || null,
            comment: state.comment || null
        };

        setLoading(el.submit, true);
        showError('');

        post('/api/bookings', payload)
            .then(function (result) {
                if (!result.ok) {
                    showError(result.error || 'Не удалось создать запись.');
                    setLoading(el.submit, false);

                    // Слот мог быть занят, пока заполнялась форма — обновляем список.
                    if (state.date) loadSlots();
                    return;
                }

                try {
                    sessionStorage.setItem('elora.lastBooking', JSON.stringify({
                        summary: result.summary,
                        token: result.manageToken,
                        telegramLink: result.telegramStartLink,
                        service: state.service.name,
                        master: state.master.name,
                        date: state.date.label,
                        time: state.time,
                        price: state.service.priceLabel
                    }));
                } catch (e) { /* приватный режим — не критично */ }

                window.location.href = '/booking/success?token=' + encodeURIComponent(result.manageToken);
            })
            .catch(function () {
                showError('Сервер не ответил. Проверьте соединение и попробуйте снова.');
                setLoading(el.submit, false);
            });
    });

    /* ---------- Кнопки «Назад» ---------- */

    Array.prototype.forEach.call(document.querySelectorAll('[data-back]'), function (button) {
        button.addEventListener('click', function () {
            showError('');
            goTo(Number(button.dataset.back));
        });
    });

    /* ---------- Стартовое состояние ---------- */

    filterServices();
    renderSummary();

    if (config.preselectedServiceId) {
        var button = el.serviceChoices.querySelector('[data-service-id="' + config.preselectedServiceId + '"]');
        if (button) {
            button.click();
            loadMasters();
            goTo(2);
        }
    }
})();
