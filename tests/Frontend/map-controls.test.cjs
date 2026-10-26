const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');

const root = resolve(__dirname, '../..');
const read = path => readFileSync(resolve(root, path), 'utf8');

function icons() {
    const context = {
        document: { currentScript: { dataset: { iconBase: '/images/resources/' } }, querySelectorAll: () => [] },
        window: {},
        L: { divIcon: options => options }
    };
    vm.runInNewContext(read('wwwroot/js/resource-icons.js'), context);
    return context.window.ResourceIcons;
}

test('zoom scaling keeps the upward pointer tip anchored and a 44px keyboard hit target for all types', () => {
    const api = icons();
    assert.equal(api.entries.length, 6);
    assert.equal(api.sizeForZoom(12), 32);
    assert.equal(api.sizeForZoom(0), 28);
    assert.equal(api.sizeForZoom(22), 36);
    assert.ok(api.sizeForZoom(10) < api.sizeForZoom(14));
    for (const { type } of api.entries) {
        for (const preview of [true, false]) {
            for (const zoom of [0, 8, 12, 14, 22]) {
                const options = api.markerOptions(type, preview, zoom);
                const size = api.sizeForZoom(zoom);
                assert.equal(options.icon.iconSize[0], 44);
                assert.equal(options.icon.iconSize[1], Math.max(44, size + 12));
                assert.equal(options.icon.iconAnchor[0], 22);
                assert.equal(options.icon.iconAnchor[1], options.icon.iconSize[1] - 8);
                assert.equal(options.icon.popupAnchor[1], 4 - options.icon.iconSize[1]);
                assert.match(options.icon.html, new RegExp(`width="${size}" height="${size}"`));
                assert.equal(options.keyboard, true);
                assert.equal(options.icon.iconUrl, api.get(type, preview ? null : {}, preview).url);
                assert.equal(options.icon.className.includes('preview'), preview);
            }
        }
    }
});

test('only map markers have an upward pointer with its tip at the geographic anchor', () => {
    const css = read('wwwroot/css/map-controls.css');
    assert.match(css, /\.resource-marker::before\s*\{[^}]*bottom: 0;[^}]*left: 15px;[^}]*border-left: 7px solid transparent;[^}]*border-right: 7px solid transparent;[^}]*border-bottom: 8px solid #000;/);
    assert.match(css, /\.resource-marker > img\s*\{[^}]*margin: 2px auto 0;/);
    assert.match(css, /\.resource-marker:focus-visible/);
    assert.doesNotMatch(css, /(?:map-legend|data-resource-icon)[^{]*::(?:before|after)/);
});

function node(tagName) {
    const handlers = {};
    const classes = new Set();
    return {
        tagName, handlers, listenerOptions: {}, children: [], attributes: {}, style: {},
        classList: { add: value => classes.add(value), remove: value => classes.delete(value), contains: value => classes.has(value) },
        appendChild(child) {
            function releaseCapture(element) {
                if (element.capturedPointer !== undefined) {
                    delete element.capturedPointer;
                    element.handlers.lostpointercapture?.();
                }
                element.children?.forEach(releaseCapture);
            }
            releaseCapture(child);
            if (child.parentNode) child.parentNode.children = child.parentNode.children.filter(item => item !== child);
            this.children.push(child);
            child.parentNode = this;
        },
        getBoundingClientRect() {
            const list = this.children.find(child => child.tagName === 'ul');
            return { left: parseFloat(this.style.left) || 0, top: parseFloat(this.style.top) || 0,
                width: tagName === 'details' ? 200 : 800,
                height: parseFloat(this.style.height) || (list ? list.getBoundingClientRect().height + 100 : tagName === 'ul' ? 180 : 600) };
        },
        setPointerCapture(id) { this.capturedPointer = id; },
        remove() { this.parentNode.children = this.parentNode.children.filter(child => child !== this); this.parentNode = null; },
        setAttribute(key, value) { this.attributes[key] = value; },
        addEventListener(name, handler, options) { handlers[name] = handler; this.listenerOptions[name] = options; },
        removeEventListener(name) { delete handlers[name]; },
        focus() { this.focused = true; }
    };
}

