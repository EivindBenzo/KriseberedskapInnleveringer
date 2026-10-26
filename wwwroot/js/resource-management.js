(function () {
    'use strict';
    function attach(root, navigate) {
        if (!root || root.dataset.managementReady) return;
        root.dataset.managementReady = 'true';
        const form = root.querySelector('.filter-bar');
        const input = root.querySelector('#resourceCity');
        const list = root.querySelector('#resourceCityOptions');
        const status = root.querySelector('.resource-management-status');
        const dialog = root.querySelector('.resource-delete-dialog');
        const deleteForm = root.querySelector('[data-resource-delete-form]');
        const cancel = root.querySelector('[data-delete-cancel]');
        const error = root.querySelector('.resource-delete-error');
        let active = -1;
        let opener;
        let busy = false;
        let filterRequest;
        const options = () => Array.from(list.querySelectorAll('[role="option"]'));
        function closeList() {
            list.hidden = true;
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
            active = -1;
            options().forEach(option => option.setAttribute('aria-selected', 'false'));
        }
        function openList() {
            list.hidden = false;
            input.setAttribute('aria-expanded', 'true');
        }
        async function load(url) {
            if (navigate) return navigate(url, true);
            filterRequest?.abort();
            const request = filterRequest = new AbortController();
            const endpoint = new URL(url, window.location.href);
            endpoint.searchParams.set('fragment', 'true');
            root.setAttribute('aria-busy', 'true');
            try {
                const response = await fetch(endpoint, { credentials: 'same-origin', signal: request.signal });
                if (!response.ok || response.redirected) throw new Error();
                const template = document.createElement('template');
                template.innerHTML = await response.text();
                const replacement = template.content.querySelector('.resource-overview');
                if (!replacement || template.content.querySelector('script, iframe')) throw new Error();
                if (request.signal.aborted || !root.isConnected) return;
                root.replaceWith(replacement);
                window.ResourceIcons?.initializeImages(replacement);
                attach(replacement);
                const message = replacement.querySelector('.resource-management-status');
                message.textContent = 'Resultatene er oppdatert.';
                message.focus();
            } catch {
                if (!request.signal.aborted) {
                    status.textContent = 'Kunne ikke oppdatere resultatene. Prøv igjen med Vis resultater.';
                    status.focus();
                }
            } finally {
                if (filterRequest === request) root.removeAttribute('aria-busy');
            }
        }
        function filter() {
            const url = new URL(form.action, window.location.href);
            url.search = new URLSearchParams(new FormData(form)).toString();
            closeList();
            load(url);
        }
        function select(option) {
            input.value = option.dataset.city;
            filter();
        }
        input.addEventListener('focus', openList);
        input.addEventListener('click', openList);
        input.addEventListener('input', () => { closeList(); openList(); });
        input.addEventListener('keydown', event => {
            const items = options();
            if (event.key === 'Escape') {
                if (!list.hidden) { event.preventDefault(); event.stopPropagation(); closeList(); }
            } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                openList();
                if (!items.length) return;
                active = active < 0 ? (event.key === 'ArrowDown' ? 0 : items.length - 1)
                    : (active + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
                items.forEach((option, index) => option.setAttribute('aria-selected', String(index === active)));
                input.setAttribute('aria-activedescendant', items[active].id);
                items[active].scrollIntoView({ block: 'nearest' });
            } else if (event.key === 'Enter' && !list.hidden && active >= 0) {
                event.preventDefault();
                select(items[active]);
            }
        });
        list.addEventListener('mousedown', event => event.preventDefault());
        list.addEventListener('click', event => {
            const option = event.target.closest('[role="option"]');
            if (option) select(option);
        });
        root.addEventListener('focusout', event => {
            if (!event.relatedTarget || !input.parentElement.contains(event.relatedTarget)) closeList();
        });
        function outside(event) {
            if (!root.isConnected) document.removeEventListener('pointerdown', outside);
            else if (!input.parentElement.contains(event.target)) closeList();
        }
        document.addEventListener('pointerdown', outside);
        root.addEventListener('click', event => {
            if (!input.parentElement.contains(event.target)) closeList();
            const link = event.target.closest('[data-resource-delete]');
            if (!link || !dialog.showModal || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button > 0) return;
            event.preventDefault();
            opener = link;
            error.textContent = '';
            deleteForm.action = link.href;
            deleteForm.elements.version.value = link.dataset.version;
            root.querySelector('#resourceDeleteName').textContent = link.dataset.resourceName;
            dialog.showModal();
            cancel.focus();
        });
        cancel.addEventListener('click', () => { if (!busy) dialog.close(); });
        dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
        dialog.addEventListener('close', () => {
            (opener?.isConnected ? opener : status).focus({ preventScroll: true });
        });
        deleteForm.addEventListener('submit', async event => {
            event.preventDefault();
            if (busy) return;
            busy = true;
            error.textContent = '';
            Array.from(deleteForm.querySelectorAll('button')).forEach(button => { button.disabled = true; });
            try {
                const response = await fetch(deleteForm.action, {
                    method: 'POST', credentials: 'same-origin',
                    headers: { Accept: 'application/json' }, body: new FormData(deleteForm)
                });
                const result = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
                if (!response.ok || response.redirected || String(result?.deletedId) !== opener.dataset.resourceDelete)
                    throw new Error(result?.error || 'Ressursen ble ikke slettet. Last oversikten på nytt og prøv igjen.');
                const row = opener.closest('[data-resource-row]');
                const city = row.dataset.city.toLocaleLowerCase('nb-NO');
                options().forEach(option => {
                    if (option.dataset.city.toLocaleLowerCase('nb-NO') !== city) return;
                    const count = Number(option.dataset.count) - 1;
                    if (!count) option.remove();
                    else {
                        option.dataset.count = String(count);
                        option.textContent = option.dataset.city + ' (' + count + ')';
                    }
                });
                options().sort((a, b) => Number(b.dataset.count) - Number(a.dataset.count) ||
                    a.dataset.city.localeCompare(b.dataset.city, 'nb-NO')).forEach(option => list.append(option));
                row.remove();
                root.querySelector('.count-badge').textContent = root.querySelectorAll('[data-resource-row]').length;
                status.textContent = 'Ressursen er slettet.';
                document.dispatchEvent(new CustomEvent('resource-deleted', { detail: { id: result.deletedId } }));
                dialog.close();
            } catch (failure) {
                error.textContent = failure.message || 'Ressursen ble ikke slettet. Prøv igjen.';
                error.focus();
            } finally {
                busy = false;
                Array.from(deleteForm.querySelectorAll('button')).forEach(button => { button.disabled = false; });
            }
        });
        if (!navigate) form.addEventListener('submit', event => { event.preventDefault(); filter(); });
    }
    window.ResourceManagement = { attach };
    document.querySelectorAll('.resource-overview').forEach(root => attach(root));
}());
