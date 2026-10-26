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
        focus() { this.focused = true; },
        classList: { add: c => classes.add(c), remove: c => classes.delete(c) },
        addEventListener(name, callback) { this.handlers[name] = callback; },
        appendChild(child) { this.children.push(child); },
        replaceChildren() { this.children = []; },
        contains() { return false; },
        get innerHTML() { return this.textContent; },
        set innerHTML(value) { this.children = []; this.textContent = value; },
        scrollIntoView() {}
    };
}

function page({ selected = false, editId = null, focus = '', register = false, placement = false, resizeObserver = false, emptyMap = false,
    resourceStatus = { isEligible: false, availability: 'Reservert', verification: 'Venter på verifisering',
        statusText: 'Kan ikke forespørres nå · Reservert · Venter på verifisering' } } = {}) {
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
    document.getElementById('resourceName').value = '';
    document.getElementById('selectedPlace').textContent = selected ? 'Eksisterende sted' : '';
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
    const timers = new Map();
    let nextTimer = 0;
    const visibility = new Map();
    let controls, observedElement, onResize, refreshAvailability, overlay;
    let currentTime = Date.now();
    const context = {
        document, window: {
            location: { search: focus ? '?focus=' + focus : register ? '?register=true' : '' },
            ResourcesOverlay: { initialize(callbacks) { overlay = callbacks; } }
        },
        URLSearchParams, AbortController, Map, Set,
        Date: class extends Date { static now() { return currentTime; } },
        setTimeout(callback, delay) { timers.set(++nextTimer, { callback, delay }); return nextTimer; },
        clearTimeout(id) { timers.delete(id); },
        fetch(url, options) {
            return new Promise((resolve, reject) => requests.push({ url, options, resolve, reject }));
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
                    options, position, dom: element(),
                    addTo() { layers.add(this); return this; },
                    bindPopup(popup) { this.popup = popup; return this; },
                    openPopup() { this.opened = true; return this; },
                    on(name, handler) { (this.events ??= {})[name] = handler; return this; },
                    setIcon(icon) { this.options.icon = icon; },
                    getLatLng() { return this.position; },
                    getElement() { return this.dom; },
                    setLatLng(position) { this.position = position; return this; }
                };
                markers.push(marker);
                return marker;
            }
        }
    };
    if (resizeObserver) context.ResizeObserver = class {
        constructor(callback) { onResize = callback; }
        observe(target) { observedElement = target; }
    };
    vm.runInNewContext(read('wwwroot/js/resource-icons.js'), context);
    context.window.ResourceIcons = {
        ...context.window.ResourceIcons,
        watchAvailability(callback) { refreshAvailability = callback; }
    };
    vm.runInNewContext(read('wwwroot/js/place-search.js'), context);
    context.PlaceSearch = context.window.PlaceSearch;
    context.resourceMarkerOptions = context.window.resourceMarkerOptions;
    const view = read('Views/Home/Map.cshtml');
    const script = view.slice(view.lastIndexOf('<script>') + 8, view.lastIndexOf('</script>'))
        .replace('const startLat = @startLat;', 'const startLat = 58;')
        .replace('const startLng = @startLng;', 'const startLng = 8;')
        .replace(/const savedResources = .*;/, emptyMap ? 'const savedResources = [];' : `const savedResources = [{id: 42, name: "Bil", type: "Maskin", lat: 60, lng: 10, availability: "Reservert", verification: "Verifisert", updated: "25.10.2026 12:30 UTC", status: ${JSON.stringify(resourceStatus)}}];`)
        .replace(/const hasSelectedPoint = .*;/, `const hasSelectedPoint = ${selected};`)
        .replace(/const registrationInitiallyOpen = .*;/, `const registrationInitiallyOpen = ${register || selected || editId !== null};`)
        .replace(/const editId = .*;/, `const editId = ${editId};`)
        .replace(/const placeUrl = .*;/, 'const placeUrl = "/Home/Place";')
        .replace(/const searchUrl = .*;/, 'const searchUrl = "/Home/SearchPlaces";')
        .replace(/localitiesUrl: @Json.Serialize.*,$/m, 'localitiesUrl: "/data/norway-localities.json",');
    vm.runInNewContext(script, context);
    if (placement) {
        if (elements.registrationPanel.hidden) elements.registrationToggle.handlers.click();
    }
    return {
        map, markers, elements, layers, controls, visibility, requests, card, overlay, api: context.window.ResourceIcons,
        expire(milliseconds) { currentTime += milliseconds; refreshAvailability(); },
        resize(width, height) {
            assert.equal(observedElement, elements.map);
            onResize([{ contentRect: { width, height } }]);
        },
        timeout() {
            for (const [id, timer] of timers) if (timer.delay === 6000) {
                timers.delete(id); timer.callback();
            }
        }
    };
}

