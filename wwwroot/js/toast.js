(function () {
    var KEY = 'bp-scroll';
    if ('scrollRestoration' in history) history.scrollRestoration = 'manual';

    // Etter et skjemainnsendt valg: gå tilbake til samme sted uten hopp.
    try {
        var saved = sessionStorage.getItem(KEY);
        if (saved) {
            sessionStorage.removeItem(KEY);
            var data = JSON.parse(saved);
            if (data.path === location.pathname && Date.now() - data.t < 15000) {
                document.documentElement.style.scrollBehavior = 'auto';
                window.scrollTo(0, data.y);
                window.addEventListener('load', function () { window.scrollTo(0, data.y); });
            }
        }
    } catch (e) { }

    document.addEventListener('submit', function (e) {
        var f = e.target;
        if (!f || (f.method || '').toLowerCase() !== 'post') return;
        try {
            sessionStorage.setItem(KEY, JSON.stringify({ y: window.scrollY, path: location.pathname, t: Date.now() }));
        } catch (err) { }
    }, true);

    function dismiss(t) {
        t.classList.add('is-leaving');
        setTimeout(function () { t.remove(); }, 250);
    }

    document.querySelectorAll('[data-toast]').forEach(function (t) {
        var ok = t.dataset.kind === 'success';
        var ms = ok ? 5000 : 9000;
        t.style.setProperty('--toast-ms', ms + 'ms');
        var timer = setTimeout(function () { dismiss(t); }, ms);
        t.addEventListener('mouseenter', function () { clearTimeout(timer); t.classList.add('is-paused'); });
        t.addEventListener('mouseleave', function () { t.classList.remove('is-paused'); timer = setTimeout(function () { dismiss(t); }, 2500); });
    });

    document.addEventListener('click', function (e) {
        var x = e.target.closest('[data-toast-close],[data-toast-retry]');
        if (!x) return;
        var t = x.closest('[data-toast]');
        if (t) dismiss(t);
    });
})();
