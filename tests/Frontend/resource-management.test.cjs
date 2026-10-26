const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const read = file => readFileSync(require('node:path').resolve(__dirname, '../..', file), 'utf8');
const settle = () => new Promise(resolve => setImmediate(resolve));

function setup({ navigate = true, readOnly = false } = {}) {
    const nodes = {};
    const requests = [];
    const navigations = [];
    const events = [];
    let replacement;
    const document = { activeElement: null, querySelectorAll: () => [], handlers: {},
        createElement() {
            return { content: { querySelector: selector => selector === '.resource-overview' ? replacement : null } };
        },
        addEventListener(name, handler) { this.handlers[name] = handler; },
        removeEventListener(name) { delete this.handlers[name]; },
        dispatchEvent: event => events.push(event) };
    function element() {
        return {
            dataset: {}, attributes: {}, handlers: {}, isConnected: true,
            addEventListener(name, handler) { this.handlers[name] = handler; },
            setAttribute(name, value) { this.attributes[name] = value; },
            removeAttribute(name) { delete this.attributes[name]; },
            focus() { document.activeElement = this; },
            scrollIntoView() {},
            remove() { this.isConnected = false; },
            closest() { return null; }
        };
    }
    const selectors = ['.filter-bar', '#resourceCity', '#resourceCityOptions', '.resource-management-status',
        '.resource-delete-dialog', '[data-resource-delete-form]', '[data-delete-cancel]',
        '.resource-delete-error', '#resourceDeleteName', '.count-badge'];
    selectors.forEach(selector => { nodes[selector] = element(); });
    const root = element();
    root.querySelector = selector => readOnly && selector !== '.resource-management-status' ? null : nodes[selector];
    root.replaceWith = node => { root.replacement = node; root.isConnected = false; };
    const row = element();
    row.dataset.city = 'Oslo';
    root.querySelectorAll = () => row.isConnected ? [row] : [];
    const input = nodes['#resourceCity'];
    input.parentElement = { contains: node => node === input || node === list };
    const list = nodes['#resourceCityOptions'];
    list.hidden = true;
    const cities = [['Oslo', 2], ['Bergen', 1], ['Ålesund', 1]].map(([city, count], index) => {
        const option = element();
        option.dataset = { city, count: String(count) };
        option.id = 'city-' + index;
        option.closest = () => option;
        return option;
    });
    list.querySelectorAll = () => cities.filter(city => city.isConnected);
    list.append = () => {};
    const form = nodes['.filter-bar'];
    form.action = 'https://example.test/Home/Resources';
    form.values = [['mine', 'true'], ['type', 'Drone'], ['eligible', 'true']];
    const deleteForm = nodes['[data-resource-delete-form]'];
    deleteForm.elements = { version: { value: '' } };
    deleteForm.querySelectorAll = () => [nodes['[data-delete-cancel]']];
    const dialog = nodes['.resource-delete-dialog'];
    dialog.querySelector = () => dialog.nestedDialog || null;
    dialog.getBoundingClientRect = () => ({ left: 300, top: 200, right: 700, bottom: 500 });
    dialog.showModal = () => { dialog.open = true; };
    dialog.close = () => { dialog.open = false; queueMicrotask(() => dialog.handlers.close()); };
    const link = element();
    Object.defineProperty(link, 'isConnected', { get: () => row.isConnected });
    link.dataset = { resourceDelete: '42', resourceName: '<img> Drone', version: 'current-version' };
    link.href = 'https://example.test/Home/Delete/42';
    link.closest = selector => selector === '[data-resource-delete]' ? link : row;
    const context = {
        document, window: { location: { href: 'https://example.test/Home/Resources' } },
        URL, URLSearchParams, AbortController,
        FormData: class {
            constructor(f) { this.form = f; }
            [Symbol.iterator]() {
                return [...this.form.values, ['city', input.value]][Symbol.iterator]();
            }
        },
        CustomEvent: class { constructor(type, { detail }) { this.type = type; this.detail = detail; } },
        fetch(url, options) { return new Promise((resolve, reject) => requests.push({ url, options, resolve, reject })); }
    };
    vm.runInNewContext(read('wwwroot/js/resource-management.js'), context);
    context.window.ResourceManagement.attach(root, navigate ? (...args) => navigations.push(args) : null);
    const event = (props = {}) => ({
        preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; }, ...props
    });
    return { nodes, root, row, input, list, cities, dialog, deleteForm, link, document, event, requests, navigations, events,
        setReplacement(node) { replacement = node; } };
}

