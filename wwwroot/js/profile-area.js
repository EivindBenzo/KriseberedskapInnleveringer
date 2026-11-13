(function () {
    var btns = document.querySelectorAll('[data-pn]');
    var panels = document.querySelectorAll('[data-pp]');
    function show(name) {
        panels.forEach(function (p) { p.hidden = p.dataset.pp !== name; });
        btns.forEach(function (b) {
            var on = b.dataset.pn === name;
            b.classList.toggle('is-active', on);
            if (on) b.setAttribute('aria-current', 'page'); else b.removeAttribute('aria-current');
        });
        try { history.replaceState(null, '', '#' + name); } catch (e) { }
        var t = document.getElementById('shellMenu'); if (t) t.checked = false;
    }
    btns.forEach(function (b) { b.addEventListener('click', function () { show(b.dataset.pn); }); });
    var h = location.hash.replace('#', '');
    if (h && document.querySelector('[data-pp="' + h + '"]')) show(h);

    var search = document.querySelector('[data-pk-search]');
    var chips = document.querySelectorAll('[data-pk-role]');
    var role = '';
    function filter() {
        var q = (search && search.value || '').trim().toLowerCase(), n = 0;
        document.querySelectorAll('[data-pk-card]').forEach(function (c) {
            var ok = (!role || c.dataset.role === role) && (!q || c.dataset.text.indexOf(q) >= 0);
            c.hidden = !ok; if (ok) n++;
        });
        var e = document.querySelector('[data-pk-empty]'); if (e) e.hidden = n > 0;
    }
    if (search) search.addEventListener('input', filter);
    chips.forEach(function (c) {
        c.addEventListener('click', function () {
            role = c.dataset.pkRole;
            chips.forEach(function (x) { x.classList.toggle('is-on', x === c); });
            filter();
        });
    });
})();