function controls({ mobile = false, native = false, rejected = false, onTypeChange } = {}) {
    const document = node();
    document.body = node();
    document.body.style.overflow = 'auto';
    document.createElement = tagName => node(tagName);
    document.createElementNS = (namespace, tagName) => Object.assign(node(tagName), { namespace });
    document.createTextNode = text => ({ textContent: text });
    document.querySelector = () => document.modal || null;
    const container = node();
    container.id = 'map';
    container.ownerDocument = document;
    if (native) {
        container.requestFullscreen = async () => {
            if (rejected) throw new Error('Denied');
            document.fullscreenElement = container;
            document.handlers.fullscreenchange();
        };
        document.exitFullscreen = async () => {
            document.fullscreenElement = null;
            document.handlers.fullscreenchange();
        };
    }
    const added = [];
    const map = {
        events: {}, invalidations: 0,
        getContainer: () => container,
        invalidateSize() { this.invalidations++; },
        on(name, handler) { this.events[name] = handler; }
    };
    const media = Object.assign(node(), { matches: mobile });
    const api = icons();
    const context = {
        window: { ResourceIcons: api, matchMedia: () => media },
        requestAnimationFrame: callback => callback(),
        L: {
            control(options) {
                return { addTo(map) { added.push({ options, element: this.onAdd(map) }); } };
            },
            DomEvent: {
                disableClickPropagation(element) {
                    for (const name of ['click', 'dblclick', 'mousedown', 'touchstart']) {
                        element.addEventListener(name, event => event.stopPropagation());
                    }
                },
                disableScrollPropagation(element) {
                    element.addEventListener('wheel', event => event.stopPropagation());
                }
            }
        }
    };
    vm.runInNewContext(read('wwwroot/js/map-controls.js'), context);
    const controller = context.window.MapControls.add(map, { onTypeChange });
    return { document, container, map, added, media, api, controller, button: added[0].element.children[0], legend: added[1].element };
}

test('legend uses all six shared icons, is bottom-left and collapses on mobile', () => {
    const page = controls({ mobile: true });
    assert.equal(page.added[1].options.position, 'bottomleft');
    assert.equal(page.legend.open, false);
    const items = page.legend.children[1].children;
    assert.equal(items.length, 6);
    page.api.entries.forEach((entry, index) => {
        const label = items[index].children[0];
        assert.equal(label.tagName, 'label');
        assert.equal(label.children[0].tagName, 'input');
        assert.equal(label.children[0].type, 'checkbox');
        assert.equal(label.children[0].checked, true);
        assert.equal(label.children[0].handlers.keydown, undefined, 'native checkbox keyboard handling is retained');
        assert.equal(label.children[1].src, page.api.get(entry.type).url);
        assert.equal(label.children[1].alt, '');
        assert.equal(label.children[2].textContent, page.api.get(entry.type).label);
    });
    page.media.handlers.change({ matches: false });
    assert.equal(page.legend.open, true);
});

test('legend changes and public API synchronize six checkboxes and callbacks without duplicate changes', () => {
    const changes = [];
    const page = controls({ onTypeChange: (...args) => changes.push(args) });
    assert.deepEqual(changes, [], 'all types start visible without emitting changes');
    page.api.entries.forEach(({ type }, index) => {
        const checkbox = page.legend.children[1].children[index].children[0].children[0];
        checkbox.checked = false;
        checkbox.handlers.change();
        assert.deepEqual(changes.at(-1), [type, false]);
        page.controller.setTypeVisible(type, true);
        assert.equal(checkbox.checked, true);
        assert.deepEqual(changes.at(-1), [type, true]);
        page.controller.setTypeVisible(type, true);
    });
    assert.equal(changes.length, 12);
    page.controller.setTypeVisible('unknown', false);
    assert.equal(changes.length, 12);
    const noCallback = controls();
    noCallback.controller.setTypeVisible(noCallback.api.entries[0].type, false);
});

