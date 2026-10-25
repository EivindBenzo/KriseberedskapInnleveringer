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
        L: { icon: options => options }
    };
    vm.runInNewContext(read('wwwroot/js/resource-icons.js'), context);
    return context.window.ResourceIcons;
}

test('zoom scaling is bounded with centered anchors and proportional popup offsets for all types', () => {
    const api = icons();
    assert.equal(api.entries.length, 6);
    assert.equal(api.sizeForZoom(12), 48);
    assert.equal(api.sizeForZoom(0), 24);
    assert.equal(api.sizeForZoom(22), 64);
    assert.ok(api.sizeForZoom(10) < api.sizeForZoom(14));
    for (const { type } of api.entries) {
        for (const preview of [true, false]) {
            for (const zoom of [0, 8, 12, 14, 22]) {
                const options = api.markerOptions(type, preview, zoom);
                const size = api.sizeForZoom(zoom);
                assert.equal(options.icon.iconSize[0], size);
                assert.equal(options.icon.iconSize[1], size);
                assert.equal(options.icon.iconAnchor[0], size / 2);
                assert.equal(options.icon.iconAnchor[1], size / 2);
                assert.equal(options.icon.popupAnchor[1], -size / 2);
                assert.equal(options.icon.iconUrl, api.get(type).url);
                assert.equal(options.icon.className.includes('preview'), preview);
            }
        }
    }
});

function node() {
    const handlers = {};
    const classes = new Set();
    return {
        handlers, listenerOptions: {}, children: [], attributes: {}, style: {},
        classList: { add: value => classes.add(value), remove: value => classes.delete(value), contains: value => classes.has(value) },
        appendChild(child) { this.children.push(child); },
        setAttribute(key, value) { this.attributes[key] = value; },
        addEventListener(name, handler, options) { handlers[name] = handler; this.listenerOptions[name] = options; },
        removeEventListener(name) { delete handlers[name]; },
        focus() { this.focused = true; }
    };
}

function controls({ mobile = false, native = false, rejected = false } = {}) {
    const document = node();
    document.body = node();
    document.body.style.overflow = 'auto';
    document.createElement = () => node();
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
            DomEvent: { disableClickPropagation() {}, disableScrollPropagation() {} }
        }
    };
    vm.runInNewContext(read('wwwroot/js/map-controls.js'), context);
    context.window.MapControls.add(map);
    return { document, container, map, added, media, api, button: added[0].element.children[0], legend: added[1].element };
}

test('legend uses all six shared icons, is bottom-left and collapses on mobile', () => {
    const page = controls({ mobile: true });
    assert.equal(page.added[1].options.position, 'bottomleft');
    assert.equal(page.legend.open, false);
    const items = page.legend.children[1].children;
    assert.equal(items.length, 6);
    page.api.entries.forEach((entry, index) => {
        assert.equal(items[index].children[0].src, page.api.get(entry.type).url);
        assert.equal(items[index].children[1].textContent, entry.type);
    });
    page.media.handlers.change({ matches: false });
    assert.equal(page.legend.open, true);
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

test('Map view rescales saved and preview markers and focuses by resource id without changing form coordinates', () => {
    const map = {
        events: {}, zoom: 12,
        setView(position, zoom) { this.position = position; this.zoom = zoom; this.events.zoomend?.(); return this; },
        getZoom() { return this.zoom; },
        on(name, callback) { this.events[name] = callback; }
    };
    const markers = [];
    const elements = {};
    const document = {
        createElement: () => ({ textContent: '', get innerHTML() { return this.textContent; } }),
        getElementById(id) { return elements[id] ??= Object.assign(node(), { value: id === 'resourceType' ? 'Drone' : 'original' }); },
        querySelectorAll: () => [],
        addEventListener() {}
    };
    const api = icons();
    const context = {
        document,
        window: { location: { search: '?focus=42' } },
        URLSearchParams,
        MapControls: { add() {} },
        resourceMarkerOptions: api.markerOptions,
        L: {
            map: () => map,
            tileLayer: () => ({ addTo() {} }),
            marker(position, options) {
                const marker = {
                    options, position,
                    addTo() { return this; },
                    bindPopup() { return this; },
                    openPopup() { this.opened = true; return this; },
                    setIcon(icon) { this.options.icon = icon; },
                    getLatLng() { return this.position; }
                };
                markers.push(marker);
                return marker;
            }
        }
    };
    const view = read('Views/Home/Map.cshtml');
    const script = view.slice(view.lastIndexOf('<script>') + 8, view.lastIndexOf('</script>'))
        .replace('const startLat = @startLat;', 'const startLat = 58;')
        .replace('const startLng = @startLng;', 'const startLng = 8;')
        .replace(/const savedResources = .*;/, 'const savedResources = [{id: 42, type: "Maskin", lat: 60, lng: 10}];')
        .replace(/const previewLabel = .*;/, 'const previewLabel = "Ny ressurs plasseres her";');
    vm.runInNewContext(script, context);
    assert.equal(map.zoom, 15);
    assert.deepEqual(Array.from(map.position), [60, 10]);
    assert.equal(markers[0].opened, true);
    assert.equal(document.getElementById('latitude').value, 'original');
    map.setView([60, 10], 8);
    for (const marker of markers) assert.equal(marker.options.icon.iconSize[0], api.sizeForZoom(8));
    elements.resourceType.value = 'Generator';
    elements.resourceType.handlers.change();
    assert.equal(markers[1].options.icon.iconUrl, api.get('Generator').url);
    assert.equal(markers[1].options.icon.iconSize[0], api.sizeForZoom(8));
    assert.match(view, /asp-action="@\(isEditing \? "Edit" : "Map"\)" asp-route-id="@ViewBag.EditId"/);
    assert.match(view, /isEditing \? "Lagre endringer" : "Send inn"/);
});
