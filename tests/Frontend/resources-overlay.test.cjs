const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

function setup({ supported = true } = {}) {
    const nodes = {};
    const classes = new Set();
    const requests = [];
    const focusCalls = [];
    let registered = false;
    let hydrated = 0;
    const document = {
        documentElement: { classList: { add: c => classes.add(c), remove: c => classes.delete(c) } },
        getElementById(id) { return nodes[id] ??= element(); },
        createElement(tag) {
            const node = element();
            if (tag === 'template') {
                node.content = {
                    querySelector(selector) {
                        return selector === '.resource-overview' && node.innerHTML.includes('resource-overview') ? {} : null;
                    }
                };
            }
            return node;
        }
    };
    function element() {
        return {
            handlers: {}, attributes: {}, children: [], dataset: {}, isConnected: true,
            addEventListener(name, callback) { this.handlers[name] = callback; },
            setAttribute(name, value) { this.attributes[name] = value; },
            removeAttribute(name) { delete this.attributes[name]; },
            hasAttribute(name) { return name in this.attributes; },
            append(node) { this.children.push(node); },
            replaceChildren(...children) { this.children = children; },
            focus() { document.activeElement = this; },
            getClientRects() { return [1]; },
            querySelectorAll() { return this.focusable || []; }
        };
    }
    const opener = document.getElementById('resourcesOverlayOpen');
    const dialog = document.getElementById('resourcesDialog');
    const content = document.getElementById('resourcesDialogContent');
    const back = document.getElementById('resourcesDialogBack');
    opener.href = 'https://example.test/Home/Resources';
    opener.focus();
    if (supported) dialog.showModal = () => { dialog.open = true; };
    dialog.close = () => {
        dialog.open = false;
        // Browsers queue close events, rather than firing synchronously.
        queueMicrotask(() => dialog.handlers.close());
    };
    const context = {
        document, window: { location: { href: 'https://example.test/Home/Map' },
            ResourceIcons: { initializeImages() { hydrated++; } } },
        URL, URLSearchParams, AbortController,
        FormData: class { constructor(form) { return form.values; } },
        fetch(url, options) {
            return new Promise((resolve, reject) => requests.push({ url, options, resolve, reject }));
        }
    };
    vm.runInNewContext(read('wwwroot/js/resources-overlay.js'), context);
    context.window.ResourcesOverlay.initialize({
        onFocus(...args) { focusCalls.push(args); },
        onRegister() { registered = true; content.focus(); }
    });
    function event(target = null, props = {}) {
        return { target, preventDefault() { this.prevented = true; }, ...props };
    }
    function link(attributes, dataset = {}) {
        const node = element();
        node.attributes = attributes;
        node.dataset = dataset;
        node.href = 'https://example.test/Home/Resources?mine=true';
        node.closest = () => node;
        return node;
    }
    return {
        document, dialog, content, opener, back, classes, requests, focusCalls, event, link,
        get registered() { return registered; }, get hydrated() { return hydrated; },
        open() {
            const e = event();
            opener.handlers.click(e);
            return e;
        }
    };
}
const settle = () => new Promise(resolve => setImmediate(resolve));
async function resolveListing(p, index = 0) {
    p.requests[index].resolve({ ok: true, text: async () => '<section class="resource-overview"></section>' });
    await settle();
}

test('opens a native modal, loads server resources, locks background scroll and restores focus on close', async () => {
    const p = setup();
    assert.equal(p.open().prevented, true);
    assert.equal(p.dialog.open, true);
    assert.equal(p.document.activeElement, p.back);
    assert.equal(p.classes.has('resources-overlay-open'), true);
    assert.equal(p.requests[0].url.searchParams.get('overlay'), 'true');
    assert.equal(p.requests[0].options.credentials, 'same-origin');
    assert.equal(p.content.attributes['aria-busy'], 'true');
    await resolveListing(p);
    assert.equal(p.hydrated, 1);
    assert.equal(p.content.attributes['aria-busy'], undefined);
    p.back.handlers.click();
    await settle();
    assert.equal(p.dialog.open, false);
    assert.equal(p.classes.size, 0);
    assert.equal(p.document.activeElement, p.opener);
    assert.equal(p.requests[0].options.signal.aborted, true);
});

test('native Escape close cleanup aborts loading and ignores late responses', async () => {
    const p = setup();
    p.open();
    p.dialog.close();
    await settle();
    await resolveListing(p);
    assert.equal(p.content.children.length, 0);
    assert.equal(p.hydrated, 0);
    assert.equal(p.document.activeElement, p.opener);
});

test('Tab and Shift+Tab wrap within visible dialog controls', () => {
    const p = setup();
    p.open();
    const last = p.link({});
    p.dialog.focusable = [p.back, last];
    const reverse = p.event(null, { key: 'Tab', shiftKey: true });
    p.dialog.handlers.keydown(reverse);
    assert.equal(reverse.prevented, true);
    assert.equal(p.document.activeElement, last);
    const forward = p.event(null, { key: 'Tab' });
    p.dialog.handlers.keydown(forward);
    assert.equal(p.document.activeElement, p.back);
});

