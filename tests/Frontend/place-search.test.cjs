const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');
const root = resolve(__dirname, '../..');
const read = path => readFileSync(resolve(root, path), 'utf8');
const settle = () => new Promise(resolve => setImmediate(resolve));

function element(id = '') {
    const classes = new Set();
    return {
        id, value: '', textContent: '', children: [], handlers: {}, attributes: {},
        classList: { add: c => classes.add(c), remove: c => classes.delete(c) },
        setAttribute(name, value) { this.attributes[name] = value; },
        removeAttribute(name) { delete this.attributes[name]; },
        addEventListener(name, handler) { this.handlers[name] = handler; },
        appendChild(child) { this.children.push(child); },
        get firstChild() { return this.children[0]; },
        set innerHTML(value) { assert.equal(value, ''); this.children = []; },
        contains(target) { return this === target || this.children.some(c => c.contains(target)); },
        scrollIntoView() {}, focus() { this.focused = true; }
    };
}

function setup() {
    const input = element('search'), list = element('suggestions'), status = element(), button = element();
    const timers = new Map(), requests = [], selections = [];
    let nextTimer = 0, outside;
    const context = {
        window: {}, AbortController,
        document: {
            createElement: () => element(),
            addEventListener(name, handler) { outside = handler; }
        },
        setTimeout(fn, delay) { timers.set(++nextTimer, { fn, delay }); return nextTimer; },
        clearTimeout(id) { timers.delete(id); },
        fetch(url, options) {
            return new Promise((resolve, reject) => requests.push({ url, options, resolve, reject }));
        }
    };
    vm.runInNewContext(read('wwwroot/js/place-search.js'), context);
    context.window.PlaceSearch.attach({
        input, list, status, button, searchUrl: '/Home/SearchPlaces',
        localitiesUrl: '/data/norway-localities.json?v=test', onSelect: place => selections.push(place)
    });
    return {
        input, list, status, button, requests, selections,
        type(value) { input.value = value; input.handlers.input(); },
        debounce() {
            for (const [id, timer] of timers) if (timer.delay === 250) {
                timers.delete(id); timer.fn();
            }
        },
        timeout() {
            for (const [id, timer] of timers) if (timer.delay === 6000) {
                timers.delete(id); timer.fn();
            }
        },
        key(key) {
            let prevented = false;
            input.handlers.keydown({ key, preventDefault() { prevented = true; } });
            return prevented;
        },
        outside() { outside({ target: element() }); }
    };
}
const bergen = { label: 'Bergen, Norge', lat: 60.39, lng: 5.32 };
const oslo = { label: 'Oslo, Norge', lat: 59.91, lng: 10.75 };
const response = data => ({ ok: true, json: async () => data });

test('typing uses only one cached local dataset, minimum length and debounce; never selects a point', async () => {
    const p = setup();
    for (const query of ['', 'B', 'x'.repeat(121), 'ab\ncd']) {
        p.type(query); p.debounce();
    }
    assert.equal(p.requests.length, 0);
    p.type('Be'); p.type('Berg');
    assert.equal(p.requests.length, 0);
    p.debounce();
    assert.equal(p.requests.length, 1);
    assert.match(p.requests[0].url, /^\/data\/norway-localities/);
    assert.match(p.status.textContent, /Henter/);
    p.requests[0].resolve(response([bergen, oslo]));
    await settle();
    assert.equal(p.list.children[0].firstChild.textContent, bergen.label);
    assert.equal(p.input.attributes['aria-expanded'], 'true');
    assert.equal(p.input.attributes['aria-busy'], 'false');
    p.type('Osl'); p.debounce(); await settle();
    assert.equal(p.list.children[0].firstChild.textContent, oslo.label);
    assert.equal(p.requests.length, 1);
    assert.equal(p.selections.length, 0);
    p.type('NoSuchPlace'); p.debounce(); await settle();
    assert.match(p.status.textContent, /Ingen treff lokalt/);
});

test('late local loads use only latest query; Escape, outside click and short input invalidate pending work', async () => {
    const p = setup();
    p.type('Be'); p.debounce();
    p.type('Os'); p.debounce();
    assert.equal(p.requests.length, 1, 'share in-flight dataset download');
    p.requests[0].resolve(response([bergen, oslo]));
    await settle();
    assert.equal(p.list.children[0].firstChild.textContent, oslo.label);
    p.type('Be'); p.key('Escape'); p.debounce(); await settle();
    assert.equal(p.list.children.length, 0);
    p.type('Be'); p.outside(); p.debounce(); await settle();
    assert.equal(p.list.children.length, 0);
    p.type('Be'); p.type('B'); p.debounce(); await settle();
    assert.equal(p.list.children.length, 0);
});

