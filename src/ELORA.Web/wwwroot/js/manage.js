/* ==========================================================================
   ELORA — управление записью: отмена и перенос.
   Все проверки дублируются на сервере: браузер только подсказывает.
   ========================================================================== */
(function () {
    'use strict';

    var config = window.ELORA_MANAGE;
    if (!config) return;

    var alertBox = document.getElementById('manageAlert');
    var cancelButton = document.getElementById('cancelBooking');
    var toggleButton = document.getElementById('toggleReschedule');
    var box = document.getElementById('rescheduleBox');
    var closeButton = document.getElementById('closeReschedule');
    var masterSelect = document.getElementById('rescheduleMaster');
    var datesStrip = document.getElementById('rescheduleDates');
    var slotsGrid = document.getElementById('rescheduleSlots');
    var emptyState = document.getElementById('rescheduleEmpty');
    var confirmButton = document.getElementById('confirmReschedule');

    var selected = { date: null, time: null };

    function token() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function showAlert(message, type) {
        if (!message) {
            alertBox.className = 'alert';
            alertBox.textContent = '';
            return;
        }
        alertBox.className = 'alert is-visible alert--' + (type || 'error');
        alertBox.textContent = message;
        alertBox.scrollIntoView({ behavior: 'smooth', block: 'center' });
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
            body: body ? JSON.stringify(body) : null
        }).then(function (response) {
            return response.json().catch(function () { return { ok: false, error: 'Ошибка сервера' }; });
        });
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function setLoading(button, loading) {
        if (!button) return;
        button.classList.toggle('is-loading', loading);
        button.disabled = loading;
    }

    /* ---------- Отмена ---------- */

    if (cancelButton) {
        cancelButton.addEventListener('click', function () {
            if (!window.confirm('Отменить запись? Время освободится для других клиентов.')) return;

            setLoading(cancelButton, true);
            showAlert('');

            post('/api/bookings/' + encodeURIComponent(config.token) + '/cancel')
                .then(function (result) {
                    if (!result.ok) {
                        showAlert(result.error || 'Не удалось отменить запись.');
                        setLoading(cancelButton, false);
                        return;
                    }
                    window.location.reload();
                })
                .catch(function () {
                    showAlert('Сервер не ответил. Попробуйте позже.');
                    setLoading(cancelButton, false);
                });
        });
    }

    /* ---------- Перенос ---------- */

    function loadDates() {
        selected.date = null;
        selected.time = null;
        confirmButton.disabled = true;
        slotsGrid.innerHTML = '';
        emptyState.hidden = false;
        datesStrip.innerHTML = '<div class="empty-state">Загружаем свободные дни…</div>';

        get('/api/dates?serviceId=' + config.serviceId + '&masterId=' + masterSelect.value)
            .then(function (dates) {
                if (!dates || !dates.length) {
                    datesStrip.innerHTML = '';
                    showAlert('У этого мастера нет свободных дней. Выберите другого мастера.', 'info');
                    return;
                }

                datesStrip.innerHTML = dates.map(function (date) {
                    return '<button type="button" class="date-chip" data-date="' + escapeHtml(date.value) + '">'
                        + '<span class="date-chip__dow">' + escapeHtml(date.weekday) + '</span>'
                        + '<span class="date-chip__day">' + date.day + '</span>'
                        + '<span class="date-chip__mon">' + escapeHtml(date.monthShort) + '</span>'
                        + '</button>';
                }).join('');
            })
            .catch(function () { showAlert('Не удалось загрузить даты.'); });
    }

    function loadSlots(dateValue) {
        selected.time = null;
        confirmButton.disabled = true;
        emptyState.hidden = true;
        slotsGrid.innerHTML = '<div class="empty-state">Ищем свободное время…</div>';

        get('/api/slots?serviceId=' + config.serviceId
            + '&masterId=' + masterSelect.value
            + '&date=' + encodeURIComponent(dateValue))
            .then(function (result) {
                if (!result.ok || !result.slots || !result.slots.length) {
                    slotsGrid.innerHTML = '';
                    emptyState.textContent = result.error || 'На этот день всё занято.';
                    emptyState.hidden = false;
                    return;
                }

                slotsGrid.innerHTML = result.slots.map(function (slot) {
                    return '<button type="button" class="slot" data-time="' + escapeHtml(slot.time) + '">'
                        + escapeHtml(slot.time) + '</button>';
                }).join('');
            })
            .catch(function () { showAlert('Не удалось получить свободное время.'); });
    }

    if (toggleButton && box) {
        toggleButton.addEventListener('click', function () {
            var willOpen = box.hidden;
            box.hidden = !willOpen;
            toggleButton.classList.toggle('is-selected', willOpen);
            if (willOpen && !datesStrip.children.length) loadDates();
        });

        closeButton.addEventListener('click', function () {
            box.hidden = true;
            toggleButton.classList.remove('is-selected');
            showAlert('');
        });

        masterSelect.addEventListener('change', loadDates);

        datesStrip.addEventListener('click', function (event) {
            var chip = event.target.closest('[data-date]');
            if (!chip) return;

            Array.prototype.forEach.call(datesStrip.children, function (item) {
                item.classList.toggle('is-selected', item === chip);
            });

            selected.date = chip.dataset.date;
            loadSlots(selected.date);
        });

        slotsGrid.addEventListener('click', function (event) {
            var button = event.target.closest('[data-time]');
            if (!button) return;

            Array.prototype.forEach.call(slotsGrid.children, function (item) {
                item.classList.toggle('is-selected', item === button);
            });

            selected.time = button.dataset.time;
            confirmButton.disabled = false;
        });

        confirmButton.addEventListener('click', function () {
            if (!selected.date || !selected.time) return;

            setLoading(confirmButton, true);
            showAlert('');

            post('/api/bookings/' + encodeURIComponent(config.token) + '/reschedule', {
                masterId: Number(masterSelect.value),
                date: selected.date,
                time: selected.time
            }).then(function (result) {
                if (!result.ok) {
                    showAlert(result.error || 'Не удалось перенести запись.');
                    setLoading(confirmButton, false);
                    loadSlots(selected.date);
                    return;
                }
                window.location.reload();
            }).catch(function () {
                showAlert('Сервер не ответил. Попробуйте позже.');
                setLoading(confirmButton, false);
            });
        });
    }
})();
