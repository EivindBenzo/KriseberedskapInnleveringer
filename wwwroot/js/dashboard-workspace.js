(function () {
    const buttons = Array.from(document.querySelectorAll('[data-dashboard-target]'));
    const panels = Array.from(document.querySelectorAll('[data-dashboard-panel]'));
    const intro = document.getElementById('dashboardIntro');
    const activePanelKey = 'beredskap.activeDashboardPanel';

    function showPanel(target, scroll) {
        buttons.forEach(function (item) {
            item.setAttribute('aria-expanded', String(item.dataset.dashboardTarget === target));
        });
        panels.forEach(function (panel) {
            panel.hidden = panel.dataset.dashboardPanel !== target;
        });
        sessionStorage.setItem(activePanelKey, target);
        if (scroll) {
            panels.find(function (panel) { return panel.dataset.dashboardPanel === target; })
                ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }

    function showAll() {
        sessionStorage.removeItem(activePanelKey);
        buttons.forEach(function (item) { item.setAttribute('aria-expanded', 'false'); });
        panels.forEach(function (panel) { panel.hidden = false; });
    }

    const savedPanel = sessionStorage.getItem(activePanelKey);
    if (savedPanel && panels.some(function (panel) { return panel.dataset.dashboardPanel === savedPanel; })) {
        showPanel(savedPanel, false);
    } else { showAll(); }

    buttons.forEach(function (button) {
        button.addEventListener('click', function () {
            const target = button.dataset.dashboardTarget;
            const isOpen = button.getAttribute('aria-expanded') === 'true';
            if (isOpen) { showAll(); return; }
            showPanel(target, true);
        });
    });

    document.addEventListener('submit', function (event) {
        const panel = event.target.closest('[data-dashboard-panel]');
        if (panel) sessionStorage.setItem(activePanelKey, panel.dataset.dashboardPanel);
    });
})();
document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-dc-toggle]');
    if (!b) return;
    var more = b.closest('.dc-card').querySelector('.dc-more');
    if (more) { more.hidden = !more.hidden; b.setAttribute('aria-expanded', String(!more.hidden)); }
});

document.addEventListener('click', function (e) {
    var o = e.target.closest('[data-dc-dialog]');
    if (o) { var d = document.getElementById(o.dataset.dcDialog); if (d && d.showModal) d.showModal(); return; }
    var c = e.target.closest('[data-dc-close]');
    if (c) { var dd = c.closest('dialog'); if (dd) dd.close(); return; }
    if (e.target.tagName === 'DIALOG' && e.target.classList.contains('dc-dialog')) e.target.close();
});
document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-dc-toggle]');
    if (!b) return;
    var card = b.closest('.dc-card');
    var more = card && card.querySelector('.dc-more');
    if (more && !more.hidden) {
        var det = more.querySelector('details');
        if (det) det.open = true;
    }
});

document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-needs-reason]');
    if (!b) return;
    var input = b.closest('form').querySelector('input[name="reason"]');
    if (input && input.hidden) { e.preventDefault(); input.hidden = false; input.required = true; input.focus(); }
});

/* Round 21: deep links (#id) open the right panel and highlight the card */
(function () {
    function go() {
        var id = decodeURIComponent(location.hash.slice(1));
        if (!id) return;
        var el = document.getElementById(id);
        if (!el) return;
        var panel = el.closest('[data-dashboard-panel]');
        if (panel) {
            document.querySelectorAll('[data-dashboard-panel]').forEach(function (p) { p.hidden = p.dataset.dashboardPanel !== panel.dataset.dashboardPanel; });
            try { sessionStorage.setItem('beredskap.activeDashboardPanel', panel.dataset.dashboardPanel); } catch (e) { }
        }
        el.classList.add('is-target');
        setTimeout(function () { el.scrollIntoView({ behavior: 'smooth', block: 'center' }); }, 60);
        setTimeout(function () { el.classList.remove('is-target'); }, 3500);
    }
    if (document.querySelector('[data-dashboard-panel]')) { window.addEventListener('hashchange', go); go(); }
})();

/* Round 26: assign dialog */
(function () {
    function sync(form, resetQty) {
        var sel = form.querySelector('[data-as-select]'); if (!sel || !sel.selectedOptions.length) return;
        var o = sel.selectedOptions[0], q = form.querySelector('[data-as-qty]');
        form.querySelectorAll('[data-as-unit]').forEach(function (n) { n.textContent = o.dataset.unit; });
        form.querySelector('[data-as-avail]').textContent = o.dataset.avail;
        var km = /· (\d+) km/.exec(o.textContent); form.querySelector('[data-as-km]').textContent = km ? km[1] : '–';
        q.max = o.dataset.max;
        if (resetQty || Number(q.value) > Number(q.max)) q.value = o.dataset.max;
        rest(form);
    }
    function rest(form) {
        var q = form.querySelector('[data-as-qty]'), r = form.querySelector('[data-as-rest]'); if (!q || !r) return;
        var left = Math.max(0, Math.round((Number(form.dataset.mangler) - Number(q.value || 0)) * 100) / 100);
        r.textContent = left;
        var box = form.querySelector('[data-as-after]'); box.classList.toggle('is-done', left === 0);
        form.querySelector('[data-as-note]').textContent = left === 0 ? 'Behovet blir fullt dekket' : 'av ' + form.dataset.mangler + ' som mangler nå';
    }
    document.querySelectorAll('[data-as-form]').forEach(function (f) {
        sync(f, true);
        var sel = f.querySelector('[data-as-select]'); if (sel) sel.addEventListener('change', function () { sync(f, true); });
        var q = f.querySelector('[data-as-qty]'); if (!q) return; q.addEventListener('input', function () { rest(f); });
        var step = function (d) { var v = Number(q.value || 0) + d; v = Math.max(Number(q.min), Math.min(Number(q.max), v)); q.value = Math.round(v * 100) / 100; rest(f); };
        f.querySelector('[data-as-dec]').addEventListener('click', function () { step(-1); });
        f.querySelector('[data-as-inc]').addEventListener('click', function () { step(1); });
    });
})();