test('read-only history attaches without city filters or delete controls in standalone and overlay views', () => {
    for (const navigate of [false, true]) {
        const p = setup({ readOnly: true, navigate });
        assert.equal(p.root.dataset.managementReady, 'true');
        assert.deepEqual(p.root.handlers, {});
        assert.deepEqual(p.document.handlers, {});
        assert.equal(p.requests.length, 0);
        assert.equal(p.navigations.length, 0);
    }
});

test('standalone AJAX replacement can display read-only history and announce the updated results', async () => {
    const p = setup({ navigate: false });
    const history = setup({ readOnly: true });
    delete history.root.dataset.managementReady;
    p.setReplacement(history.root);
    p.list.handlers.click(p.event({ target: p.cities[1] }));
    p.requests[0].resolve({ ok: true, text: async () => '<section class="resource-overview"></section>' });
    await settle();
    assert.equal(p.root.replacement, history.root);
    assert.equal(history.root.dataset.managementReady, 'true');
    assert.equal(history.nodes['.resource-management-status'].textContent, 'Resultatene er oppdatert.');
    assert.equal(history.document.activeElement, history.nodes['.resource-management-status']);
    assert.deepEqual(history.root.handlers, {});
});

test('focus exposes every scoped city and keyboard selection filters immediately retaining scope and filters', () => {
    const p = setup();
    p.input.handlers.focus();
    assert.equal(p.list.hidden, false);
    assert.equal(p.cities.length, 3);
    p.input.handlers.keydown(p.event({ key: 'ArrowDown' }));
    assert.equal(p.input.attributes['aria-activedescendant'], 'city-0');
    p.input.handlers.keydown(p.event({ key: 'Enter' }));
    assert.equal(p.input.value, 'Oslo');
    const url = p.navigations[0][0];
    assert.equal(url.searchParams.get('city'), 'Oslo');
    assert.equal(url.searchParams.has('query'), false);
    assert.equal(url.searchParams.get('type'), 'Drone');
    assert.equal(url.searchParams.get('eligible'), 'true');
    assert.equal(url.searchParams.get('mine'), 'true');
    assert.equal(p.list.hidden, true);
});

test('resource filters expose only By, type and status with hover and keyboard selection styling', () => {
    const view = read('Views/Home/Verify.cshtml');
    assert.match(view, /<label for="resourceCity">By<\/label>/);
    assert.doesNotMatch(view, /By \/ sted|Navn eller sted|(?:id|name|for)="query"|asp-route-query|ViewBag\.Query/);
    const css = read('wwwroot/css/resource-management.css');
    assert.match(css, /\[role="option"\]:hover:not\(\[aria-selected="true"\]\)\s*\{[^}]*background:/);
    assert.match(css, /\[aria-selected="true"\]\s*\{[^}]*background:[^}]*outline: 2px solid/);
});

test('delete confirmation is viewport centered and scrollable on small screens', () => {
    const css = read('wwwroot/css/resource-management.css');
    const dialog = css.match(/\.resource-delete-dialog\s*\{([^}]+)\}/)[1];
    assert.match(dialog, /position: fixed;/);
    assert.match(dialog, /inset: 0;/);
    assert.match(dialog, /margin: auto;/);
    assert.match(dialog, /max-height: calc\(100dvh - 3rem\);/);
    assert.match(dialog, /overflow: auto;/);
    assert.match(dialog, /background: var\(--surface\);/);
    assert.match(dialog, /color: var\(--text\);/);
});

test('city suggestions inherit dark surfaces and retain visible hover and keyboard focus', () => {
    const css = read('wwwroot/css/resource-management.css');
    const list = css.match(/\.resource-city-options\s*\{([^}]+)\}/)[1];
    assert.match(list, /background: var\(--surface\)/);
    assert.match(list, /color: var\(--text\)/);
    assert.match(css, /:hover:not\(\[aria-selected="true"\]\)[^}]+background: var\(--hover\)/);
    assert.match(css, /\[aria-selected="true"\][^}]+outline: 2px solid var\(--accent\)/);
});

