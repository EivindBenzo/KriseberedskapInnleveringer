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
