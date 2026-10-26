(function () {
    'use strict';

    window.ResourcesOverlay = {
        initialize({ onFocus, onRegister }) {
            const opener = document.getElementById('resourcesOverlayOpen');
            const dialog = document.getElementById('resourcesDialog');
            const content = document.getElementById('resourcesDialogContent');
            const back = document.getElementById('resourcesDialogBack');
            if (!dialog.showModal) return;
            let controller;
            let previousFocus;
            let afterClose;

            function close(action) {
                afterClose = action;
                dialog.close();
            }

            async function load(url, focusResults = false) {
                controller?.abort();
                controller = new AbortController();
                const request = controller;
                const endpoint = new URL(url, window.location.href);
                endpoint.searchParams.set('overlay', 'true');
                content.replaceChildren();
                const loading = document.createElement('p');
                loading.setAttribute('role', 'status');
                loading.textContent = 'Laster ressurser …';
                content.append(loading);
                content.setAttribute('aria-busy', 'true');
                try {
                    const response = await fetch(endpoint, { signal: request.signal, credentials: 'same-origin' });
                    if (!response.ok || response.redirected) throw new Error('Resources unavailable');
                    const html = await response.text();
                    if (request.signal.aborted || !dialog.open) return;
                    const template = document.createElement('template');
                    template.innerHTML = html;
                    if (!template.content.querySelector('.resource-overview') ||
                        template.content.querySelector('script, iframe')) throw new Error('Unexpected response');
                    content.replaceChildren(template.content);
                    window.ResourceIcons.initializeImages(content);
                    content.scrollTop = 0;
                    if (focusResults) content.focus();
                } catch {
                    if (request.signal.aborted || !dialog.open) return;
                    const error = document.createElement('p');
                    error.setAttribute('role', 'alert');
                    error.textContent = 'Kunne ikke laste ressursene. Prøv igjen eller åpne ressursoversikten i en ny fane.';
                    const retry = document.createElement('button');
                    retry.type = 'button';
                    retry.className = 'btn-secondary';
                    retry.textContent = 'Prøv igjen';
                    retry.addEventListener('click', () => load(url, true));
                    const fallback = document.createElement('a');
                    endpoint.searchParams.delete('overlay');
                    fallback.href = endpoint.href;
                    fallback.target = '_blank';
                    fallback.rel = 'noopener';
                    fallback.textContent = 'Åpne ressursoversikten (ny fane)';
                    content.replaceChildren(error, retry, fallback);
                } finally {
                    if (controller === request) content.removeAttribute('aria-busy');
                }
            }

            opener.addEventListener('click', event => {
                if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button > 0) return;
                event.preventDefault();
                previousFocus = document.activeElement;
                dialog.showModal();
                document.documentElement.classList.add('resources-overlay-open');
                back.focus();
                load(opener.href);
            });
            back.addEventListener('click', () => close());
            dialog.addEventListener('close', () => {
                controller?.abort();
                document.documentElement.classList.remove('resources-overlay-open');
                content.replaceChildren();
                (previousFocus?.isConnected ? previousFocus : opener).focus({ preventScroll: true });
                const action = afterClose;
                afterClose = null;
                action?.();
            });
            // Native showModal makes the background inert; keep Tab within the dialog at both ends.
            dialog.addEventListener('keydown', event => {
                if (event.key !== 'Tab') return;
                const focusable = Array.from(dialog.querySelectorAll('a[href], button, input, select, textarea, summary, [tabindex="0"]'))
                    .filter(element => !element.disabled && element.getClientRects().length > 0);
                const first = focusable[0];
                const last = focusable[focusable.length - 1];
                if (event.shiftKey && (document.activeElement === first || document.activeElement === content)) {
                    event.preventDefault();
                    last?.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                    event.preventDefault();
                    first?.focus();
                }
            });
            content.addEventListener('click', event => {
                const link = event.target.closest('a');
                if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button > 0) return;
                if (link.hasAttribute('data-resources-link')) {
                    event.preventDefault();
                    load(link.href, true);
                } else if (link.hasAttribute('data-map-focus')) {
                    const lat = Number(link.dataset.lat);
                    const lng = Number(link.dataset.lng);
                    if (!Number.isFinite(lat) || !Number.isFinite(lng) || Math.abs(lat) > 90 || Math.abs(lng) > 180) return;
                    event.preventDefault();
                    close(() => onFocus(link.dataset.mapFocus, lat, lng));
                } else if (link.hasAttribute('data-map-back') || link.hasAttribute('data-map-register')) {
                    event.preventDefault();
                    close(link.hasAttribute('data-map-register') ? onRegister : null);
                }
            });
            content.addEventListener('submit', event => {
                const form = event.target;
                if (!form.matches('.filter-bar')) return;
                event.preventDefault();
                const url = new URL(form.action, window.location.href);
                url.search = new URLSearchParams(new FormData(form)).toString();
                load(url, true);
            });
        }
    };
}());
