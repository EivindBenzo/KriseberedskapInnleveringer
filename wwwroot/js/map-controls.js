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
                    return;
                }
                if (event.key?.toLowerCase() !== 'f' || event.repeat || event.defaultPrevented
                    || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey
                    || event.target?.isContentEditable
                    || event.target?.closest?.('input, textarea, select, [contenteditable], [role="textbox"], [role="combobox"]')
                    || doc.querySelector?.('dialog[open], [role="dialog"]:not([hidden]), [aria-modal="true"]:not([hidden])')
                    || (doc.fullscreenElement && doc.fullscreenElement !== container)
                    || container.getClientRects?.().length === 0) return;
                event.preventDefault();
                toggle();
            }, true);
            refresh();

            const legend = L.control({ position: 'bottomleft' });
            const media = window.matchMedia('(max-width: 600px)');
            const filters = new Map();
            let bulkToggle;
            function anyTypeVisible() {
                return Array.from(filters.values()).some(filter => filter.visible);
            }
            function setTypeVisible(type, visible) {
                const filter = filters.get(type);
                if (!filter) return;
                const next = Boolean(visible);
                filter.checkbox.checked = next;
                const changed = filter.visible !== next;
                filter.visible = next;
                bulkToggle.textContent = anyTypeVisible() ? 'Unselect all' : 'Select all';
                if (changed && onTypeChange) onTypeChange(type, next);
            }
            let details;
            let list;
            let dragHandle;
            let resizeHandle;
            let gesture;
            function bounds() {
                return container.getBoundingClientRect();
            }
            function movePanel(left, top) {
                const area = bounds();
                const panel = details.getBoundingClientRect();
                if (details.parentNode !== container) container.appendChild(details);
                details.classList.add('map-legend-positioned');
                details.style.left = Math.max(0, Math.min(left, Math.max(0, area.width - panel.width))) + 'px';
                details.style.top = Math.max(0, Math.min(top, Math.max(0, area.height - panel.height))) + 'px';
            }
            function resizePanel(height) {
                const area = bounds();
                const panel = details.getBoundingClientRect();
                const current = list.getBoundingClientRect().height;
                const available = Math.max(0, Math.min(360, area.height - (panel.height - current)));
                const next = Math.max(Math.min(28, available), Math.min(height, available));
                list.style.height = next + 'px';
                list.style.maxHeight = next + 'px';
                resizeHandle.setAttribute('aria-description', 'Listehøyde: ' + Math.round(next) + ' piksler');
                movePanel(panel.left - area.left, panel.top - area.top + current - next);
            }
            function addHandle(handle, mode) {
                handle.type = 'button';
                handle.className = 'map-legend-' + mode;
                handle.textContent = mode === 'drag' ? 'Flytt' : 'Endre høyde';
                handle.setAttribute('aria-label', mode === 'drag'
                    ? 'Flytt ressurstyper med piltastene' : 'Endre høyden på ressurstyper med opp- og nedpil');
                if (mode === 'resize') handle.setAttribute('aria-controls', 'mapLegendTypes');
                listen(handle, 'click', event => { event.preventDefault(); event.stopPropagation(); });
                listen(handle, 'pointerdown', event => {
                    if (event.button !== 0 || event.isPrimary === false) return;
                    event.preventDefault();
                    event.stopPropagation();
                    const panel = details.getBoundingClientRect();
                    const area = bounds();
                    movePanel(panel.left - area.left, panel.top - area.top);
                    gesture = { mode, x: event.clientX, y: event.clientY, left: panel.left - area.left,
                        top: panel.top - area.top, height: list.getBoundingClientRect().height };
                    handle.setPointerCapture(event.pointerId);
                });
                listen(handle, 'pointermove', event => {
                    if (!gesture || gesture.mode !== mode) return;
                    event.preventDefault();
                    if (mode === 'drag') movePanel(gesture.left + event.clientX - gesture.x, gesture.top + event.clientY - gesture.y);
                    else resizePanel(gesture.height + gesture.y - event.clientY);
                });
                const stop = () => { gesture = null; };
                listen(handle, 'pointerup', stop);
                listen(handle, 'pointercancel', stop);
                listen(handle, 'lostpointercapture', stop);
                listen(handle, 'keydown', event => {
                    if (!['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return;
                    event.preventDefault();
                    event.stopPropagation();
                    const step = event.shiftKey ? 40 : 10;
                    if (mode === 'resize') {
                        if (event.key === 'ArrowUp' || event.key === 'ArrowDown')
                            resizePanel(list.getBoundingClientRect().height + (event.key === 'ArrowUp' ? step : -step));
                    } else {
                        const panel = details.getBoundingClientRect();
                        const area = bounds();
                        movePanel(panel.left - area.left + (event.key === 'ArrowRight' ? step : event.key === 'ArrowLeft' ? -step : 0),
                            panel.top - area.top + (event.key === 'ArrowDown' ? step : event.key === 'ArrowUp' ? -step : 0));
                    }
                });
            }
            legend.onAdd = function () {
                details = doc.createElement('details');
                details.className = 'map-legend';
                details.open = !media.matches;
                const summary = doc.createElement('summary');
                summary.textContent = 'Ressurstyper';
                const handles = doc.createElement('span');
                handles.className = 'map-legend-handles';
                dragHandle = doc.createElement('button');
                resizeHandle = doc.createElement('button');
                addHandle(dragHandle, 'drag');
                addHandle(resizeHandle, 'resize');
                handles.appendChild(dragHandle);
                handles.appendChild(resizeHandle);
                summary.appendChild(handles);
                details.appendChild(summary);
                list = doc.createElement('ul');
                list.id = 'mapLegendTypes';
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
                const actions = doc.createElement('div');
                actions.className = 'map-legend-actions';
                bulkToggle = doc.createElement('button');
                bulkToggle.type = 'button';
                bulkToggle.textContent = 'Unselect all';
                listen(bulkToggle, 'click', () => {
                    const visible = !anyTypeVisible();
                    filters.forEach((_, type) => setTypeVisible(type, visible));
                });
                actions.appendChild(bulkToggle);
                details.appendChild(actions);
                L.DomEvent.disableClickPropagation(details);
                L.DomEvent.disableScrollPropagation(details);
                return details;
            };
            legend.addTo(map);
            if (media.addEventListener) {
                listen(media, 'change', event => { details.open = !event.matches; });
            }
            function keepInMap() {
                if (!details.classList.contains('map-legend-positioned')) return;
                resizePanel(list.getBoundingClientRect().height);
                const panel = details.getBoundingClientRect();
                const area = bounds();
                movePanel(panel.left - area.left, panel.top - area.top);
            }
            if (window.addEventListener) listen(window, 'resize', keepInMap);
            map.on('resize', keepInMap);
            map.on('unload', function () {
                if (fallback) leaveFallback();
                cleanups.forEach(cleanup => cleanup());
                if (details.parentNode === container) details.remove();
            });
            return { setTypeVisible };
        }
    };
}());