test('city click filters immediately; Escape dismisses list without closing outer dialog; outside focus closes safely', () => {
    const p = setup();
    p.input.handlers.focus();
    p.input.handlers.keydown(p.event({ key: 'ArrowUp' }));
    assert.equal(p.input.attributes['aria-activedescendant'], 'city-2');
    p.input.handlers.input();
    assert.equal(p.input.attributes['aria-activedescendant'], undefined);
    const escape = p.event({ key: 'Escape' });
    p.input.handlers.keydown(escape);
    assert.equal(escape.stopped, true);
    assert.equal(p.list.hidden, true);
    p.input.handlers.focus();
    p.root.handlers.focusout(p.event({ relatedTarget: {} }));
    assert.equal(p.list.hidden, true);
    p.input.handlers.focus();
    p.document.handlers.pointerdown(p.event({ target: {} }));
    assert.equal(p.list.hidden, true);
    p.list.handlers.click(p.event({ target: p.cities[2] }));
    assert.equal(p.navigations[0][0].searchParams.get('city'), 'Ålesund');
});

test('standalone city selection fetches partial results instead of navigating the document', () => {
    const p = setup({ navigate: false });
    p.list.handlers.click(p.event({ target: p.cities[1] }));
    assert.equal(p.requests.length, 1);
    assert.equal(p.requests[0].url.searchParams.get('city'), 'Bergen');
    assert.equal(p.requests[0].url.searchParams.get('fragment'), 'true');
    assert.equal(p.requests[0].url.searchParams.has('overlay'), false);
    assert.equal(p.requests[0].options.credentials, 'same-origin');
});

test('delete confirmation uses safe text, version and antiforgery form, cancel and Escape restore focus', async () => {
    const p = setup();
    p.root.handlers.click(p.event({ target: p.link }));
    assert.equal(p.dialog.open, true);
    assert.equal(p.nodes['#resourceDeleteName'].textContent, '<img> Drone');
    assert.equal(p.deleteForm.elements.version.value, 'current-version');
    assert.equal(p.document.activeElement, p.nodes['[data-delete-cancel]']);
    p.nodes['[data-delete-cancel]'].handlers.click();
    await settle();
    assert.equal(p.document.activeElement, p.link);
    p.root.handlers.click(p.event({ target: p.link }));
    const escape = p.event();
    p.dialog.handlers.cancel(escape);
    assert.equal(escape.prevented, undefined, 'native Escape remains enabled');
    p.dialog.close();
    await settle();
    assert.equal(p.document.activeElement, p.link);
});

test('delete backdrop cancels only the topmost dialog without submitting or bubbling to the overlay', async () => {
    const p = setup();
    p.root.handlers.click(p.event({ target: p.link }));
    const outside = () => p.event({ target: p.dialog, clientX: 100, clientY: 300 });
    const down = outside();
    const click = outside();
    p.dialog.handlers.pointerdown(down);
    p.dialog.handlers.click(click);
    await settle();
    assert.equal(down.stopped, true);
    assert.equal(click.stopped, true);
    assert.equal(p.dialog.open, false);
    assert.equal(p.document.activeElement, p.link);
    assert.equal(p.requests.length, 0);

    p.root.handlers.click(p.event({ target: p.link }));
    const padding = p.event({ target: p.dialog, clientX: 310, clientY: 210 });
    const inner = p.event({ target: p.deleteForm, clientX: 400, clientY: 300 });
    for (const inside of [padding, inner]) {
        p.dialog.handlers.pointerdown(inside);
        p.dialog.handlers.click(inside);
        assert.equal(p.dialog.open, true);
        p.dialog.handlers.pointerdown(inside);
        p.dialog.handlers.click(outside());
        assert.equal(p.dialog.open, true, 'dragging out does not dismiss');
    }
    p.dialog.handlers.pointerdown(outside());
    p.dialog.handlers.click(padding);
    assert.equal(p.dialog.open, true);
    p.dialog.handlers.pointerdown(outside());
    p.dialog.handlers.pointercancel();
    p.dialog.handlers.click(outside());
    assert.equal(p.dialog.open, true, 'cancelled gestures do not dismiss');
    p.dialog.nestedDialog = {};
    p.dialog.handlers.pointerdown(outside());
    p.dialog.handlers.click(outside());
    assert.equal(p.dialog.open, true);
    assert.equal(p.requests.length, 0);
});

