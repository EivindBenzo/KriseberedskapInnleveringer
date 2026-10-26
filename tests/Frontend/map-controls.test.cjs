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
        appendChild(child) { this.children.push(child); },
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
