(function () {
    const buttons = Array.from(document.querySelectorAll('[data-dashboard-target]'));
    const panels = Array.from(document.querySelectorAll('[data-dashboard-panel]'));

    buttons.forEach(function (button) {
        button.addEventListener('click', function () {
            const target = button.dataset.dashboardTarget;
            const isOpen = button.getAttribute('aria-expanded') === 'true';

            buttons.forEach(function (item) {
                item.setAttribute('aria-expanded', String(item === button && !isOpen));
            });
            panels.forEach(function (panel) {
                panel.hidden = isOpen || panel.dataset.dashboardPanel !== target;
            });

            if (!isOpen) {
                const firstPanel = panels.find(function (panel) { return panel.dataset.dashboardPanel === target; });
                firstPanel?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    });
})();
