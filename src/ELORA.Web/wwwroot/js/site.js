/* ELORA — общий клиентский скрипт: меню, аккордеоны, карусель отзывов. */
(function () {
  'use strict';

  /* ---------- Подтверждение перед отправкой формы ----------
     Формы с атрибутом data-confirm спрашивают согласие до отправки. Раньше такой обработчик
     был только в админке; странице «Мои записи» он нужен тоже — там есть кнопка «Забыть
     этот браузер». Обработчик один на документ, поэтому подходит и формам, добавленным позже. */
  document.addEventListener('submit', function (event) {
    var form = event.target.closest('form[data-confirm]');
    if (!form) return;
    if (!window.confirm(form.getAttribute('data-confirm') || 'Подтвердите действие')) {
      event.preventDefault();
    }
  });

  /* ---------- Мобильное меню ---------- */
  var burger = document.getElementById('burger');
  var menu = document.getElementById('mobileMenu');
  var menuClose = document.getElementById('menuClose');

  function openMenu() {
    if (!menu) return;
    menu.classList.add('is-open');
    menu.setAttribute('aria-hidden', 'false');
    if (burger) {
      burger.classList.add('is-open');
      burger.setAttribute('aria-expanded', 'true');
    }
    document.body.classList.add('no-scroll');
  }

  function closeMenu() {
    if (!menu) return;
    menu.classList.remove('is-open');
    menu.setAttribute('aria-hidden', 'true');
    if (burger) {
      burger.classList.remove('is-open');
      burger.setAttribute('aria-expanded', 'false');
    }
    document.body.classList.remove('no-scroll');
  }

  if (burger) burger.addEventListener('click', function () {
    if (menu && menu.classList.contains('is-open')) closeMenu(); else openMenu();
  });
  if (menuClose) menuClose.addEventListener('click', closeMenu);
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') closeMenu();
  });

  /* ---------- Универсальный аккордеон ----------
     Работает для FAQ и для мобильного прайс-листа. */
  document.querySelectorAll('[data-accordion]').forEach(function (root) {
    var single = root.getAttribute('data-accordion') === 'single';

    root.querySelectorAll('.faq__q, .price-acc__head').forEach(function (trigger) {
      trigger.addEventListener('click', function () {
        var item = trigger.closest('.faq__item, .price-acc__item');
        if (!item) return;

        var willOpen = !item.classList.contains('is-open');

        if (single) {
          root.querySelectorAll('.faq__item.is-open, .price-acc__item.is-open').forEach(function (other) {
            if (other !== item) {
              other.classList.remove('is-open');
              var t = other.querySelector('.faq__q, .price-acc__head');
              if (t) t.setAttribute('aria-expanded', 'false');
            }
          });
        }

        item.classList.toggle('is-open', willOpen);
        trigger.setAttribute('aria-expanded', willOpen ? 'true' : 'false');
      });
    });
  });

  /* В референсе все пункты FAQ свёрнуты — оставляем так же,
     пользователь раскрывает нужный сам. */

  /* ---------- Карусель отзывов: точки-индикаторы ---------- */
  var reviewsTrack = document.querySelector('.reviews__grid');
  var dotsWrap = document.querySelector('.reviews__dots');
  if (reviewsTrack && dotsWrap) {
    var cards = reviewsTrack.querySelectorAll('.review-card');
    cards.forEach(function (_, i) {
      var dot = document.createElement('span');
      dot.className = 'reviews__dot' + (i === 0 ? ' is-active' : '');
      dotsWrap.appendChild(dot);
    });

    var dots = dotsWrap.querySelectorAll('.reviews__dot');
    function syncDots() {
      if (!cards.length) return;
      var index = Math.round(reviewsTrack.scrollLeft / Math.max(1, reviewsTrack.clientWidth));
      dots.forEach(function (dot, i) {
        dot.classList.toggle('is-active', i === index);
      });
    }
    reviewsTrack.addEventListener('scroll', syncDots, { passive: true });
  }

  /* ---------- Плавная прокрутка к якорям ---------- */
  document.querySelectorAll('a[href^="#"]').forEach(function (link) {
    link.addEventListener('click', function (e) {
      var id = link.getAttribute('href');
      if (!id || id === '#') return;
      var target = document.querySelector(id);
      if (!target) return;
      e.preventDefault();
      target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  });

  /* ---------- Появление секций при скролле ----------
     Класс js-reveal ставится только из JS, поэтому при отключённом
     JavaScript контент остаётся видимым, а не превращается в пустые блоки. */
  if ('IntersectionObserver' in window) {
    document.documentElement.classList.add('js-reveal');

    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('is-revealed');
        observer.unobserve(entry.target);
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.01 });

    var targets = document.querySelectorAll('[data-reveal]');
    targets.forEach(function (el) { observer.observe(el); });

    // Страховка: элементы, которые уже в кадре (или очень высокие),
    // показываем сразу, чтобы снимок страницы не оставался полупустым.
    var revealVisible = function () {
      var limit = window.innerHeight;
      targets.forEach(function (el) {
        if (el.classList.contains('is-revealed')) return;
        var rect = el.getBoundingClientRect();
        if (rect.top < limit && rect.bottom > 0) {
          el.classList.add('is-revealed');
          observer.unobserve(el);
        }
      });
    };

    window.addEventListener('load', revealVisible);
    window.addEventListener('resize', revealVisible);
  }
})();