test('delete submission cannot be cancelled by backdrop, Escape or repeated clicks', async () => {
    const p = setup();
    p.root.handlers.click(p.event({ target: p.link }));
    const submitted = p.deleteForm.handlers.submit(p.event());
    await p.deleteForm.handlers.submit(p.event());
    const outside = p.event({ target: p.dialog, clientX: 100, clientY: 300 });
    p.dialog.handlers.pointerdown(outside);
    p.dialog.handlers.click(outside);
    p.nodes['[data-delete-cancel]'].handlers.click();
    const escape = p.event();
    p.dialog.handlers.cancel(escape);
    assert.equal(escape.prevented, true);
    assert.equal(p.dialog.open, true);
    assert.equal(p.requests.length, 1);
    p.requests[0].reject(new Error('Offline'));
    await submitted;
    p.dialog.handlers.pointerdown(outside);
    p.dialog.handlers.click(outside);
    assert.equal(p.dialog.open, false, 'backdrop works again after a failed request');
});

test('successful JSON deletion updates resource count, city counts and map event without navigation', async () => {
    const p = setup();
    p.root.handlers.click(p.event({ target: p.link }));
    const submitted = p.deleteForm.handlers.submit(p.event());
    assert.equal(p.requests[0].options.method, 'POST');
    assert.equal(p.requests[0].options.headers.Accept, 'application/json');
    assert.equal(p.requests[0].options.body.form, p.deleteForm);
    p.requests[0].resolve({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ deletedId: 42 }) });
    await submitted;
    await settle();
    assert.equal(p.row.isConnected, false);
    assert.equal(p.cities[0].dataset.count, '1');
    assert.equal(p.nodes['.count-badge'].textContent, 0);
    assert.equal(p.events[0].type, 'resource-deleted');
    assert.equal(p.events[0].detail.id, 42);
    assert.equal(p.navigations.length, 0);
    assert.equal(p.dialog.open, false);
    assert.equal(p.document.activeElement, p.nodes['.resource-management-status']);
});

test('conflict, auth redirect and network failures retain resource and report accessible errors', async () => {
    for (const failure of ['conflict', 'redirect', 'network']) {
        const p = setup();
        p.root.handlers.click(p.event({ target: p.link }));
        const submitted = p.deleteForm.handlers.submit(p.event());
        if (failure === 'network') p.requests[0].reject(new Error('Offline'));
        else p.requests[0].resolve({
            ok: failure === 'redirect', redirected: failure === 'redirect',
            headers: { get: () => 'application/json' }, json: async () => ({ error: 'Endret samtidig' })
        });
        await submitted;
        assert.equal(p.row.isConnected, true);
        assert.equal(p.dialog.open, true);
        assert.ok(p.nodes['.resource-delete-error'].textContent);
        assert.equal(p.document.activeElement, p.nodes['.resource-delete-error']);
        assert.equal(p.nodes['[data-delete-cancel]'].disabled, false);
        assert.equal(p.events.length, 0);
    }
});

test('map deletion removes marker/sidebar data without touching center, zoom, filters or registration point', () => {
    const source = read('Views/Home/Map.cshtml');
    const listener = source.match(/document\.addEventListener\('resource-deleted', function \(event\) \{([\s\S]*?)\n    \}\);/)[1];
    const item = { resource: { id: 42 }, marker: {} };
    const retained = { resource: { id: 7 }, marker: {} };
    let removed = 0;
    const context = {
        event: { detail: { id: 42 } },
        savedMarkers: [item, retained], savedResources: [{ id: 42 }, { id: 7 }],
        resourceCards: [{ dataset: { resourceId: '42' }, remove() { removed++; } },
            { dataset: { resourceId: '7' }, hidden: true }],
        map: { removeLayer(marker) { assert.equal(marker, item.marker); } },
        document: { getElementById() { return {}; } }
    };
    vm.runInNewContext(listener, context);
    assert.equal(removed, 1);
    assert.equal(context.savedMarkers[0], retained);
    assert.equal(context.savedResources.length, 1);
    assert.equal(context.resourceCards.length, 1);
    assert.doesNotMatch(listener, /setView|setZoom|setTypeVisible|setLocation|registration|resourceFilter/);
    const view = read('Views/Home/Verify.cshtml');
    assert.match(view, /@Html.AntiForgeryToken\(\)/);
    assert.match(view, /resource-delete-error" role="alert"/);
});
