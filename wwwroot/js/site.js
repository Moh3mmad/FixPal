// FixPal global JavaScript intentionally stays small.
// Feature-specific scripts (map, request tracking, AI upload) should live in
// dedicated files and be loaded only by the views that need them.

document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('time[data-utc]').forEach(element => {
        const date = new Date(element.dataset.utc);
        if (!Number.isNaN(date.getTime())) {
            element.dateTime = element.dataset.utc;
            element.textContent = new Intl.DateTimeFormat('ar', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
            element.title = 'حسب توقيت جهازك';
        }
    });
    const nav = document.querySelector('.fp-navbar');
    if (!nav) return;

    const updateNavState = () => {
        nav.classList.toggle('is-scrolled', window.scrollY > 8);
    };

    updateNavState();
    window.addEventListener('scroll', updateNavState, { passive: true });
});