test('tabs and GET filters stay in the overlay and cancel outdated requests', async () => {
    const p = setup();
    p.open();
    const tab = p.link({ 'data-resources-link': '' });
    const click = p.event(tab);
    p.content.handlers.click(click);
    assert.equal(click.prevented, true);
    assert.equal(p.requests[0].options.signal.aborted, true);
    assert.equal(p.requests[1].url.searchParams.get('mine'), 'true');
    await resolveListing(p, 1);
    assert.equal(p.document.activeElement, p.content);
    const filter = {
        action: 'https://example.test/Home/Resources',
        matches: selector => selector === '.filter-bar',
        values: [['mine', 'true'], ['query', 'Drone & bil'], ['type', 'Drone'], ['eligible', 'true']]
    };
    const submit = p.event(filter);
    p.content.handlers.submit(submit);
    assert.equal(submit.prevented, true);
    assert.equal(p.requests[2].url.searchParams.get('query'), 'Drone & bil');
    assert.equal(p.requests[2].url.searchParams.get('eligible'), 'true');
    assert.equal(p.requests[2].url.searchParams.get('overlay'), 'true');
});

test('Vis på kart and registration close first, then invoke existing map callbacks', async () => {
    const p = setup();
    p.open();
    p.content.handlers.click(p.event(p.link({ 'data-map-focus': '42' }, { mapFocus: '42', lat: '60', lng: '10' })));
    await settle();
    assert.equal(p.dialog.open, false);
    assert.deepEqual(p.focusCalls, [['42', 60, 10]]);
    p.open();
    p.content.handlers.click(p.event(p.link({ 'data-map-register': '' })));
    await settle();
    assert.equal(p.registered, true);
    assert.equal(p.document.activeElement, p.content, 'close cleanup must not steal focus from registration');
});

test('failed or redirected requests offer an accessible retry and standalone new-tab fallback', async () => {
    const p = setup();
    p.open();
    p.requests[0].resolve({ ok: true, redirected: true });
    await settle();
    const [error, retry, fallback] = p.content.children;
    assert.equal(error.attributes.role, 'alert');
    assert.equal(fallback.target, '_blank');
    assert.equal(new URL(fallback.href).searchParams.has('overlay'), false);
    retry.handlers.click();
    assert.equal(p.requests.length, 2);
    assert.equal(p.dialog.open, true);
});

test('unsupported dialogs and modified clicks retain ordinary standalone navigation', () => {
    assert.equal(setup({ supported: false }).opener.handlers.click, undefined);
    const p = setup();
    const click = p.event(null, { ctrlKey: true });
    p.opener.handlers.click(click);
    assert.equal(click.prevented, undefined);
    assert.equal(p.requests.length, 0);
});

test('overlay uses the authorized resource view, existing permission gates and antiforgery-protected workflows', () => {
    const controller = read('Controllers/HomeController.cs');
    assert.match(controller, /\[Authorize\]\s*public class HomeController/);
    assert.match(controller, /overlay \? PartialView\("Verify", model\) : View\("Verify", model\)/);
    const view = read('Views/Home/Verify.cshtml');
    assert.match(view, /ResourcePermissions.CanManage\(User, r.OwnerId\)/);
    assert.match(view, /User.IsInRole\("Admin"\)/);
    assert.match(view, /asp-action="ResetMap" method="post"/);
    assert.match(controller, /\[HttpPost\]\s*\[Authorize\(Roles = "Admin"\)\]\s*\[ValidateAntiForgeryToken\]\s*public async Task<IActionResult> ResetMap/);
    assert.match(view, /target="@actionTarget" rel="@@?\(overlay \? "noopener" : null\)"/);
    assert.match(view, /@if \(!overlay\)/);
    assert.doesNotMatch(view, /asp-route-overlay=/, 'modified tab clicks must open the standalone view');
    const map = read('Views/Home/Map.cshtml');
    assert.match(map, /<dialog[^>]+aria-labelledby="resourcesDialogTitle"/);
    assert.match(map, /aria-label="Tilbake til kartet"/);
    const css = read('wwwroot/css/resources-overlay.css');
    assert.match(css, /\.resources-dialog\s*\{[^}]*width: 80vw;/);
    assert.doesNotMatch(css, /60vw/);
    assert.match(css, /max-height: 85dvh/);
    assert.match(css, /overflow: auto/);
    assert.match(css, /@media \(max-width: 900px\)/);
});

test('only the standalone resources view includes Åpne kart, and popup cards have responsive columns', () => {
    const view = read('Views/Home/Verify.cshtml');
    assert.match(view, /@if \(!overlay\)\s*\{\s*<a asp-action="Map" data-map-back>Åpne kart<\/a>\s*\}/);
    assert.equal((view.match(/>Åpne kart<\/a>/g) || []).length, 1);
    const css = read('wwwroot/css/resources-overlay.css');
    assert.match(css, /\.resources-dialog-content \.resource-row\s*\{[^}]*grid-template-columns: minmax\(0, 1\.35fr\) minmax\(0, 1fr\) minmax\(10rem, \.85fr\)/);
    assert.match(css, /@media \(max-width: 900px\)[\s\S]*width: calc\(100vw - 1\.5rem\)[\s\S]*\.resource-row\s*\{[^}]*grid-template-columns: minmax\(0, 1fr\)/);
});
