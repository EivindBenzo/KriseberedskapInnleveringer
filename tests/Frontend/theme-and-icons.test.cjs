const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync, existsSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');
const { inflateSync } = require('node:zlib');

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
    assert.equal(page.elements.themeLabel.textContent, 'Mørk modus');
    const next = loadTheme(page.storage.value);
    assert.equal(next.document.documentElement.dataset.theme, 'dark');
    page.events.click();
    assert.equal(page.storage.value, 'light');
    assert.equal(page.elements.themeLabel.textContent, 'Lys modus');
});

test('blocked storage does not break the default or toggle', () => {
    const page = loadTheme('dark', true);
    assert.equal(page.document.documentElement.dataset.theme, 'light');
    page.events.DOMContentLoaded();
    page.events.click();
    assert.equal(page.document.documentElement.dataset.theme, 'dark');
});

test('current-mode label and icon agree while accessible action and persisted reload follow each toggle', () => {
    for (const saved of ['light', 'dark']) {
        const page = loadTheme(saved);
        page.events.DOMContentLoaded();
        for (let i = 0; i < 3; i++) {
            const dark = page.document.documentElement.dataset.theme === 'dark';
            const action = dark ? 'Bytt til lys modus' : 'Bytt til mørk modus';
            assert.equal(page.elements.themeLabel.textContent, dark ? 'Mørk modus' : 'Lys modus');
            assert.equal(page.elements.themeIcon.textContent, dark ? '☾' : '☀');
            assert.equal(page.button.attributes['aria-pressed'], String(dark));
            assert.equal(page.button.attributes['aria-label'], action);
            assert.equal(page.button.attributes.title, action);
            const reload = loadTheme(page.storage.value);
            reload.events.DOMContentLoaded();
            assert.equal(reload.elements.themeLabel.textContent, page.elements.themeLabel.textContent);
            page.events.click();
            assert.equal(page.storage.value, dark ? 'light' : 'dark');
        }
    }
});

test('compact icons clamp and scale with zoom while marker, legend and overview mapping stay identical', () => {
    const context = {
        document: { currentScript: { dataset: { iconBase: '/images/resources/' } }, querySelectorAll: () => [] },
        window: {},
        L: { icon: options => options }
    };
    vm.runInNewContext(source('resource-icons.js'), context);
    const api = context.window.ResourceIcons;
    const mapping = [
        ['Kjøretøy', 'car'], ['Maskin', 'excavator'], ['Drone', 'drone'],
        ['Generator', 'generator'], ['Personell', 'personnel'], ['Utstyr', 'toolbox']
    ];
    assert.deepEqual(Array.from(api.entries, entry => [entry.type, entry.file]), mapping);
    assert.equal(api.sizeForZoom(), 32);
    for (const [zoom, size] of [[-10, 28], [0, 28], [8, 28], [10, 30], [12, 32], [14, 34], [16, 36], [30, 36], [NaN, 32], [Infinity, 32]]) {
        assert.equal(api.sizeForZoom(zoom), size);
        for (const [type, file] of [...mapping, ['not-a-type', 'unknown']]) {
            for (const preview of [false, true]) {
                const marker = api.markerOptions(type, preview, zoom);
                assert.equal(marker.icon.iconUrl, `/images/resources/${file}.svg?v=solid-20261025`);
                assert.equal(marker.icon.iconUrl, context.window.resourceIconUrl(type));
                assert.equal(marker.icon.iconSize[0], size);
                assert.equal(marker.icon.iconAnchor[0], size / 2);
                assert.equal(marker.icon.iconAnchor[1], size / 2);
                assert.equal(marker.icon.popupAnchor[1], -size / 2);
            }
        }
    }
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
        assert.ok(existsSync(resolve(root, 'wwwroot', saved.icon.iconUrl.slice(1).split('?')[0])));
        assert.equal(saved.icon.iconAnchor[0], saved.icon.iconSize[0] / 2);
    }
    for (const type of ['', null, '__proto__', '<svg onload=alert(1)>', '../../other']) {
        assert.equal(options(type).icon.iconUrl, '/images/resources/unknown.svg?v=solid-20261025');
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
        assert.equal(context.window.resourceIconUrl('Drone'), '/app/images/resources/drone.svg?v=solid-20261025');
        assert.equal(images[0].src, context.window.ResourceIcons.get('Drone').url);
        assert.equal(images[0].alt, 'Drone');
        assert.equal(images[1].alt, '', 'explicit decorative alt is preserved');
        assert.equal(images[2].src, '/app/images/resources/unknown.svg?v=solid-20261025');
        assert.equal(images[2].alt, 'Ukjent / ikke valgt ressurstype');
    }
});

