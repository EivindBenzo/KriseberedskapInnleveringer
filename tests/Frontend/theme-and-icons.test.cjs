const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync, existsSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');

const root = resolve(__dirname, '../..');
const source = name => readFileSync(resolve(root, 'wwwroot/js', name), 'utf8');

function loadTheme(saved, blocked = false) {
    const events = {};
    const button = {
        hidden: true,
        attributes: {},
        setAttribute(key, value) { this.attributes[key] = value; },
        addEventListener(name, handler) { events[name] = handler; }
    };
    const elements = { themeToggle: button, themeIcon: {}, themeLabel: {} };
    const document = {
        documentElement: { dataset: {} },
        addEventListener(name, handler) { events[name] = handler; },
        getElementById(id) { return elements[id]; }
    };
    const storage = {
        value: saved,
        getItem() { if (blocked) throw new Error('Blocked'); return this.value; },
        setItem(key, value) { if (blocked) throw new Error('Blocked'); this.value = value; }
    };
    vm.runInNewContext(source('theme.js'), { document, localStorage: storage });
    return { document, events, elements, storage, button };
}

test('light is the early default; only an explicit saved dark preference enables dark', () => {
    for (const saved of [null, '', 'light', 'invalid', 'DARK']) {
        assert.equal(loadTheme(saved).document.documentElement.dataset.theme, 'light');
    }
    assert.equal(loadTheme('dark').document.documentElement.dataset.theme, 'dark');
});

test('toggle updates state and Norwegian label and persists across page loads', () => {
    const page = loadTheme(null);
    page.events.DOMContentLoaded();
    assert.equal(page.button.hidden, false);
    assert.equal(page.button.attributes['aria-pressed'], 'false');
    page.events.click();
    assert.equal(page.document.documentElement.dataset.theme, 'dark');
    assert.equal(page.button.attributes['aria-pressed'], 'true');
    assert.equal(page.elements.themeLabel.textContent, 'Lys modus');
    const next = loadTheme(page.storage.value);
    assert.equal(next.document.documentElement.dataset.theme, 'dark');
    page.events.click();
    assert.equal(page.storage.value, 'light');
    assert.equal(page.elements.themeLabel.textContent, 'Mørk modus');
});

test('blocked storage does not break the default or toggle', () => {
    const page = loadTheme('dark', true);
    assert.equal(page.document.documentElement.dataset.theme, 'light');
    page.events.DOMContentLoaded();
    page.events.click();
    assert.equal(page.document.documentElement.dataset.theme, 'dark');
});

test('six types use distinct local SVGs; unknown types use a safe fallback', () => {
    const context = {
        document: { currentScript: { dataset: { iconBase: '/images/resources/' } }, querySelectorAll: () => [] },
        window: {},
        L: { icon: options => options }
    };
    vm.runInNewContext(source('resource-icons.js'), context);
    const options = context.window.resourceMarkerOptions;
    const types = ['Kjøretøy', 'Maskin', 'Drone', 'Generator', 'Personell', 'Utstyr'];
    const urls = types.map(type => options(type).icon.iconUrl);
    assert.equal(new Set(urls).size, 6);
    for (const type of types) {
        const saved = options(type);
        const preview = options(type, true);
        assert.equal(saved.icon.iconUrl, preview.icon.iconUrl);
        assert.equal(saved.alt, type);
        assert.equal(saved.title, type);
        assert.match(preview.icon.className, /resource-marker-preview/);
        assert.ok(existsSync(resolve(root, 'wwwroot', saved.icon.iconUrl.slice(1))));
        assert.equal(saved.icon.iconAnchor[0], saved.icon.iconSize[0] / 2);
    }
    for (const type of ['', null, '__proto__', '<svg onload=alert(1)>', '../../other']) {
        assert.equal(options(type).icon.iconUrl, '/images/resources/unknown.svg');
        assert.equal(options(type).alt, 'Ukjent / ikke valgt ressurstype');
    }
});

test('overview icon URLs and image initialization share the mapping without requiring Leaflet', () => {
    for (const readyState of ['loading', 'complete']) {
        const events = {};
        const images = [
            { dataset: { resourceIcon: 'Drone' }, hasAttribute: () => false },
            { dataset: { resourceIcon: 'Utstyr' }, alt: '', hasAttribute: () => true },
            { dataset: { resourceIcon: '../../invalid' }, hasAttribute: () => false }
        ];
        const context = {
            document: {
                readyState,
                currentScript: { dataset: { iconBase: '/app/images/resources/' } },
                querySelectorAll(selector) {
                    assert.equal(selector, 'img[data-resource-icon]');
                    return images;
                },
                addEventListener(name, callback) { events[name] = callback; }
            },
            window: {}
        };
        vm.runInNewContext(source('resource-icons.js'), context);
        if (readyState === 'loading') {
            assert.equal(images[0].src, undefined);
            events.DOMContentLoaded();
        }
        assert.equal(context.window.resourceIconUrl('Drone'), '/app/images/resources/drone.svg');
        assert.equal(images[0].src, context.window.ResourceIcons.get('Drone').url);
        assert.equal(images[0].alt, 'Drone');
        assert.equal(images[1].alt, '', 'explicit decorative alt is preserved');
        assert.equal(images[2].src, '/app/images/resources/unknown.svg');
        assert.equal(images[2].alt, 'Ukjent / ikke valgt ressurstype');
    }
});
