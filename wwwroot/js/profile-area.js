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

(function () {
    var KEY = 'bp-fav-contacts', favs;
    try { favs = JSON.parse(localStorage.getItem(KEY) || '[]'); } catch (e) { favs = []; }
    var all = document.querySelector('[data-pk-all]'), box = document.querySelector('[data-pk-favs]');
    var sec = document.querySelector('[data-pk-favsec]'), head = document.querySelector('[data-pk-allh]');
    if (!all || !box) return;
    var cards = Array.prototype.slice.call(all.querySelectorAll('[data-pk-card]'));
    cards.forEach(function (c, i) { c.dataset.idx = i; });
    function render() {
        cards.forEach(function (c) {
            var on = favs.indexOf(c.dataset.id) >= 0;
            var s = c.querySelector('[data-pk-star]');
            s.textContent = on ? '★' : '☆'; s.setAttribute('aria-pressed', on); s.classList.toggle('is-fav', on);
            s.setAttribute('aria-label', on ? 'Fjern fra favoritter' : 'Legg til som favoritt');
        });
        cards.slice().sort(function (a, b) { return a.dataset.idx - b.dataset.idx; }).forEach(function (c) {
            (favs.indexOf(c.dataset.id) >= 0 ? box : all).appendChild(c);
        });
        sec.hidden = box.children.length === 0; head.hidden = sec.hidden;
    }
    document.addEventListener('click', function (e) {
        var s = e.target.closest('[data-pk-star]'); if (!s) return;
        var id = s.closest('[data-pk-card]').dataset.id, i = favs.indexOf(id);
        if (i >= 0) favs.splice(i, 1); else favs.push(id);
        try { localStorage.setItem(KEY, JSON.stringify(favs)); } catch (er) { }
        render();
    });
    render();
})();
