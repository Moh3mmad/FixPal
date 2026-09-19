(() => {
    'use strict';

    document.querySelectorAll('.fp-customer-calendar').forEach((calendar) => {
        calendar.addEventListener('click', (event) => {
            if (!(event.target instanceof Element)) return;
            const slot = event.target.closest('[data-calendar-slot]:not(:disabled)');
            if (!slot || !calendar.contains(slot)) return;

            const panel = calendar.closest('.fp-appointment-panel');
            const start = panel?.querySelector(`#${calendar.dataset.calendarStart}`);
            const end = panel?.querySelector(`#${calendar.dataset.calendarEnd}`);
            if (!start || !end) return;

            start.value = slot.dataset.start;
            end.value = slot.dataset.end;
            start.dispatchEvent(new Event('change', { bubbles: true }));
            end.dispatchEvent(new Event('change', { bubbles: true }));
            calendar.querySelectorAll('[data-calendar-slot]').forEach((item) => {
                item.setAttribute('aria-pressed', String(item === slot));
            });
            start.focus();
        });
    });
})();