test('legend and fullscreen controls suppress map click, double-click and scroll without cancelling native defaults', () => {
    const page = controls();
    for (const control of [page.legend, page.added[0].element]) {
        for (const name of ['click', 'dblclick', 'wheel']) {
            let stopped = false;
            control.handlers[name]({ stopPropagation() { stopped = true; } });
            assert.equal(stopped, true, name);
        }
    }
});

test('fullscreen button has a local decorative four-corner SVG and accurate Norwegian accessible state', async () => {
    const page = controls();
    const svg = page.button.children[0];
    assert.equal(svg.tagName, 'svg');
    assert.equal(svg.namespace, 'http://www.w3.org/2000/svg');
    assert.equal(svg.attributes.viewBox, '0 0 24 24');
    assert.equal(svg.attributes['aria-hidden'], 'true');
    assert.equal(svg.attributes.focusable, 'false');
    assert.equal(svg.attributes.stroke, 'currentColor');
    assert.equal(svg.children[0].tagName, 'path');
    const expand = svg.children[0].attributes.d;
    assert.equal((expand.match(/M/g) || []).length, 4);
    assert.equal(page.button.attributes['aria-label'], 'Vis kart i fullskjerm');
    assert.equal(page.button.title, 'Vis kart i fullskjerm');
    await page.button.handlers.click();
    assert.notEqual(svg.children[0].attributes.d, expand);
    assert.equal((svg.children[0].attributes.d.match(/M/g) || []).length, 4);
    assert.equal(page.button.attributes['aria-label'], 'Avslutt fullskjerm');
    assert.equal(page.button.title, 'Avslutt fullskjerm (Esc)');
    await page.button.handlers.click();
    assert.equal(svg.children[0].attributes.d, expand);
    assert.equal(page.button.attributes['aria-label'], 'Vis kart i fullskjerm');
});

test('filter state persists through native and fallback fullscreen transitions, zoom and mobile collapse', async () => {
    for (const native of [false, true]) {
        const changes = [];
        const page = controls({ native, onTypeChange: (...args) => changes.push(args) });
        const type = page.api.entries[0].type;
        const checkbox = page.legend.children[1].children[0].children[0].children[0];
        page.controller.setTypeVisible(type, false);
        await page.button.handlers.click();
        assert.equal(checkbox.checked, false);
        page.map.events.zoomend?.();
        page.media.handlers.change({ matches: true });
        assert.equal(page.legend.open, false);
        if (native) await page.document.exitFullscreen();
        else page.document.handlers.keydown({ key: 'Escape', preventDefault() {} });
        page.media.handlers.change({ matches: false });
        assert.equal(checkbox.checked, false);
        assert.equal(page.legend.open, true);
        assert.deepEqual(changes, [[type, false]]);
        await page.button.handlers.click();
        page.controller.setTypeVisible(type, true);
        await page.button.handlers.click();
        assert.equal(checkbox.checked, true);
        assert.deepEqual(changes, [[type, false], [type, true]]);
    }
});

