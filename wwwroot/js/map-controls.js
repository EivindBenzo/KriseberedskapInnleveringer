(function () {
    'use strict';

    window.MapControls = {
        add(map, { onTypeChange } = {}) {
            const container = map.getContainer();
            const doc = container.ownerDocument;
            let fallback = false;
            let previousOverflow;
            let active = false;
            let pending = false;
            const cleanups = [];

            function listen(target, name, handler, options) {
                target.addEventListener(name, handler, options);
                cleanups.push(() => target.removeEventListener(name, handler, options));
            }

            const fullscreen = L.control({ position: 'bottomright' });
            const button = doc.createElement('button');
            button.type = 'button';
            button.className = 'map-fullscreen-button';
            button.setAttribute('aria-controls', container.id);
            const svg = doc.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('viewBox', '0 0 24 24');
            svg.setAttribute('width', '24');
            svg.setAttribute('height', '24');
            svg.setAttribute('aria-hidden', 'true');
            svg.setAttribute('focusable', 'false');
            svg.setAttribute('fill', 'none');
            svg.setAttribute('stroke', 'currentColor');
            svg.setAttribute('stroke-width', '2');
            svg.setAttribute('stroke-linecap', 'square');
            svg.setAttribute('stroke-linejoin', 'miter');
            const corners = doc.createElementNS('http://www.w3.org/2000/svg', 'path');
            svg.appendChild(corners);
            button.appendChild(svg);

            function refresh() {
                const next = fallback || doc.fullscreenElement === container;
                button.setAttribute('aria-pressed', String(next));
                button.setAttribute('aria-label', next ? 'Avslutt fullskjerm' : 'Vis kart i fullskjerm');
                button.title = next ? 'Avslutt fullskjerm (Esc)' : 'Vis kart i fullskjerm';
                corners.setAttribute('d', next
                    ? 'M3 9H9V3 M15 3V9H21 M21 15H15V21 M9 21V15H3'
                    : 'M9 3H3V9 M15 3H21V9 M21 15V21H15 M9 21H3V15');
                if (active !== next) {
                    active = next;
                    requestAnimationFrame(() => map.invalidateSize({ pan: false }));
                    if (!next) button.focus({ preventScroll: true });
                }
            }

            function enterFallback() {
                fallback = true;
                previousOverflow = doc.body.style.overflow;
                doc.body.style.overflow = 'hidden';
                container.classList.add('map-fullscreen-fallback');
                refresh();
            }

            function leaveFallback() {
                fallback = false;
                container.classList.remove('map-fullscreen-fallback');
                doc.body.style.overflow = previousOverflow;
                refresh();
            }

            async function toggle() {
                if (pending) return;
                if (fallback) {
                    leaveFallback();
                    return;
                }
                pending = true;
                try {
                    if (doc.fullscreenElement === container) {
                        await doc.exitFullscreen();
                    } else if (container.requestFullscreen && doc.fullscreenEnabled !== false) {
                        try {
                            await container.requestFullscreen();
                        } catch {
                            enterFallback();
                        }
                    } else {
                        enterFallback();
                    }
                } catch {
                    // A browser can reject exiting while it is already leaving fullscreen.
                } finally {
                    pending = false;
                    refresh();
                }
            }

            fullscreen.onAdd = function () {
                const control = doc.createElement('div');
                control.className = 'leaflet-bar map-fullscreen-control';
                control.appendChild(button);
                L.DomEvent.disableClickPropagation(control);
                L.DomEvent.disableScrollPropagation(control);
                return control;
            };
            fullscreen.addTo(map);
            listen(button, 'click', toggle);
            listen(doc, 'fullscreenchange', refresh);
            listen(doc, 'keydown', event => {
                if (event.key === 'Escape' && fallback) {
                    event.preventDefault();
                    leaveFallback();
                }
            }, true);
            refresh();

            const legend = L.control({ position: 'bottomleft' });
            const media = window.matchMedia('(max-width: 600px)');
            const filters = new Map();
            function setTypeVisible(type, visible) {
                const filter = filters.get(type);
                if (!filter) return;
                const next = Boolean(visible);
                filter.checkbox.checked = next;
                if (filter.visible === next) return;
                filter.visible = next;
                if (onTypeChange) onTypeChange(type, next);
            }
            let details;
            legend.onAdd = function () {
                details = doc.createElement('details');
                details.className = 'map-legend';
                details.open = !media.matches;
                const summary = doc.createElement('summary');
                summary.textContent = 'Ressurstyper';
                details.appendChild(summary);
                const list = doc.createElement('ul');
                window.ResourceIcons.entries.forEach(entry => {
                    const resource = window.ResourceIcons.get(entry.type);
                    const item = doc.createElement('li');
                    const label = doc.createElement('label');
                    const checkbox = doc.createElement('input');
                    checkbox.type = 'checkbox';
                    checkbox.checked = true;
                    filters.set(entry.type, { checkbox, visible: true });
                    listen(checkbox, 'change', () => setTypeVisible(entry.type, checkbox.checked));
                    label.appendChild(checkbox);
                    const image = doc.createElement('img');
                    image.src = resource.url;
                    image.alt = '';
                    image.width = 28;
                    image.height = 28;
                    label.appendChild(image);
                    label.appendChild(doc.createTextNode(resource.label));
                    item.appendChild(label);
                    list.appendChild(item);
                });
                details.appendChild(list);
                L.DomEvent.disableClickPropagation(details);
                L.DomEvent.disableScrollPropagation(details);
                return details;
            };
            legend.addTo(map);
            if (media.addEventListener) {
                listen(media, 'change', event => { details.open = !event.matches; });
            }
            map.on('unload', function () {
                if (fallback) leaveFallback();
                cleanups.forEach(cleanup => cleanup());
            });
            return { setTypeVisible };
        }
    };
}());
