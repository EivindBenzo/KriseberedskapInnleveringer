const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');

const root = resolve(__dirname, '../..');
const read = path => readFileSync(resolve(root, path), 'utf8');
function element(value = '') {
    const classes = new Set();
    return {
        value, textContent: '', handlers: {}, children: [], dataset: {}, attributes: {},
        setAttribute(name, value) { this.attributes[name] = value; },
        removeAttribute(name) { delete this.attributes[name]; },
        get firstChild() { return this.children[0]; },
        focus() {},
        classList: { add: c => classes.add(c), remove: c => classes.delete(c) },
        addEventListener(name, callback) { this.handlers[name] = callback; },
        appendChild(child) { this.children.push(child); },
        contains() { return false; },
        get innerHTML() { return this.textContent; },
        set innerHTML(value) { this.children = []; this.textContent = value; },
        scrollIntoView() {}
    };
}

function page({ selected = false, editId = null, focus = '', register = false, placement = false } = {}) {
    const elements = {};
    const card = element();
    card.dataset = { resourceId: '42', resourceType: 'Maskin' };
    card.focusButton = element();
    card.querySelector = () => card.focusButton;
    card.hidden = false;
    const document = {
        currentScript: { dataset: { iconBase: '/images/resources/' } },
        createElement: () => element(),
        getElementById(id) { return elements[id] ??= element(); },
        querySelectorAll: selector => selector === '.resource-card' ? [card] : [],
        addEventListener() {}
    };
    document.getElementById('resourceType').value = 'Drone';
    document.getElementById('selectedPlace').textContent = selected ? 'Eksisterende sted' : 'Ingen plassering valgt';
    const layers = new Set();
    const map = {
        events: {}, zoom: 12,
        setView(position, zoom) { this.position = position; this.zoom = zoom; this.events.zoomend?.(); return this; },
        setZoom(zoom) { this.zoom = zoom; this.events.zoomend?.(); return this; },
        getZoom() { return this.zoom; },
        on(name, handler) { this.events[name] = handler; },
        removeLayer(marker) { layers.delete(marker); },
        invalidateSize() { this.invalidations = (this.invalidations || 0) + 1; },
        getContainer: () => element()
    };
    const markers = [];
    const requests = [];
    const visibility = new Map();
    let controls;
    const context = {
        document, window: { location: { search: focus ? '?focus=' + focus : register ? '?register=true' : '' } },
        URLSearchParams, AbortController, Map, Set,
        setTimeout: () => 1, clearTimeout() {},
        fetch(url, options) {
            return new Promise(resolve => requests.push({ url, options, resolve }));
        },
        MapControls: { add(map, options) {
            controls = {
                setTypeVisible(type, visible) {
                    visibility.set(type, visible);
                    options.onTypeChange(type, visible);
                }
            };
            return controls;
        } },
        L: {
            icon: options => options,
            map: () => map, tileLayer: () => ({ addTo() {} }),
            marker(position, options) {
                const marker = {
                    options, position,
                    addTo() { layers.add(this); return this; },
                    bindPopup(popup) { this.popup = popup; return this; },
                    openPopup() { this.opened = true; return this; },
                    on(name, handler) { (this.events ??= {})[name] = handler; return this; },
                    setIcon(icon) { this.options.icon = icon; },
                    getLatLng() { return this.position; },
                    setLatLng(position) { this.position = position; return this; }
                };
                markers.push(marker);
                return marker;
            }
        }
    };
    vm.runInNewContext(read('wwwroot/js/resource-icons.js'), context);
    vm.runInNewContext(read('wwwroot/js/place-search.js'), context);
    context.PlaceSearch = context.window.PlaceSearch;
    context.resourceMarkerOptions = context.window.resourceMarkerOptions;
    const view = read('Views/Home/Map.cshtml');
    const script = view.slice(view.lastIndexOf('<script>') + 8, view.lastIndexOf('</script>'))
        .replace('const startLat = @startLat;', 'const startLat = 58;')
        .replace('const startLng = @startLng;', 'const startLng = 8;')
        .replace(/const savedResources = .*;/, 'const savedResources = [{id: 42, name: "Bil", type: "Maskin", lat: 60, lng: 10, availability: "Reservert", verification: "Venter på verifisering", updated: "25.10.2026 12:30 UTC"}];')
        .replace(/const hasSelectedPoint = .*;/, `const hasSelectedPoint = ${selected};`)
        .replace(/const registrationInitiallyOpen = .*;/, `const registrationInitiallyOpen = ${register || selected || editId !== null};`)
        .replace(/const editId = .*;/, `const editId = ${editId};`)
        .replace(/const placeUrl = .*;/, 'const placeUrl = "/Home/Place";')
        .replace(/const searchUrl = .*;/, 'const searchUrl = "/Home/SearchPlaces";')
        .replace(/localitiesUrl: @Json.Serialize.*,$/m, 'localitiesUrl: "/data/norway-localities.json",');
    vm.runInNewContext(script, context);
    if (placement) {
        if (elements.registrationPanel.hidden) elements.registrationToggle.handlers.click();
        elements.placementToggle.handlers.click();
    }
    return { map, markers, elements, layers, controls, visibility, requests, card, api: context.window.ResourceIcons };
}