test('overlay resource focus uses the existing map without replacing registration or selected point', () => {
    const p = page({ selected: true });
    p.elements.resourceName.value = 'Uferdig registrering';
    const selectedMarker = p.markers[1];
    const coordinates = selectedMarker.position;
    p.overlay.onFocus('42', 60, 10);
    assert.deepEqual(Array.from(p.map.position), [60, 10]);
    assert.equal(p.map.zoom, 15);
    assert.equal(p.markers.length, 2);
    assert.equal(selectedMarker.position, coordinates);
    assert.equal(p.elements.resourceName.value, 'Uferdig registrering');
    assert.equal(p.elements.registrationPanel.hidden, false);
    assert.equal(p.markers[0].opened, true);
});

test('overlay handles newly listed map points and registration without reloading the map', () => {
    const p = page();
    p.overlay.onFocus('100', 59, 9);
    assert.deepEqual(Array.from(p.map.position), [59, 9]);
    assert.equal(p.markers.length, 1);
    p.overlay.onRegister();
    assert.equal(p.elements.registrationPanel.hidden, false);
    assert.equal(p.markers.length, 1);
});

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

test('opening registration activates placement without a preview; first click creates it independently of filters', () => {
    const p = page({ placement: true });
    assert.equal(p.elements.registrationToggle.attributes['aria-expanded'], 'true');
    assert.equal(p.elements.mapWorkspace.dataset.registrationOpen, 'true');
    assert.match(p.elements.mapModeStatus.textContent, /Registrering/);
    assert.equal(p.markers.length, 1);
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    assert.equal(p.markers.length, 2);
    assert.equal(p.elements.latitude.value, 59);
    assert.equal(p.elements.longitude.value, 9);
    assert.equal(p.elements.selectedPlace.textContent, '59.00000, 9.00000');
    assert.equal(p.elements.citySearch.value, '59.00000, 9.00000');
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

test('saved markers retain operational state through zoom and filters while pending placement stays neutral', () => {
    const now = Date.now();
    for (const eligible of [false, true]) {
        const resourceStatus = {
            isEligible: eligible, availability: 'Tilgjengelig', verification: 'Godkjent',
            verifiedAtUtc: new Date(now - 60000).toISOString(),
            verificationExpiresAtUtc: new Date(now + 60000).toISOString(),
            statusText: eligible ? 'Kan forespørres nå' : 'Kan ikke forespørres nå'
        };
        const p = page({ register: true, resourceStatus });
        const expected = /-available\.svg/;
        assert.match(p.markers[0].options.icon.iconUrl, expected);
        assert.ok(p.markers[0].options.title.includes(resourceStatus.statusText));
        assert.ok(p.markers[0].popup.includes('Tilgjengelig · Godkjenning: Godkjent'));
        p.map.setZoom(8);
        p.controls.setTypeVisible('Maskin', false);
        p.controls.setTypeVisible('Maskin', true);
        assert.match(p.markers[0].options.icon.iconUrl, expected);
        p.map.events.click({ latlng: { lat: 59, lng: 9 } });
        assert.match(p.markers[1].options.icon.className, /resource-state-neutral/);
        if (eligible) {
            p.expire(120000);
            assert.match(p.markers[0].options.icon.iconUrl, /-available\.svg/);
            assert.match(p.markers[0].dom.title, /Godkjenning utløpt/);
            assert.match(p.markers[0].dom.alt, /Godkjenning utløpt/);
            assert.match(p.markers[0].popup, /Godkjenning utløpt/);
            assert.doesNotMatch(p.markers[0].popup, /Verifisert|Verifisering:|Tilgjengelighet:/);
            assert.match(p.markers[1].options.icon.className, /resource-state-neutral/);
        }
    }
    const view = read('Views/Home/Map.cshtml');
    assert.match(view, /status = resourceStatuses\[r.Id\]/);
    assert.match(view, /ViewBag.ResourceStatuses/);
    assert.match(view, /data-resource-status="/);
    assert.match(view, /<summary>[\s\S]*?<small data-resource-status-text="@r.Id">@status.Availability · Godkjenning: @status.Verification<\/small>[\s\S]*?<\/summary>/);
    assert.doesNotMatch(view, /@status.StatusText/);
    assert.doesNotMatch(view, /<dt>(?:Tilgjengelighet|Verifisering)<\/dt>|VerificationLabel/);
    assert.match(view, /@AvailabilityLabel\(Model.Availability\)/);
    assert.match(view, /watchAvailability/);
});

test('map popups show concise availability and approval without assessment prose', () => {
    const p = page({
        resourceStatus: { isEligible: false, availability: 'Utilgjengelig', verification: 'Venter på verifisering',
            statusText: 'Kan ikke forespørres nå · Utilgjengelig · Venter på verifisering' }
    });
    const popup = p.markers[0].popup;
    assert.match(popup, /Status: Utilgjengelig · Godkjenning: Venter på verifisering/);
    assert.doesNotMatch(popup, /Operativ vurdering|Operativ status|Kan ikke forespørres nå/);
    assert.match(popup, /href="#resource-42"/);
});

test('edit and validation redisplay retain selected point without duplicating edited saved marker', () => {
    const edit = page({ selected: true, editId: 42 });
    assert.equal(edit.markers.length, 1);
    assert.deepEqual(Array.from(edit.markers[0].position), [58, 8]);
    assert.equal(edit.elements.selectedPlace.textContent, 'Eksisterende sted');
    assert.equal(edit.elements.citySearch.value, 'Eksisterende sted');
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
test('an empty resource map shows no markers and distinguishes reset state from filters', () => {
    const p = page({ emptyMap: true });
    assert.equal(p.markers.length, 0);
    p.controls.setTypeVisible('Maskin', false);
    assert.equal(p.elements.resourceCount.textContent, 0);
    assert.equal(p.elements.resourceResultsStatus.textContent, 'Ingen ressurser er plassert på kartet.');
});

test('preview never binds or opens a popup after placement, lookup, type, search, edit or validation', async () => {
    const p = page({ placement: true });
    assert.ok(p.markers[0].popup.includes('Bil'), 'saved resources retain their meaningful popups');
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    p.requests[0].resolve({ ok: true, json: async () => ({ label: 'Valgt sted' }) });
    await settle();
    assert.equal(p.elements.selectedPlace.textContent, 'Valgt sted');
    assert.equal(p.elements.citySearch.value, 'Valgt sted');
    p.elements.resourceType.value = 'Utstyr';
    p.elements.resourceType.handlers.change();
    p.elements.citySearch.value = 'Bergen';
    p.elements.citySearchButton.handlers.click();
    p.requests[1].resolve({ ok: true, json: async () => [{ lat: 60.39, lng: 5.32, label: 'Bergen' }] });
    await settle();
    p.elements.citySuggestions.children[0].children[0].handlers.click({ stopPropagation() {} });
    assert.equal(p.elements.selectedPlace.textContent, 'Bergen');
    assert.equal(p.elements.citySearch.value, 'Bergen');
    assert.equal(p.elements.latitude.value, 60.39);
    assert.equal(p.elements.longitude.value, 5.32);
    assert.equal(p.requests.length, 2, 'search selection does not trigger reverse lookup or another search');
    assert.equal(p.markers.length, 2, 'selection reuses one preview');
    const previews = [p.markers[1], page({ selected: true, editId: 42 }).markers[0],
        page({ selected: true }).markers[1]];
    for (const marker of previews) {
        assert.equal(marker.popup, undefined);
        assert.equal(marker.opened, undefined);
        assert.ok(marker.options.title.startsWith('Ny ressurs (ikke lagret): '));
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
    assert.equal(p.elements.citySearch.value, 'Siste sted');
    p.map.events.click({ latlng: { lat: 0, lng: 0 } });
    p.requests[2].resolve({ ok: false });
    await settle();
    assert.equal(p.elements.selectedPlace.textContent, '0.00000, 0.00000');
    assert.equal(p.elements.citySearch.value, '0.00000, 0.00000');
    assert.equal(p.elements.latitude.value, 0);
});

test('typing invalidates reverse lookup without changing form data or starting a remote search', async () => {
    const p = page({ register: true });
    p.elements.resourceName.value = 'Min ressurs';
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    const lookup = p.requests[0];
    p.elements.citySearch.value = 'Bergen';
    p.elements.citySearch.handlers.input();
    assert.equal(lookup.options.signal.aborted, true);
    lookup.resolve({ ok: true, json: async () => ({ label: 'For sent' }) });
    await settle();
    assert.equal(p.elements.citySearch.value, 'Bergen');
    assert.equal(p.elements.selectedPlace.textContent, '59.00000, 9.00000');
    assert.equal(p.elements.latitude.value, 59);
    assert.equal(p.elements.longitude.value, 9);
    assert.equal(p.elements.resourceName.value, 'Min ressurs');
    assert.equal(p.elements.resourceType.value, 'Drone');
    assert.equal(p.requests.length, 1);
    p.map.events.click({ latlng: { lat: 60, lng: 10 } });
    p.requests[1].resolve({ ok: true, json: async () => ({ label: '<img src=x onerror=alert(1)>' }) });
    await settle();
    assert.equal(p.elements.citySearch.value, '<img src=x onerror=alert(1)>');
    assert.equal(p.elements.selectedPlace.textContent, '<img src=x onerror=alert(1)>');
    assert.equal(p.elements.citySearch.children.length, 0);
    assert.equal(p.elements.selectedPlace.children.length, 0);
    assert.equal(p.markers.length, 2, 'new clicks move the existing preview');
    assert.deepEqual(Array.from(p.markers[1].position), [60, 10]);
});

test('reverse lookup failure or invalid label retains usable coordinates in summary and search', async () => {
    for (const result of [{ ok: false }, { ok: true, json: async () => ({ label: 'x'.repeat(221) }) },
        { ok: true, json: async () => ({ label: 'bad\nlabel' }) },
        { ok: true, json: async () => ({ label: 123 }) }]) {
        const p = page({ register: true });
        assert.equal(p.markers.length, 1, 'registration alone must not create a preview');
        p.map.events.click({ latlng: { lat: 59, lng: 9 } });
        p.requests[0].resolve(result);
        await settle();
        assert.equal(p.elements.citySearch.value, '59.00000, 9.00000');
        assert.equal(p.elements.selectedPlace.textContent, '59.00000, 9.00000');
        assert.equal(p.elements.latitude.value, 59);
        assert.equal(p.elements.longitude.value, 9);
        assert.equal(p.requests.length, 1);
    }
});

test('reverse request timeout and network failure retain coordinates, and closing registration invalidates lookup', async () => {
    const p = page({ register: true });
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    p.timeout();
    assert.equal(p.requests[0].options.signal.aborted, true);
    p.requests[0].resolve({ ok: true, json: async () => ({ label: 'Timed out' }) });
    await settle();
    assert.equal(p.elements.citySearch.value, '59.00000, 9.00000');
    p.map.events.click({ latlng: { lat: 60, lng: 10 } });
    p.requests[1].reject(new Error('Offline'));
    await settle();
    assert.equal(p.elements.citySearch.value, '60.00000, 10.00000');
    assert.equal(p.elements.selectedPlace.textContent, '60.00000, 10.00000');
    p.map.events.click({ latlng: { lat: 61, lng: 11 } });
    p.elements.registrationToggle.handlers.click();
    assert.equal(p.requests[2].options.signal.aborted, true);
    p.requests[2].resolve({ ok: true, json: async () => ({ label: 'Closed' }) });
    await settle();
    assert.equal(p.elements.citySearch.value, '61.00000, 11.00000');
    assert.equal(p.elements.latitude.value, 61);
    assert.equal(p.elements.longitude.value, 11);
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

test('browse clicks and search only move the map, including after returning from registration', async () => {
    for (const register of [false, true]) {
        const p = page({ register });
        assert.equal(p.elements.registrationPanel.hidden, !register);
        if (register) p.elements.registrationCancel.handlers.click();
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

test('returning to browsing stops placement, retains form values and restores toolbar focus', () => {
    const p = page({ placement: true });
    p.elements.resourceName.value = 'Min ressurs';
    p.map.events.click({ latlng: { lat: 59, lng: 9 } });
    p.elements.registrationCancel.handlers.click();
    assert.equal(p.elements.registrationToggle.focused, true);
    assert.equal(p.elements.mapWorkspace.dataset.registrationOpen, 'false');
    assert.equal(p.elements.mapModeStatus.textContent, '');
    p.map.events.click({ latlng: { lat: 60, lng: 10 } });
    assert.equal(p.elements.latitude.value, 59);
    assert.equal(p.elements.registrationPanel.hidden, true);
    assert.equal(p.elements.registrationToggle.attributes['aria-expanded'], 'false');
    p.map.events.click({ latlng: { lat: 61, lng: 11 } });
    assert.equal(p.elements.latitude.value, 59);
    assert.equal(p.requests.length, 1);
    p.elements.registrationToggle.handlers.click();
    assert.equal(p.elements.registrationToggle.attributes['aria-expanded'], 'true');
    assert.equal(p.elements.resourceName.value, 'Min ressurs');
    assert.equal(p.elements.resourceType.value, 'Drone');
    assert.equal(p.elements.resourceName.focused, true);
    assert.equal(p.markers.length, 2);
    p.map.events.click({ latlng: { lat: 62, lng: 12 } });
    assert.equal(p.elements.latitude.value, 62);
    assert.equal(p.markers.length, 2);
});

test('registration toolbar is grouped with resources; obsolete controls are absent and attribution remains', () => {
    const view = read('Views/Home/Map.cshtml');
    const toolbar = view.slice(view.indexOf('<div class="workspace-toolbar"'), view.indexOf('@if (isEditing)'));
    assert.match(toolbar, /id="registrationToggle" class="btn-submit"/);
    assert.match(toolbar, /aria-controls="registrationPanel" aria-expanded=/);
    assert.match(toolbar, /Ressurser og mine ressurser/);
    assert.doesNotMatch(view, /placementToggle|Velg plassering|Personvern og kartdata/);
    const attribution = view.match(/attribution: '([^']+)'/)[1];
    assert.match(attribution, /openstreetmap.org\/copyright/);
    assert.match(attribution, /geonames.org/);
    assert.match(attribution, /creativecommons.org\/licenses\/by\/4.0\/.*bearbeidet/);
    assert.match(read('wwwroot/data/NOTICE.txt'), /GeoNames[\s\S]*Creative Commons Attribution 4.0/);
    assert.doesNotMatch(view, /Søk og punktoppslag bruker OpenStreetMap|citySearchHelp|Unngå fortrolige opplysninger/);
    assert.match(view, /aria-describedby="citySearchStatus"/);
    assert.match(view, /<legend>Ressursdetaljer<\/legend>/);
    assert.match(view, /<legend>Plassering/);
    assert.match(view, /Ingen plassering valgt ennå/);
    const p = page({ register: true });
    p.elements.showList.handlers.click();
    p.elements.locationMapLink.handlers.click();
    assert.equal(p.elements.mapWorkspace.dataset.mobileView, 'map');
    assert.equal(p.markers.length, 1, 'going to the map never selects a location');
});

test('edit and invalid submission start in placement mode, preserving then moving existing coordinates', () => {
    for (const editId of [null, 42]) {
        const p = page({ selected: true, editId });
        assert.equal(p.elements.registrationPanel.hidden, false);
        assert.equal(p.elements.latitude.value, 58);
        assert.equal(p.elements.longitude.value, 8);
        assert.equal(p.requests.length, 0);
        p.map.events.click({ latlng: { lat: 61, lng: 11 } });
        assert.equal(p.elements.latitude.value, 61);
        assert.equal(p.elements.longitude.value, 11);
        assert.equal(p.requests.length, 1);
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

test('container resize invalidates Leaflet after grid changes without selecting coordinates or moving map', () => {
    const p = page({ resizeObserver: true });
    const position = Array.from(p.map.position);
    p.resize(1100, 700);
    assert.equal(p.map.invalidations, 1);
    p.resize(900, 700);
    assert.equal(p.map.invalidations, 2);
    p.resize(0, 0);
    assert.equal(p.map.invalidations, 2, 'ignore the hidden mobile map');
    p.resize(700, 650);
    assert.equal(p.map.invalidations, 3, 'restore the map after its container becomes visible');
    assert.deepEqual(Array.from(p.map.position), position);
    assert.equal(p.elements.latitude, undefined);
    assert.equal(p.markers.length, 1);
    assert.equal(p.requests.length, 0);
    delete p.map.invalidateSize;
    assert.doesNotThrow(() => p.resize(600, 650));
});

test('resource selection expands details; sidebar and shared legend filters also filter results', () => {
    const p = page();
    assert.doesNotMatch(p.markers[0].popup, /Tilgjengelighet:/);
    assert.match(p.markers[0].popup, /Status: Reservert · Godkjenning: Venter på verifisering/);
    assert.doesNotMatch(p.markers[0].popup, /Verifisering:/);
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
    assert.match(view, /ResourceOperationalStatus\.From/);
    assert.match(view, /r\.UpdatedAtUtc \?\? r\.CreatedAtUtc/);
    assert.match(view, /"Lagre endringer" : "Registrer ressurs"/);
    const css = read('wwwroot/css/workspace-map.css');
    assert.match(css, /grid-template-columns:/);
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