test('larger fullscreen controls are scoped to native and fallback selectors with bounded mobile legend', () => {
    const css = read('wwwroot/css/map-controls.css');
    for (const selector of [
        '.map-fullscreen-button', '.map-fullscreen-button svg', '.map-legend',
        '.map-legend summary', '.map-legend ul', '.map-legend label', '.map-legend img', '.map-legend input'
    ]) {
        assert.ok(css.includes(`#map:fullscreen ${selector},\n#map.map-fullscreen-fallback ${selector} {`), selector);
    }
    assert.match(css, /\.map-fullscreen-button svg\s*\{[^}]*width: 24px;[^}]*height: 24px;/);
    assert.match(css, /#map:fullscreen \.map-fullscreen-button svg,[^}]*width: 32px;[^}]*height: 32px;/);
    assert.match(css, /#map:fullscreen \.map-legend,[^}]*font-size: 1\.125rem;/);
    assert.match(css, /\.map-legend ul\s*\{[^}]*max-height: min\(240px, 45dvh\);[^}]*overflow-y: auto;/);
    assert.match(css, /#map:fullscreen \.map-legend ul,[^}]*max-height: min\(360px, 45dvh\);/);
    assert.match(css, /@media \(max-width: 600px\)[\s\S]*#map:fullscreen \.map-legend,[\s\S]*max-width: min\(280px, calc\(100vw - 110px\)\);/);
});

test('fullscreen fallback restores page scrolling, focus, aria and map dimensions on Escape', async () => {
    const page = controls();
    assert.equal(page.added[0].options.position, 'bottomright');
    assert.equal(page.button.type, 'button');
    assert.equal(page.button.attributes['aria-controls'], 'map');
    assert.equal(page.button.attributes['aria-pressed'], 'false');
    assert.equal(page.document.listenerOptions.keydown, true, 'Escape must run before Leaflet consumes it');
    await page.button.handlers.click();
    assert.equal(page.container.classList.contains('map-fullscreen-fallback'), true);
    assert.equal(page.document.body.style.overflow, 'hidden');
    assert.equal(page.button.attributes['aria-pressed'], 'true');
    let prevented = false;
    page.document.handlers.keydown({ key: 'Escape', preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.equal(page.container.classList.contains('map-fullscreen-fallback'), false);
    assert.equal(page.document.body.style.overflow, 'auto');
    assert.equal(page.button.attributes['aria-pressed'], 'false');
    assert.equal(page.button.focused, true);
    assert.equal(page.map.invalidations, 2);
});

test('native fullscreen changes and browser Escape update controls; denied native requests fall back', async () => {
    const page = controls({ native: true });
    await page.button.handlers.click();
    assert.equal(page.document.fullscreenElement, page.container);
    assert.equal(page.button.attributes['aria-pressed'], 'true');
    await page.document.exitFullscreen();
    assert.equal(page.button.attributes['aria-pressed'], 'false');
    assert.equal(page.map.invalidations, 2);
    await page.button.handlers.click();
    await page.button.handlers.click();
    assert.equal(page.document.fullscreenElement, null);
    const denied = controls({ native: true, rejected: true });
    await denied.button.handlers.click();
    assert.equal(denied.container.classList.contains('map-fullscreen-fallback'), true);
    denied.map.events.unload();
    assert.equal(denied.document.body.style.overflow, 'auto');
    assert.equal(denied.document.handlers.keydown, undefined);
});

test('f toggles map fullscreen, ignores repeats, typing, modifiers and open dialogs, leaving native Escape alone', async () => {
    for (const native of [false, true]) {
        const page = controls({ native });
        const press = overrides => {
            let prevented = false;
            page.document.handlers.keydown({ key: 'f', preventDefault() { prevented = true; }, ...overrides });
            return prevented;
        };
        for (const event of [{ repeat: true }, { ctrlKey: true }, { altKey: true }, { metaKey: true },
            { shiftKey: true }, { defaultPrevented: true }, { target: { isContentEditable: true } },
            { target: { closest: () => ({ tagName: 'INPUT' }) } }]) {
            assert.equal(press(event), false);
            assert.equal(page.button.attributes['aria-pressed'], 'false');
        }
        page.document.modal = {};
        assert.equal(press(), false);
        page.document.modal = null;
        assert.equal(press(), true);
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(page.button.attributes['aria-pressed'], 'true');
        if (native) {
            assert.equal(press({ key: 'Escape' }), false);
            assert.equal(page.document.fullscreenElement, page.container);
        }
        assert.equal(press(), true);
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(page.button.attributes['aria-pressed'], 'false');
    }
});

test('single bulk toggle uses exact dynamic labels for checkbox and programmatic selection', () => {
    const changes = [];
    const page = controls({ onTypeChange: (...args) => changes.push(args) });
    const actions = page.legend.children[2].children;
    assert.equal(actions.length, 1);
    const toggle = actions[0];
    assert.equal(toggle.textContent, 'Unselect all');
    toggle.handlers.click();
    assert.equal(toggle.textContent, 'Select all');
    assert.equal(changes.length, 6);
    page.legend.children[1].children.forEach(item => assert.equal(item.children[0].children[0].checked, false));
    page.controller.setTypeVisible(page.api.entries[0].type, true);
    assert.equal(toggle.textContent, 'Unselect all', 'focusResource uses this API to reveal its type');
    page.controller.setTypeVisible(page.api.entries[0].type, true);
    assert.equal(changes.length, 7, 'duplicate visibility updates emit no duplicate callbacks');
    const checkbox = page.legend.children[1].children[0].children[0].children[0];
    checkbox.checked = false;
    checkbox.handlers.change();
    assert.equal(toggle.textContent, 'Select all');
    toggle.handlers.click();
    assert.equal(toggle.textContent, 'Unselect all');
    assert.equal(changes.length, 14);
    page.legend.children[1].children.forEach(item => assert.equal(item.children[0].children[0].checked, true));
});

test('first pointer drag and resize reparent before capture and preserve the gesture on later moves', () => {
    for (const mode of ['drag', 'resize']) {
        const page = controls();
        const handle = page.legend.children[0].children[0].children[mode === 'drag' ? 0 : 1];
        const capture = handle.setPointerCapture;
        handle.setPointerCapture = function (id) {
            assert.equal(page.legend.parentNode, page.container, 'reparent before pointer capture');
            capture.call(this, id);
        };
        const event = values => ({ button: 0, pointerId: 3, clientX: 0, clientY: 0,
            preventDefault() {}, stopPropagation() {}, ...values });
        handle.handlers.pointerdown(event());
        handle.handlers.pointermove(event({ clientX: 20, clientY: 20 }));
        handle.handlers.pointermove(event({ clientX: 40, clientY: 40 }));
        assert.equal(handle.capturedPointer, 3);
        if (mode === 'drag') assert.equal(page.legend.style.left, '40px');
        else assert.equal(page.legend.children[1].style.height, '140px');
    }
});

test('legend top handles support bounded pointer drag, resize and keyboard adjustments with cleanup', () => {
    const page = controls();
    const [drag, resize] = page.legend.children[0].children[0].children;
    assert.match(drag.attributes['aria-label'], /piltastene/);
    assert.equal(resize.attributes['aria-controls'], page.legend.children[1].id);
    const event = values => ({ button: 0, pointerId: 1, clientX: 0, clientY: 0,
        preventDefault() {}, stopPropagation() {}, ...values });
    drag.handlers.pointerdown(event());
    assert.equal(drag.capturedPointer, 1);
    drag.handlers.pointermove(event({ clientX: 900, clientY: 900 }));
    drag.handlers.pointerup();
    assert.equal(page.legend.parentNode, page.container);
    assert.equal(page.legend.style.left, '600px');
    assert.equal(page.legend.style.top, '320px');
    drag.handlers.keydown(event({ key: 'ArrowLeft' }));
    assert.equal(page.legend.style.left, '590px');
    resize.handlers.pointerdown(event({ clientY: 200 }));
    resize.handlers.pointermove(event({ clientY: 300 }));
    resize.handlers.pointerup();
    const list = page.legend.children[1];
    assert.equal(list.style.height, '80px');
    assert.equal(page.legend.style.top, '420px', 'top handle keeps the lower edge stable');
    resize.handlers.keydown(event({ key: 'ArrowUp' }));
    assert.equal(list.style.height, '90px');
    resize.handlers.pointerdown(event());
    resize.handlers.pointercancel();
    resize.handlers.pointermove(event({ clientY: -900 }));
    assert.equal(list.style.height, '90px');
    page.map.events.unload();
    assert.equal(page.legend.parentNode, null);
    assert.equal(drag.handlers.pointerdown, undefined);
});
