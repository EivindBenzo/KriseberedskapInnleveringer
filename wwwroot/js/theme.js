(function () {
    'use strict';
    const root = document.documentElement;
    let theme = 'light';
    try {
        if (localStorage.getItem('heimevernet-theme') === 'dark') theme = 'dark';
    } catch (_) {
        // The light default and toggle also work when browser storage is blocked.
    }
    root.dataset.theme = theme;

    document.addEventListener('DOMContentLoaded', function () {
        const button = document.getElementById('themeToggle');
        function updateControl() {
            const dark = root.dataset.theme === 'dark';
            button.setAttribute('aria-pressed', String(dark));
            const action = dark ? 'Bytt til lys modus' : 'Bytt til mørk modus';
            button.setAttribute('aria-label', action);
            button.setAttribute('title', action);
            document.getElementById('themeIcon').textContent = dark ? '☾' : '☀';
            document.getElementById('themeLabel').textContent = dark ? 'Mørk modus' : 'Lys modus';
        }
        updateControl();
        button.hidden = false;
        button.addEventListener('click', function () {
            root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
            updateControl();
            try {
                localStorage.setItem('heimevernet-theme', root.dataset.theme);
            } catch (_) {
                // Keep the selected theme for this page even without storage.
            }
        });
    });
}());