test('fresh create (including redirect after save) has only saved markers; type/zoom/filter changes never create preview', () => {
    const p = page();
    assert.equal(p.markers.length, 1);
    assert.equal(p.elements.latitude, undefined);
    p.elements.resourceType.handlers.change();
    p.map.setView([60, 10], 8);
    p.controls.setTypeVisible('Maskin', false);
    assert.equal(p.layers.size, 0);
    p.controls.setTypeVisible('Maskin', true);
    assert.equal(p.layers.size, 1);
    assert.equal(p.markers.length, 1);
    assert.equal(p.markers[0].options.icon.iconSize[0], p.api.sizeForZoom(8));
    assert.equal(p.requests.length, 0);
});

test('only explicit placement creates preview; saved marker filters never remove it', () => {
    const p = page({ placement: true });
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    assert.equal(p.markers.length, 2);
    assert.equal(p.elements.latitude.value, 59);
    assert.equal(p.elements.longitude.value, 9);
    assert.equal(p.elements.selectedPlace.textContent, '59.00000, 9.00000');
    assert.equal(p.requests.length, 1);
    p.elements.resourceType.value = 'Generator';
    p.elements.resourceType.handlers.change();
    p.controls.setTypeVisible('Maskin', false);
    p.controls.setTypeVisible('Generator', false);
    assert.equal(p.layers.size, 1);
    assert.ok(p.layers.has(p.markers[1]));
    p.map.setView([59, 9], 8);
    assert.equal(p.markers[1].options.icon.iconUrl, p.api.get('Generator').url);
    assert.equal(p.markers[1].options.icon.iconSize[0], p.api.sizeForZoom(8));
    p.controls.setTypeVisible('Maskin', true);
    assert.deepEqual(Array.from(p.markers[0].position), [60, 10]);
    assert.equal(p.markers[0].options.icon.iconSize[0], p.api.sizeForZoom(8));
});

test('edit and validation redisplay retain selected point without duplicating edited saved marker', () => {
    const edit = page({ selected: true, editId: 42 });
    assert.equal(edit.markers.length, 1);
    assert.deepEqual(Array.from(edit.markers[0].position), [58, 8]);
    assert.equal(edit.elements.selectedPlace.textContent, 'Eksisterende sted');
    assert.equal(edit.requests.length, 0);
    const invalid = page({ selected: true });
    assert.equal(invalid.markers.length, 2);
});

test('focusing a saved resource explicitly restores its type and never selects form coordinates', () => {
    const p = page({ focus: 42 });
    assert.equal(p.map.zoom, 15);
    assert.deepEqual(Array.from(p.map.position), [60, 10]);
    assert.equal(p.visibility.get('Maskin'), true);
    assert.equal(p.markers[0].opened, true);
    assert.equal(p.elements.latitude, undefined);
    assert.equal(p.markers.length, 1);
});

const settle = () => new Promise(resolve => setImmediate(resolve));
test('preview never binds or opens a popup after placement, lookup, type, search, edit or validation', async () => {
    const p = page({ placement: true });
    assert.ok(p.markers[0].popup.includes('Bil'), 'saved resources retain their meaningful popups');
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    p.requests[0].resolve({ ok: true, json: async () => ({ label: 'Valgt sted' }) });
    await settle();
    assert.equal(p.elements.selectedPlace.textContent, 'Valgt sted');
    p.elements.resourceType.value = 'Utstyr';
    p.elements.resourceType.handlers.change();
    p.elements.citySearch.value = 'Bergen';
    p.elements.citySearchButton.handlers.click();
    p.requests[1].resolve({ ok: true, json: async () => [{ lat: 60.39, lng: 5.32, label: 'Bergen' }] });
    await settle();
    p.elements.citySuggestions.children[0].children[0].handlers.click({ stopPropagation() {} });
    assert.equal(p.elements.selectedPlace.textContent, 'Bergen');
    assert.equal(p.markers.length, 2, 'selection reuses one preview');
    const previews = [p.markers[1], page({ selected: true, editId: 42 }).markers[0],
        page({ selected: true }).markers[1]];
    for (const marker of previews) {
        assert.equal(marker.popup, undefined);
        assert.equal(marker.opened, undefined);
        assert.ok(marker.options.title.startsWith('Ny ressurs: '));
        assert.ok(marker.options.alt);
    }
});

