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
                assert.equal(marker.icon.iconUrl, `/images/resources/${file}${preview ? '' : '-unavailable'}.svg?v=outline-emerald-20261026`);
                assert.equal(marker.icon.iconUrl, context.window.resourceIconUrl(type, preview ? null : {}));
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
        assert.notEqual(saved.icon.iconUrl, preview.icon.iconUrl);
        assert.equal(saved.alt, type + ' · Kan ikke forespørres nå · Ukjent status');
        assert.equal(saved.title, saved.alt);
        assert.match(preview.icon.className, /resource-marker-preview/);
        assert.ok(existsSync(resolve(root, 'wwwroot', saved.icon.iconUrl.slice(1).split('?')[0])));
        assert.equal(saved.icon.iconAnchor[0], saved.icon.iconSize[0] / 2);
    }
    for (const type of ['', null, '__proto__', '<svg onload=alert(1)>', '../../other']) {
        assert.equal(options(type).icon.iconUrl, '/images/resources/unknown-unavailable.svg?v=outline-emerald-20261026');
        assert.equal(options(type).alt, 'Ukjent / ikke valgt ressurstype · Kan ikke forespørres nå · Ukjent status');
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
            events.DOMContentLoaded({ type: 'DOMContentLoaded' });
        }
        assert.equal(context.window.resourceIconUrl('Drone'), '/app/images/resources/drone.svg?v=outline-emerald-20261026');
        assert.equal(images[0].src, context.window.ResourceIcons.get('Drone', {}).url);
        assert.equal(images[0].alt, 'Drone · Kan ikke forespørres nå · Ukjent status');
        assert.equal(images[1].alt, '', 'explicit decorative alt is preserved');
        assert.equal(images[2].src, '/app/images/resources/unknown-unavailable.svg?v=outline-emerald-20261026');
        assert.equal(images[2].alt, 'Ukjent / ikke valgt ressurstype · Kan ikke forespørres nå · Ukjent status');
    }
});

