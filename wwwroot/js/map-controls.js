(function () {
    'use strict';

    window.MapControls = {
        add(map) {
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

            function refresh() {
                const next = fallback || doc.fullscreenElement === container;
                button.setAttribute('aria-pressed', String(next));
                button.setAttribute('aria-label', next ? 'Avslutt fullskjerm' : 'Vis kart i fullskjerm');
                button.title = next ? 'Avslutt fullskjerm (Esc)' : 'Vis kart i fullskjerm';
                button.textContent = next ? 'Avslutt fullskjerm' : 'Fullskjerm';
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
                    const image = doc.createElement('img');
                    image.src = resource.url;
                    image.alt = '';
                    image.width = 28;
                    image.height = 28;
                    item.appendChild(image);
                    item.appendChild(doc.createTextNode(resource.label));
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
        }
    };
}());