test('all seven original glyphs share a solid monochrome grid without decorative badges or external content', () => {
    const glyphs = ['car', 'excavator', 'drone', 'generator', 'personnel', 'toolbox', 'unknown']
        .map(file => readFileSync(resolve(root, `wwwroot/images/resources/${file}.svg`), 'utf8'));
    assert.equal(new Set(glyphs).size, 7);
    for (const svg of glyphs) {
        assert.match(svg, /viewBox="0 0 48 48"/);
        assert.match(svg, /<path\b/);
        assert.deepEqual([...new Set(svg.match(/#[\da-f]{6}/gi))], ['#172554']);
        assert.doesNotMatch(svg, /<(?:circle|ellipse|rect|image|text|script|foreignObject|use)\b|\bstroke=|\b(?:href|style)=/i);
        assert.doesNotMatch(svg, /<path[^>]*d="M0[ ,]0h48v48|<path[^>]*d="M24[ ,]0/i);
    }
    assert.match(glyphs[2], /a7 5/g, 'quadcopter has actual rotor silhouettes');
    assert.equal((glyphs[4].match(/a[56] [56] 0 1 1/g) || []).length, 3, 'personnel depicts a group of three heads');
});

test('every rendered consumer loads the cache-versioned shared mapping, not private icon URLs', () => {
    for (const path of ['Views/Home/Map.cshtml', 'Views/Home/Verify.cshtml']) {
        const view = readFileSync(resolve(root, path), 'utf8');
        assert.match(view, /src="~\/js\/resource-icons\.js" asp-append-version="true" data-icon-base="@Url\.Content\("~\/images\/resources\/"\)"/);
        assert.match(view, /FileVersionProvider\.AddFileVersionToPath\(Context\.Request\.PathBase,/);
        assert.match(view, /data-icon-urls="@System\.Text\.Json\.JsonSerializer\.Serialize\(resourceIconUrls\)"/);
        assert.match(view, /<img data-resource-icon="@r\.ResourceType" alt="" width="32" height="32"/);
        assert.doesNotMatch(view, /images\/resources\/(?:car|excavator|drone|generator|personnel|toolbox|unknown)\.svg/);
    }
    const controls = source('map-controls.js');
    assert.match(controls, /ResourceIcons/);
    assert.doesNotMatch(controls, /images\/resources\/|\.svg['"]/);
    assert.match(readFileSync(resolve(root, 'Views/Home/Verify.cshtml'), 'utf8'), /data-resource-icon="@r\.ResourceType"/);
});

test('server-generated per-asset versions flow through markers, legends, cards and unknown fallback under a path base', () => {
    const files = ['car', 'excavator', 'drone', 'generator', 'personnel', 'toolbox', 'unknown'];
    const urls = Object.fromEntries(files.map(file => [file, `/app/images/resources/${file}.svg?v=${file}-content-hash`]));
    const images = [
        { dataset: { resourceIcon: 'Drone' }, alt: '', hasAttribute: () => true },
        { dataset: { resourceIcon: 'invalid' }, hasAttribute: () => false }
    ];
    const context = {
        document: {
            currentScript: { dataset: { iconBase: '/app/images/resources/', iconUrls: JSON.stringify(urls) } },
            querySelectorAll: () => images
        },
        window: {},
        L: { icon: options => options }
    };
    vm.runInNewContext(source('resource-icons.js'), context);
    const api = context.window.ResourceIcons;
    for (const entry of [...api.entries, { type: 'invalid', file: 'unknown' }]) {
        assert.equal(api.get(entry.type).url, urls[entry.file]);
        assert.equal(api.markerOptions(entry.type).icon.iconUrl, urls[entry.file]);
        assert.equal(api.markerOptions(entry.type, true).icon.iconUrl, urls[entry.file]);
        assert.equal(context.window.resourceIconUrl(entry.type), urls[entry.file]);
    }
    assert.equal(images[0].src, urls.drone);
    assert.equal(images[0].alt, '', 'cards already show the type in adjacent text');
    assert.equal(images[1].src, urls.unknown);
    const verify = readFileSync(resolve(root, 'Views/Home/Verify.cshtml'), 'utf8');
    assert.match(verify, /IFileVersionProvider FileVersionProvider/);
    assert.match(verify, /FileVersionProvider\.AddFileVersionToPath\(Context\.Request\.PathBase,/);
    assert.match(verify, /data-icon-urls="@System\.Text\.Json\.JsonSerializer\.Serialize\(resourceIconUrls\)"/);
});

test('header corrects flattened artwork to the reference crest proportions with a tightly cropped transparent color canvas', () => {
    const png = readFileSync(resolve(root, 'wwwroot/images/forsvaret-logo.png'));
    assert.equal(png.subarray(1, 4).toString(), 'PNG');
    const width = png.readUInt32BE(16), height = png.readUInt32BE(20);
    assert.equal(width, 588);
    assert.equal(height, 721);
    assert.ok(Math.abs(width / height - 406 / 498) < 0.001,
        'matches the 406×498 reference emblem, not the flattened 1403×721 source');
    assert.equal(png[25], 6, 'RGBA retains the original transparent canvas');
    const chunks = [];
    for (let offset = 8; offset < png.length;) {
        const length = png.readUInt32BE(offset);
        if (png.toString('ascii', offset + 4, offset + 8) === 'IDAT') chunks.push(png.subarray(offset + 8, offset + 8 + length));
        offset += length + 12;
    }
    const encoded = inflateSync(Buffer.concat(chunks));
    const pixels = Buffer.alloc(width * height * 4), stride = width * 4;
    const paeth = (a, b, c) => {
        const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    };
    for (let y = 0; y < height; y++) {
        const filter = encoded[y * (stride + 1)];
        assert.ok(filter <= 4);
        for (let x = 0; x < stride; x++) {
            const index = y * stride + x;
            const left = x >= 4 ? pixels[index - 4] : 0;
            const up = y ? pixels[index - stride] : 0;
            const upperLeft = y && x >= 4 ? pixels[index - stride - 4] : 0;
            const prediction = [0, left, up, Math.floor((left + up) / 2), paeth(left, up, upperLeft)][filter];
            pixels[index] = (encoded[y * (stride + 1) + x + 1] + prediction) & 255;
        }
    }
    let red = 0, gold = 0, transparent = 0;
    const bounds = [width, height, 0, 0];
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
        const i = (y * width + x) * 4, [r, g, b, a] = pixels.subarray(i, i + 4);
        if (!a) { transparent++; continue; }
        if (r > 150 && g < 120 && b < 120) red++;
        if (r > 180 && g > 120 && b < 100) gold++;
        bounds[0] = Math.min(bounds[0], x); bounds[1] = Math.min(bounds[1], y);
        bounds[2] = Math.max(bounds[2], x); bounds[3] = Math.max(bounds[3], y);
    }
    assert.ok(red > 20000 && gold > 20000, 'retains the red shield and gold crown/lion/swords');
    assert.ok(transparent > width * height / 3, 'canvas outside the crest stays transparent');
    assert.ok(bounds[0] < 2 && bounds[1] < 2 && bounds[2] >= width - 2 && bounds[3] >= height - 2, 'canvas tightly bounds the artwork');
    for (const corner of [0, width - 1, width * (height - 1), width * height - 1]) assert.equal(pixels[corner * 4 + 3], 0);
    const layout = readFileSync(resolve(root, 'Views/Shared/_Layout.cshtml'), 'utf8');
    assert.match(layout, /<img src="~\/images\/forsvaret-logo\.png" asp-append-version="true"[^>]*width="588" height="721"/);
    const css = readFileSync(resolve(root, 'wwwroot/css/site.css'), 'utf8');
    assert.match(css, /\.nav-brand\s*\{[^}]*flex-shrink: 0;/);
    assert.match(css, /\.nav-brand img\s*\{[^}]*height: 80px;[^}]*width: auto;[^}]*object-fit: contain;/);
    assert.match(css, /@media[^{]*\{[\s\S]*\.nav-brand img\s*\{[^}]*height: 56px;[^}]*width: auto;/);
    assert.doesNotMatch(css, /\.nav-brand img\s*\{[^}]*(?:width:\s*\d|filter:\s*(?:grayscale|invert|brightness))/);
    assert.match(css, /\.resource-marker\s*\{[^}]*drop-shadow\(0 0 0\.75px #fff\)/);
    assert.match(css, /:root\[data-theme="dark"\] \.map-legend img,[^}]*filter: brightness\(0\) invert\(1\);/);
});