test('all seven natural outline glyphs share slim rounded strokes and a map-independent backplate', () => {
    const glyphs = ['car', 'excavator', 'drone', 'generator', 'personnel', 'toolbox', 'unknown']
        .map(file => readFileSync(resolve(root, `wwwroot/images/resources/${file}.svg`), 'utf8'));
    assert.equal(new Set(glyphs).size, 7);
    for (const svg of glyphs) {
        assert.match(svg, /viewBox="0 0 48 48"/);
        assert.match(svg, /<path\b/);
        assert.deepEqual([...new Set(svg.match(/#[\da-f]{6}/gi))], ['#f8fafc', '#cbd5e1', '#172554']);
        assert.match(svg, /<g fill="none" stroke="#172554" stroke-width="2\.25" stroke-linecap="round" stroke-linejoin="round">/);
        assert.match(svg, /<rect x="1" y="1" width="46" height="46" rx="10" fill="#f8fafc" fill-opacity="\.96" stroke="#cbd5e1"\/>/);
        assert.equal((svg.match(/<rect\b/g) || []).length, 1, 'one subtle backplate, no stacked badge');
        assert.doesNotMatch(svg, /<(?:image|text|script|foreignObject|use)\b|\b(?:href|style)=/i);
    }
    assert.equal((glyphs[2].match(/<ellipse\b/g) || []).length, 4, 'quadcopter has four individual rotors');
    assert.equal((glyphs[4].match(/<circle\b/g) || []).length, 3, 'personnel depicts a group of three heads');
});

test('every rendered consumer loads the cache-versioned shared mapping, not private icon URLs', () => {
    for (const path of ['Views/Home/Map.cshtml', 'Views/Home/Verify.cshtml']) {
        const view = readFileSync(resolve(root, path), 'utf8');
        assert.match(view, /src="~\/js\/resource-icons\.js" asp-append-version="true" data-icon-base="@Url\.Content\("~\/images\/resources\/"\)"/);
        assert.match(view, /FileVersionProvider\.AddFileVersionToPath\(Context\.Request\.PathBase,/);
        assert.match(view, /data-icon-urls="@System\.Text\.Json\.JsonSerializer\.Serialize\(resourceIconUrls\)"/);
        assert.match(view, /<img\b(?=[^>]*data-resource-icon="@r\.ResourceType")(?=[^>]*alt="")(?=[^>]*width="32")(?=[^>]*height="32")/);
        assert.doesNotMatch(view, /images\/resources\/(?:car|excavator|drone|generator|personnel|toolbox|unknown)\.svg/);
    }
    const controls = source('map-controls.js');
    assert.match(controls, /ResourceIcons/);
    assert.doesNotMatch(controls, /images\/resources\/|\.svg['"]/);
    assert.match(readFileSync(resolve(root, 'Views/Home/Verify.cshtml'), 'utf8'), /data-resource-icon="@r\.ResourceType"/);
    assert.match(readFileSync(resolve(root, 'Views/Home/Verify.cshtml'), 'utf8'),
        /asp-controller="Operations" asp-action="Index" asp-fragment="needs"/);
});

test('server-generated per-asset versions flow through markers, legends, cards and unknown fallback under a path base', () => {
    const files = ['car', 'excavator', 'drone', 'generator', 'personnel', 'toolbox', 'unknown']
        .flatMap(file => [file, file + '-available', file + '-unavailable']);
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
        assert.equal(api.markerOptions(entry.type).icon.iconUrl, urls[entry.file + '-unavailable']);
        assert.equal(api.markerOptions(entry.type, true).icon.iconUrl, urls[entry.file]);
        assert.equal(context.window.resourceIconUrl(entry.type), urls[entry.file]);
    }
    assert.equal(images[0].src, urls['drone-unavailable']);
    assert.equal(images[0].alt, '', 'cards already show the type in adjacent text');
    assert.equal(images[1].src, urls['unknown-unavailable']);
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
    assert.match(css, /\.resource-marker,[^}]*drop-shadow\(0 0 0\.75px #fff\)[^}]*drop-shadow\(0 1px 1px #0f172a\)/);
    assert.doesNotMatch(css, /:root\[data-theme="dark"\] \.map-legend img,[^}]*filter: brightness\(0\) invert\(1\);/);
});

function loadResourceIcons(extra = {}) {
    const context = {
        document: {
            currentScript: { dataset: { iconBase: '/images/resources/' } },
            querySelectorAll: () => []
        },
        window: {},
        L: { icon: options => options },
        ...extra
    };
    vm.runInNewContext(source('resource-icons.js'), context);
    return context.window.ResourceIcons;
}

function eligibleStatus(overrides = {}) {
    return {
        isEligible: true, availability: 'Tilgjengelig', verification: 'Godkjent',
        verifiedAtUtc: '2020-01-01T00:00:00Z', verificationExpiresAtUtc: '2099-01-01T00:00:00Z',
        statusText: 'Kan forespørres nå · Tilgjengelig · Godkjent', ...overrides
    };
}

test('only availability determines icon color; preview stays neutral', () => {
    const api = loadResourceIcons();
    const eligible = eligibleStatus();
    assert.equal(api.get('Drone', eligible).url, '/images/resources/drone-available.svg?v=outline-emerald-20261026');
    assert.equal(api.get('Drone', eligible, true).url, api.get('Drone').url);
    assert.equal(api.markerOptions('Drone', true, 12, eligible).title, 'Ny ressurs (ikke lagret): Drone');
    for (const overrides of [
        { isEligible: false }, { isEligible: 'true' }, { isEligible: undefined },
        { verification: 'Venter på verifisering' }, { verification: 'Avvist' },
        { verification: 'Ukjent verifiseringsstatus' }, { verifiedAtUtc: null },
        { verifiedAtUtc: '2098-01-01T00:00:00Z' }, { verificationExpiresAtUtc: null },
        { verificationExpiresAtUtc: 'invalid' }, { verificationExpiresAtUtc: '2020-01-01T00:00:00Z' }
    ]) {
        const status = eligibleStatus(overrides);
        assert.equal(api.get('Drone', status).state, 'available', JSON.stringify(overrides));
        assert.match(api.get('Drone', status).url, /drone-available\.svg/);
    }
    for (const availability of ['Utilgjengelig', 'Reservert', 'Tildelt', 'Ukjent']) {
        assert.equal(api.get('Drone', eligibleStatus({ availability })).state, 'unavailable');
    }
    assert.equal(api.get('Drone', { availability: 'Tilgjengelig' }).state, 'available');
    assert.equal(api.get('unknown', eligible).state, 'available');
    assert.equal(api.get('Drone', {}).state, 'unavailable');
    assert.match(api.markerOptions('Drone').icon.iconUrl, /drone-unavailable\.svg/, 'missing saved state fails closed');
    assert.equal(api.get('Drone').state, 'neutral', 'type legends have no operational status');
    assert.match(api.markerOptions('Drone', false, 12, eligible).alt, /Kan forespørres nå/);
});

test('expiry boundary keeps availability color and updates the accessible reason without reloading', () => {
    const calls = [];
    let now = Date.parse('2026-10-26T12:00:00Z');
    class Clock extends Date { static now() { return now; } }
    const api = loadResourceIcons({
        Date: Clock,
        window: { setTimeout(callback, delay) { calls.push({ callback, delay }); } }
    });
    const status = eligibleStatus({ verificationExpiresAtUtc: '2026-10-26T12:00:01Z' });
    assert.equal(api.get('Drone', status).state, 'available');
    let refreshes = 0;
    api.watchAvailability(() => { refreshes++; }, [status]);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].delay, 1001);
    now += 1000;
    assert.equal(api.get('Drone', status).state, 'available');
    assert.match(api.markerOptions('Drone', false, 12, status).alt, /Godkjenning utløpt/);
    calls[0].callback();
    assert.equal(refreshes, 1);
    assert.equal(calls.length, 1, 'expired status does not reschedule forever');
});

test('availability variants retain identical outlines and backplates with saturated emerald or red strokes', () => {
    const files = ['car', 'excavator', 'drone', 'generator', 'personnel', 'toolbox', 'unknown'];
    for (const file of files) {
        const neutral = readFileSync(resolve(root, `wwwroot/images/resources/${file}.svg`), 'utf8').trim();
        for (const [state, color] of [['available', '#047857'], ['unavailable', '#b91c1c']]) {
            const svg = readFileSync(resolve(root, `wwwroot/images/resources/${file}-${state}.svg`), 'utf8').trim();
            assert.equal(svg.replaceAll(color, '#172554'), neutral);
            assert.match(svg, new RegExp(`<g fill="none" stroke="${color}" stroke-width="2\\.25"`));
            assert.doesNotMatch(svg, /<(?:image|script|foreignObject|use)\b/i);
        }
    }
    const urls = { 'drone-available': '/app/images/resources/drone-available.svg?v=content-hash' };
    const api = loadResourceIcons({
        document: {
            currentScript: { dataset: { iconBase: '/app/images/resources/', iconUrls: JSON.stringify(urls) } },
            querySelectorAll: () => []
        }
    });
    assert.equal(api.get('Drone', eligibleStatus()).url, urls['drone-available']);
});

test('slim status strokes retain accessible contrast on the shared backplate over light and dark tiles', () => {
    const luminance = rgb => rgb.map(channel => {
        const value = channel / 255;
        return value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4;
    }).reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
    for (const tile of [[0, 0, 0], [255, 255, 255], [34, 139, 34]]) {
        const plate = [248, 250, 252].map((channel, index) => .96 * channel + .04 * tile[index]);
        for (const stroke of [[4, 120, 87], [185, 28, 28], [23, 37, 84]]) {
            const contrast = (luminance(plate) + .05) / (luminance(stroke) + .05);
            assert.ok(contrast >= 4.5, `${stroke} has ${contrast.toFixed(2)}:1 contrast on ${tile}`);
        }
    }
});

test('cards keep concise availability and approval while refreshing approval expiry', () => {
    let now = Date.parse('2026-10-26T12:00:00Z');
    class Clock extends Date { static now() { return now; } }
    const callbacks = [];
    const text = {};
    const status = eligibleStatus({ verificationExpiresAtUtc: '2026-10-26T12:00:01Z' });
    const image = {
        dataset: { resourceIcon: 'Drone', resourceStatus: JSON.stringify(status) },
        alt: '',
        hasAttribute: () => true,
        closest: () => ({ querySelector: () => text })
    };
    const automaticImage = {
        dataset: { resourceIcon: 'Drone', resourceStatus: JSON.stringify(status) },
        hasAttribute: () => Object.hasOwn(automaticImage, 'alt')
    };
    loadResourceIcons({
        Date: Clock,
        document: {
            currentScript: { dataset: { iconBase: '/app/images/resources/' } },
            querySelectorAll: () => [image, automaticImage]
        },
        window: { setTimeout(callback) { callbacks.push(callback); } }
    });
    assert.match(image.src, /drone-available\.svg/);
    assert.equal(image.dataset.resourceState, 'available');
    assert.equal(text.textContent, 'Tilgjengelig · Godkjenning: Godkjent');
    assert.doesNotMatch(text.textContent, /Kan forespørres|Kan ikke forespørres/);
    assert.match(automaticImage.alt, /Kan forespørres nå/);
    now += 1001;
    callbacks[0]();
    assert.match(image.src, /drone-available\.svg/);
    assert.equal(image.dataset.resourceState, 'available');
    assert.equal(text.textContent, 'Tilgjengelig · Godkjenning: Godkjenning utløpt');
    assert.match(automaticImage.alt, /Godkjenning utløpt/);
    assert.equal(image.alt, '', 'adjacent visible status carries the decorative image meaning');
});

test('mixed list items render green for available and red for unavailable regardless of verification', () => {
    const images = [
        ['Drone', 'Tilgjengelig', 'Venter på verifisering'],
        ['Generator', 'Utilgjengelig', 'Godkjent'],
        ['Utstyr', 'Tilgjengelig', 'Avvist'],
        ['Kjøretøy', 'Utilgjengelig', 'Venter på verifisering']
    ].map(([type, availability, verification]) => ({
        dataset: {
            resourceIcon: type,
            resourceStatus: JSON.stringify({ availability, verification, isEligible: false })
        },
        hasAttribute: () => false
    }));
    loadResourceIcons({
        document: {
            currentScript: { dataset: { iconBase: '/images/resources/' } },
            querySelectorAll: () => images
        }
    });

    for (const [index, file] of ['drone', 'generator', 'toolbox', 'car'].entries()) {
        const state = index % 2 === 0 ? 'available' : 'unavailable';
        assert.equal(images[index].dataset.resourceState, state);
        assert.match(images[index].src, new RegExp(`/${file}-${state}\\.svg`));
    }
});

test('dynamic overlays hydrate through the shared root API and stop expiry refreshes after detachment', () => {
    let now = Date.parse('2026-10-26T12:00:00Z');
    class Clock extends Date { static now() { return now; } }
    const callbacks = [];
    const status = eligibleStatus({ verificationExpiresAtUtc: '2026-10-26T12:00:01Z' });
    const image = {
        isConnected: true,
        dataset: { resourceIcon: 'Drone', resourceStatus: JSON.stringify(status) },
        hasAttribute: () => false
    };
    const api = loadResourceIcons({
        Date: Clock,
        window: { setTimeout(callback) { callbacks.push(callback); } }
    });
    api.initializeImages({
        querySelectorAll(selector) {
            assert.equal(selector, 'img[data-resource-icon]');
            return [image];
        }
    });
    assert.match(image.src, /drone-available\.svg/);
    assert.match(image.alt, /Kan forespørres nå/);
    assert.equal(callbacks.length, 1);
    image.isConnected = false;
    const originalTitle = image.title;
    now += 1000;
    callbacks[0]();
    assert.equal(image.title, originalTitle, 'detached content is not refreshed');
    assert.equal(callbacks.length, 1, 'detached roots do not reschedule');
    const detachedImage = { ...image, src: undefined };
    api.initializeImages({ querySelectorAll: () => [detachedImage] });
    assert.equal(detachedImage.src, undefined);
    assert.equal(callbacks.length, 1, 'already detached content creates no timer');
});

test('availability callbacks can stop a long-delay timer chain when an overlay is closed', () => {
    let now = Date.parse('2026-10-26T12:00:00Z');
    class Clock extends Date { static now() { return now; } }
    const callbacks = [];
    const api = loadResourceIcons({
        Date: Clock,
        window: { setTimeout(callback) { callbacks.push(callback); } }
    });
    api.watchAvailability(() => false, [eligibleStatus()]);
    assert.equal(callbacks.length, 1);
    now += 2147483647;
    callbacks[0]();
    assert.equal(callbacks.length, 1);
});