test('new point clears old label immediately; late reverse responses cannot change latest selection', async () => {
    const p = page({ placement: true });
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    const first = p.requests[0];
    p.map.events.click({ latlng: { lat: 60, lng: 10 } });
    assert.equal(first.options.signal.aborted, true);
    assert.equal(p.elements.selectedPlace.textContent, '60.00000, 10.00000');
    p.requests[1].resolve({ ok: true, json: async () => ({ label: 'Siste sted' }) });
    await settle();
    first.resolve({ ok: true, json: async () => ({ label: 'Gammelt sted' }) });
    await settle();
    assert.equal(p.elements.selectedPlace.textContent, 'Siste sted');
    p.map.events.click({ latlng: { lat: 0, lng: 0 } });
    p.requests[2].resolve({ ok: false });
    await settle();
    assert.equal(p.elements.selectedPlace.textContent, '0.00000, 0.00000');
    assert.equal(p.elements.latitude.value, 0);
});

test('full city search requires explicit button/Enter, ignores outdated results and uses selection without stale labels', async () => {
    const p = page({ placement: true });
    p.elements.citySearch.value = 'Bergen';
    p.elements.citySearch.handlers.input();
    assert.equal(p.requests.length, 0);
    let prevented = false;
    p.elements.citySearch.handlers.keydown({ key: 'Enter', preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.equal(p.requests.length, 1);
    p.requests[0].resolve({ ok: true, json: async () => [{ lat: 60.39, lng: 5.32, label: 'Bergen' }] });
    await settle();
    p.elements.citySuggestions.children[0].children[0].handlers.click({ stopPropagation() {} });
    assert.equal(p.elements.selectedPlace.textContent, 'Bergen');
    p.map.events.click({ latlng: { lat: 58, lng: 8 } });
    assert.equal(p.elements.selectedPlace.textContent, '58.00000, 8.00000');
    p.elements.citySearchButton.handlers.click();
    const stale = p.requests.at(-1);
    p.elements.citySearch.value = 'Oslo';
    p.elements.citySearch.handlers.input();
    stale.resolve({ ok: true, json: async () => [{ lat: 60, lng: 5, label: 'Stale' }] });
    await settle();
    assert.equal(stale.options.signal.aborted, true);
    assert.equal(p.elements.citySuggestions.children.length, 0);
});

test('browse clicks and browse search only move the map, including when registration is open', async () => {
    for (const register of [false, true]) {
        const p = page({ register });
        assert.equal(p.elements.registrationPanel.hidden, !register);
        p.map.events.click({ latlng: { lat: 59, lng: 9 } });
        assert.equal(p.requests.length, 0);
        assert.equal(p.markers.length, 1);
        assert.equal(p.elements.latitude, undefined);
        p.elements.citySearch.value = 'Bergen';
        p.elements.citySearchButton.handlers.click();
        p.requests[0].resolve({ ok: true, json: async () => [{ lat: 60.39, lng: 5.32, label: 'Bergen' }] });
        await settle();
        p.elements.citySuggestions.children[0].children[0].handlers.click({ stopPropagation() {} });
        assert.deepEqual(Array.from(p.map.position), [60.39, 5.32]);
        assert.equal(p.markers.length, 1);
        assert.equal(p.elements.latitude, undefined);
        assert.equal(p.elements.longitude, undefined);
    }
});

test('closing registration or disabling placement stops coordinate updates and retains chosen point', () => {
    const p = page({ placement: true });
    assert.equal(p.elements.placementToggle.attributes['aria-pressed'], 'true');
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    p.elements.placementToggle.handlers.click();
    assert.equal(p.elements.placementToggle.attributes['aria-pressed'], 'false');
    p.map.events.click({ latlng: { lat: 60, lng: 10 } });
    assert.equal(p.elements.latitude.value, 59);
    p.elements.placementToggle.handlers.click();
    p.elements.registrationToggle.handlers.click();
    assert.equal(p.elements.registrationPanel.hidden, true);
    assert.equal(p.elements.registrationToggle.attributes['aria-expanded'], 'false');
    p.map.events.click({ latlng: { lat: 61, lng: 11 } });
    assert.equal(p.elements.latitude.value, 59);
    assert.equal(p.requests.length, 1);
    p.elements.registrationToggle.handlers.click();
    assert.equal(p.elements.placementToggle.attributes['aria-pressed'], 'false');
    assert.equal(p.markers.length, 2);
});

test('edit and invalid submission start in browse mode while preserving coordinates', () => {
    for (const editId of [null, 42]) {
        const p = page({ selected: true, editId });
        assert.equal(p.elements.registrationPanel.hidden, false);
        p.map.events.click({ latlng: { lat: 61, lng: 11 } });
        assert.equal(p.elements.latitude.value, 58);
        assert.equal(p.elements.longitude.value, 8);
        assert.equal(p.requests.length, 0);
    }
});

test('mobile Kart/Liste controls and resource focus change view without choosing coordinates', () => {
    const p = page();
    p.elements.showList.handlers.click();
    assert.equal(p.elements.mapWorkspace.dataset.mobileView, 'list');
    assert.equal(p.elements.showList.attributes['aria-pressed'], 'true');
    assert.equal(p.elements.showMap.attributes['aria-pressed'], 'false');
    p.elements.showMap.handlers.click();
    assert.equal(p.elements.mapWorkspace.dataset.mobileView, 'map');
    assert.equal(p.elements.showMap.attributes['aria-pressed'], 'true');
    assert.equal(p.map.invalidations, 1);
    p.elements.showList.handlers.click();
    p.card.focusButton.handlers.click();
    assert.equal(p.elements.mapWorkspace.dataset.mobileView, 'map');
    assert.equal(p.card.open, true);
    assert.equal(p.elements.latitude, undefined);
    assert.equal(p.markers.length, 1);
    assert.equal(p.requests.length, 0);
    delete p.map.invalidateSize;
    p.elements.showMap.handlers.click();
    assert.equal(p.elements.mapWorkspace.dataset.mobileView, 'map', 'minimal map stubs need no invalidateSize');
});

test('resource selection expands details; sidebar and shared legend filters also filter results', () => {
    const p = page();
    assert.match(p.markers[0].popup, /Tilgjengelighet: Reservert/);
    assert.match(p.markers[0].popup, /Verifisering: Venter på verifisering/);
    assert.match(p.markers[0].popup, /Sist oppdatert: 25.10.2026 12:30 UTC/);
    p.markers[0].events.click();
    assert.equal(p.card.open, true);
    p.elements.resourceFilter.value = 'Drone';
    p.elements.resourceFilter.handlers.change();
    assert.equal(p.card.hidden, true);
    assert.equal(p.elements.resourceCount.textContent, 0);
    assert.equal(p.layers.size, 0);
    p.controls.setTypeVisible('Maskin', true);
    assert.equal(p.card.hidden, false);
    assert.equal(p.elements.resourceCount.textContent, 1);
    assert.equal(p.layers.size, 1);
});

test('map-first markup keeps search outside registration and exposes persisted operational fields', () => {
    const view = read('Views/Home/Map.cshtml');
    assert.ok(view.indexOf('id="citySearch"') < view.indexOf('<form method="post"'));
    assert.match(view, /Context\.Request\.Query\["register"\] == "true"/);
    assert.match(view, /!ViewData\.ModelState\.IsValid/);
    assert.match(view, /type="hidden" asp-for="Version"/);
    for (const field of ['Availability', 'Capability', 'TransportRequirements']) {
        assert.ok(view.includes(`asp-for="${field}"`));
    }
    assert.match(view, /WorkflowEligibility\.HasCurrentApproval/);
    assert.match(view, /r\.UpdatedAtUtc \?\? r\.CreatedAtUtc/);
    assert.match(view, /"Lagre endringer" : "Registrer ressurs"/);
    const css = read('wwwroot/css/workspace-map.css');
    assert.match(css, /grid-template-columns: 320px minmax\(0, 1fr\)/);
    assert.match(css, /\[data-mobile-view="map"\] \.workspace-results/);
    assert.match(css, /\[data-mobile-view="list"\] \.workspace-map-region/);
});

test('map provides owner-gated management links and readonly place summary; primary navigation has no stale tabs', () => {
    const view = read('Views/Home/Map.cshtml');
    assert.match(view, /ResourcePermissions\.CanManage\(User, r.OwnerId\)/);
    assert.match(view, /asp-action="Edit"/);
    assert.match(view, /asp-action="Delete"/);
    assert.match(view, /asp-action="Delete" asp-route-id="@r\.Id" class="btn-danger"/);
    assert.doesNotMatch(view, /name="LocationName"/);
    assert.match(view, /selectedPoint \? startLat : ""/);
    for (const path of ['Views/Home/Index.cshtml', 'Views/Shared/_Layout.cshtml']) {
        assert.doesNotMatch(read(path), /asp-action="(?:Form|Data)"/);
    }
    assert.match(read('Views/Shared/_Layout.cshtml'), /images\/forsvaret-logo\.png/);
});
