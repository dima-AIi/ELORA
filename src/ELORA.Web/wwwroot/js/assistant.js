/* ELORA — AI-консультант в секции FAQ.
   Одна точка отправки — sendToAssistant(message): и кнопки быстрых вопросов,
   и поле ввода ходят через неё. Ответ приходит с сервера (/api/chat), ключ
   провайдера в браузер не попадает. */
(function () {
  'use strict';

  var root = document.querySelector('[data-assistant]');
  if (!root) return;

  var chat = root.querySelector('[data-chat]');
  var form = root.querySelector('[data-chat-form]');
  var input = root.querySelector('[data-chat-input]');
  var send = root.querySelector('[data-chat-send]');
  var quick = root.querySelector('[data-quick]');
  if (!chat || !form || !input || !send) return;

  var GREETING = 'Здравствуйте! Я Эльора, ваш AI-помощник. Спросите об услугах, ценах ' +
    'или записи — отвечу сразу.';
  var FAILURE = 'Что-то пошло не так, попробуйте ещё раз';

  /* История едет на сервер вместе с вопросом: без неё консультант каждый раз
     отвечает как впервые и теряет нить разговора. Держим столько же, сколько
     принимает сервер (Ai:HistoryMessages), иначе лишнее просто отбросится. */
  var HISTORY_LIMIT = 6;
  var history = [];

  var busy = false;

  /* ---------- Отрисовка ---------- */

  function addBubble(text, kind) {
    var bubble = document.createElement('div');
    bubble.className = 'bubble bubble--' + kind;
    bubble.textContent = text;
    chat.appendChild(bubble);
    chat.scrollTop = chat.scrollHeight;
    return bubble;
  }

  function addTyping() {
    var bubble = document.createElement('div');
    bubble.className = 'bubble bubble--bot bubble--typing';
    bubble.innerHTML = '<i></i><i></i><i></i>';
    bubble.setAttribute('aria-label', 'Консультант печатает');
    chat.appendChild(bubble);
    chat.scrollTop = chat.scrollHeight;
    return bubble;
  }

  /* ---------- Состояние ---------- */

  function setBusy(value) {
    busy = value;
    input.disabled = value;
    send.disabled = value || input.value.trim() === '';
    if (quick) {
      quick.querySelectorAll('.chip').forEach(function (chip) {
        chip.disabled = value;
      });
    }
    // preventScroll: возвращаем фокус после ответа, но не даём странице прыгнуть.
    if (!value) input.focus({ preventScroll: true });
  }

  /* Кнопка активна, только когда есть что отправлять: пустое поле — disabled. */
  function syncSend() {
    send.disabled = busy || input.value.trim() === '';
  }

  /* ---------- Отправка ---------- */

  function sendToAssistant(message) {
    var text = (message || '').trim();
    if (!text || busy) return;

    addBubble(text, 'me');
    input.value = '';
    setBusy(true);

    var typing = addTyping();
    var sentHistory = history.slice(-HISTORY_LIMIT);
    history.push({ role: 'user', content: text });

    fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: text, history: sentHistory })
    })
      .then(function (response) {
        return response.json().catch(function () { return null; })
          .then(function (data) {
            return { ok: response.ok, data: data };
          });
      })
      .then(function (result) {
        typing.remove();

        var reply = result.data && typeof result.data.reply === 'string'
          ? result.data.reply.trim()
          : '';

        if (!result.ok || !reply) {
          var reason = result.data && result.data.error ? result.data.error : FAILURE;
          addBubble(reason, 'error');
          addRetry(text);
          return;
        }

        addBubble(reply, 'bot');
        history.push({ role: 'assistant', content: reply });
      })
      .catch(function () {
        typing.remove();
        addBubble(FAILURE, 'error');
        addRetry(text);
      })
      .then(function () {
        setBusy(false);
        syncSend();
      });
  }

  /* Повтор неудавшегося вопроса: отдельная кнопка под сообщением об ошибке. */
  function addRetry(text) {
    var wrap = document.createElement('div');
    wrap.className = 'bubble bubble--error';
    wrap.style.padding = '0';
    wrap.style.background = 'none';

    var button = document.createElement('button');
    button.type = 'button';
    button.className = 'chip';
    button.textContent = 'Повторить';
    button.addEventListener('click', function () {
      wrap.remove();
      sendToAssistant(text);
    });

    wrap.appendChild(button);
    chat.appendChild(wrap);
    chat.scrollTop = chat.scrollHeight;
  }

  /* ---------- События ---------- */

  form.addEventListener('submit', function (event) {
    event.preventDefault();
    sendToAssistant(input.value);
  });

  input.addEventListener('input', syncSend);

  if (quick) {
    quick.addEventListener('click', function (event) {
      var chip = event.target.closest('[data-question]');
      if (!chip || busy) return;
      sendToAssistant(chip.getAttribute('data-question'));
    });
  }

  /* ---------- Старт ---------- */

  addBubble(GREETING, 'bot');
  syncSend();

  // Фокус в поле чата на загрузке страницы НЕ ставим: браузер прокручивает к
  // сфокусированному полю, и сайт открывался сразу на секции с консультантом,
  // а не с начала страницы. Фокус ставим только когда человек сам начал диалог
  // (см. setBusy) — и всегда с preventScroll, чтобы страница не прыгала.
})();