test('keyboard, click/touch selection and Escape/Tab use accessible options and never submit the form', async () => {
    const p = setup();
    p.type('Be'); p.debounce();
    p.requests[0].resolve(response([bergen, { ...bergen, label: 'Berg, Norge' }]));
    await settle();
    assert.equal(p.key('ArrowDown'), true);
    assert.equal(p.input.attributes['aria-activedescendant'], 'suggestions-option-0');
    assert.equal(p.list.children[0].firstChild.attributes['aria-selected'], 'true');
    p.key('ArrowDown');
    assert.equal(p.key('Enter'), true);
    assert.equal(p.selections[0].label, 'Berg, Norge');
    assert.equal(p.input.focused, true);
    assert.equal(p.input.attributes['aria-expanded'], 'false');
    assert.equal(p.requests.length, 1);
    p.type('Be'); p.debounce(); await settle();
    p.key('ArrowUp');
    assert.equal(p.input.attributes['aria-activedescendant'], 'suggestions-option-1');
    p.key('Tab');
    assert.equal(p.list.children.length, 0);
    p.type('Be'); p.debounce(); await settle();
    let stopped = false;
    p.list.children[0].firstChild.handlers.click({ stopPropagation() { stopped = true; } });
    assert.equal(stopped, true, 'detaching the option must not trigger the outside-click handler');
    assert.equal(p.selections[1].label, bergen.label);
    assert.equal(p.status.textContent, 'Valgt: ' + bergen.label);
    assert.equal(p.input.attributes['aria-activedescendant'], undefined);
});

test('full search is explicit, cancelled on edit, and stale success/errors cannot replace local results', async () => {
    const p = setup();
    p.type('Be'); p.button.handlers.click();
    assert.match(p.requests[0].url, /^\/Home\/SearchPlaces\?query=Be$/);
    const stale = p.requests[0];
    p.type('Os'); p.debounce();
    assert.equal(stale.options.signal.aborted, true);
    p.requests[1].resolve(response([bergen, oslo]));
    await settle();
    stale.resolve(response([bergen]));
    await settle();
    assert.equal(p.list.children[0].firstChild.textContent, oslo.label);
    p.key('Escape');
    assert.equal(p.key('Enter'), true);
    assert.equal(p.requests.length, 3);
    p.requests[2].reject(new Error('Network'));
    await settle();
    assert.match(p.status.textContent, /Kunne ikke hente steder/);
    p.button.handlers.click();
    p.requests[3].resolve(response([oslo]));
    await settle();
    p.key('Enter');
    assert.equal(p.selections[0].label, oslo.label);
    assert.equal(p.requests.length, 4);
});

test('untrusted labels stay text; invalid results rejected, local failure allows explicit fallback', async () => {
    const p = setup();
    p.type('Be'); p.debounce();
    p.requests[0].reject(new Error('Offline'));
    await settle();
    assert.match(p.status.textContent, /Kunne ikke hente lokale forslag/);
    p.button.handlers.click();
    p.requests[1].resolve(response([
        { ...bergen, label: '<img src=x onerror=alert(1)>' },
        { ...oslo, lat: '59' }, { ...oslo, lng: Infinity }, { ...oslo, lat: 91 },
        { ...oslo, label: 'x'.repeat(221) }
    ]));
    await settle();
    assert.equal(p.list.children.length, 1);
    assert.equal(p.list.children[0].firstChild.textContent, '<img src=x onerror=alert(1)>');
    p.type('Be'); p.debounce();
    p.requests[2].resolve(response({ invalid: true }));
    await settle();
    assert.match(p.status.textContent, /Kunne ikke hente lokale forslag/);
});

test('timeouts abort bounded requests, invalid full queries do not fetch, and a local failure can retry', async () => {
    const p = setup();
    p.type('x');
    p.button.handlers.click();
    assert.equal(p.requests.length, 0);
    assert.match(p.status.textContent, /2 og 120/);
    p.type('Be'); p.debounce();
    p.timeout();
    assert.equal(p.requests[0].options.signal.aborted, true);
    p.requests[0].reject(new Error('Aborted'));
    await settle();
    assert.equal(p.input.attributes['aria-busy'], 'false');
    p.type('Be'); p.debounce();
    p.requests[1].resolve(response([bergen]));
    await settle();
    assert.equal(p.list.children.length, 1);
    p.button.handlers.click();
    p.timeout();
    assert.equal(p.requests[2].options.signal.aborted, true);
    p.requests[2].resolve(response([oslo]));
    await settle();
    assert.equal(p.list.children.length, 0, 'timed-out results are ignored');
});

test('bundled data is bounded, Norwegian, and supports accented name matching', async () => {
    const data = JSON.parse(read('wwwroot/data/norway-localities.json'));
    assert.equal(data.length, 624);
    assert.ok(data.every(p => p.label.includes(', Norge') && p.lat >= 57 && p.lat <= 72
        && p.lng >= 4 && p.lng <= 32));
    for (const name of ['Oslo', 'Bergen', 'Kristiansand', 'Tromsø', 'Ålesund', 'Trondheim']) {
        assert.ok(data.some(p => p.label === name + ', Norge'), name);
    }
    const p = setup();
    p.type('tromso'); p.debounce();
    p.requests[0].resolve(response(data)); await settle();
    assert.equal(p.list.children[0].firstChild.textContent, 'Tromsø, Norge');
    assert.ok(p.list.children.length <= 6);
    const markup = read('Views/Home/Map.cshtml');
    assert.match(markup, /role="combobox"/);
    assert.match(markup, /role="listbox"/);
    assert.match(markup, /aria-live="polite"/);
    assert.match(markup, /FileVersionProvider.AddFileVersionToPath/);
});
