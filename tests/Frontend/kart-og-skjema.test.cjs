// Sjekker registreringsskjemaene og at kart.js tegner det serveren har bestemt.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');

const root = resolve(__dirname, '../..');
const read = file => readFileSync(resolve(root, file), 'utf8');

// ---------- Skjemaene ----------

test('ressursleverandør kan velge «Annen type …» og skrive inn en ny ressurstype', () => {
    const map = read('Views/Home/Map.cshtml');
    assert.match(map, /<option value="@RessursSkjemaViewModel\.AnnenType" selected="@annenType">Annen type …<\/option>/);
    assert.match(map, /class="form-group annen-type" id="nyRessurstypeGroup"/);
    assert.match(map, /name="NyRessurstype"/);
    assert.match(map, /@foreach \(var type in ressurstyper\)/, 'listen hentes fra databasen, ikke en fast liste');
    assert.match(read('Models/FormViewModels.cs'), /public const string AnnenType = "__annen__";/);
});

test('offentlig aktør kan velge «Annen type …» og skrive inn en ny behovstype', () => {
    const view = read('Views/Behov/Registrer.cshtml');
    assert.match(view, /<option value="@BehovSkjemaViewModel\.AnnenType" selected="@annenType">Annen type …<\/option>/);
    assert.match(view, /class="form-group annen-type" id="nyBehovstypeGroup"/);
    assert.match(view, /name="NyBehovstype"/);
    assert.match(read('Models/FormViewModels.cs'), /public const int AnnenType = -1;/);
});

test('feltet for egen type vises med CSS (:has), uten JavaScript', () => {
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /\.annen-type \{ display: none; \}/);
    assert.match(css, /form:has\(option\[value="__annen__"\]:checked\) \.annen-type/);
    assert.match(css, /form:has\(option\[value="-1"\]:checked\) \.annen-type \{ display: block; \}/);
    assert.match(css, /@supports not selector\(:has\(\*\)\)/, 'eldre nettlesere viser feltet alltid');
});

test('kartlista og filteret samler egne ressurstyper under «Annet» med ett felles symbol', () => {
    assert.match(read('Views/Home/Map.cshtml'), /@foreach \(var gruppe in RessursIkon\.Grupper\)/);
    assert.match(read('Models/RessursIkon.cs'), /public static IReadOnlyList<string> Grupper \{ get; \} = \[\.\. FasteTyper, Annet\];/);
    assert.match(read('Models/ResourceQueries.cs'), /if \(filter\.Type == RessursIkon\.Annet\)/);
    // Hver fast type og «Annet» har et symbol i tre varianter.
    for (const fil of ['car', 'excavator', 'drone', 'generator', 'personnel', 'toolbox', 'other', 'unknown'])
        for (const variant of ['', '-available', '-unavailable'])
            assert.match(read(`wwwroot/images/resources/${fil}${variant}.svg`), /^<svg xmlns="http:\/\/www\.w3\.org\/2000\/svg"/, fil + variant);
});

// ---------- Kartet ----------

test('kartsidene får kartdata som JSON fra serveren og laster kart.js', () => {
    for (const view of ['Views/Home/Map.cshtml', 'Views/Behov/Registrer.cshtml']) {
        const html = read(view);
        assert.match(html, /<script type="application\/json" id="kartdata">/, view);
        assert.match(html, /<script src="~\/js\/kart\.js" asp-append-version="true"><\/script>/, view);
        // Standardinnstillingen til JsonSerializer gjør om < og > slik at tekst fra brukere ikke kan bryte ut av <script>.
        assert.doesNotMatch(html, /UnsafeRelaxedJsonEscaping/, view);
    }
});

// Et objekt som godtar alle kall og alltid gir seg selv tilbake. Brukes for de delene av
// Leaflet og nettleseren som testene ikke bryr seg om (kontroller, kartlag, knapper osv.).
function hvaSomHelst() {
    const f = function () { return p; };
    const p = new Proxy(f, { get: (_, k) => (k === 'then' ? undefined : k === Symbol.toPrimitive ? () => '' : p), apply: () => p, construct: () => p });
    return p;
}

// Kjører kart.js i en liten falsk nettleser og returnerer det skriptet tegnet.
function kjørKart(data, felter = {}) {
    const markører = [];
    const hendelser = {};
    const kart = new Proxy({}, {
        get(t, k) {
            if (k === 'setView') return (senter, zoom) => { t.senter = senter; t.zoom = zoom; return kart; };
            if (k === 'addLayer') return m => { markører.push(m); return kart; };
            if (k === 'on') return (navn, fn) => { (hendelser[navn] ??= []).push(fn); return kart; };
            return k in t ? t[k] : hvaSomHelst();
        }
    });
    const L = new Proxy({}, {
        get(_, k) {
            if (k === 'map') return () => kart;
            if (k === 'markerClusterGroup') return undefined; // uten gruppering legges markørene rett på kartet
            if (k === 'divIcon') return valg => ({ valg });
            if (k === 'latLng') return (lat, lng) => ({ lat, lng });
            if (k === 'marker') return (punkt, valg) => {
                const m = { punkt, valg, addTo() { markører.push(m); return m; }, bindPopup(html) { m.popup = html; return m; },
                    on() { return m; }, openPopup() { m.åpen = true; return m; }, setLatLng(p) { m.punkt = p; return m; } };
                return m;
            };
            return hvaSomHelst();
        }
    });
    const elementer = { kartdata: { textContent: JSON.stringify(data) }, ...felter };
    const document = { getElementById: id => elementer[id] ?? null, querySelectorAll: () => [], querySelector: () => null,
        addEventListener() { }, dispatchEvent() { } };
    const window = { location: { search: '', href: 'http://localhost/' }, setTimeout() { }, innerWidth: 1200,
        matchMedia: () => ({ matches: false, addEventListener() { } }) };
    vm.runInNewContext(read('wwwroot/js/kart.js'), { L, document, window, navigator: {}, CustomEvent: function () { }, URL });
    const klikk = latlng => (hendelser.click ?? []).forEach(fn => fn({ latlng }));
    return { kart, markører, elementer, klikk };
}

test('kart.js tegner markørene og popup-teksten som serveren har laget', () => {
    const { kart, markører } = kjørKart({
        lat: 58.1, lng: 7.9, zoom: 15, plassering: null,
        markorer: [{ lat: 58.1, lng: 7.9, ikon: '/images/resources/car-available.svg', tittel: 'Bil', popup: '<strong>Bil</strong>', apen: true, kanForespørres: true }]
    });
    assert.deepEqual([...kart.senter], [58.1, 7.9]);
    assert.equal(kart.zoom, 15);
    assert.equal(markører.length, 1);
    assert.match(markører[0].valg.icon.valg.html, /car-available\.svg/);
    assert.equal(markører[0].popup, '<strong>Bil</strong>');
    assert.equal(markører[0].åpen, true, 'markøren serveren har bedt om, åpnes med en gang');
});

test('kart.js fyller ut breddegrad og lengdegrad når man klikker i kartet i et skjema', () => {
    const { markører, elementer, klikk } = kjørKart(
        { lat: 58, lng: 8, zoom: 12, markorer: [], plassering: { latFelt: 'latitude', lngFelt: 'longitude', lat: null, lng: null } },
        { latitude: { value: '' }, longitude: { value: '' }, needPlace: { textContent: '' } });
    klikk({ lat: 58.123456789, lng: 7.987654321 });
    assert.equal(elementer.latitude.value, '58.123457');
    assert.equal(elementer.longitude.value, '7.987654');
    assert.equal(elementer.needPlace.textContent, 'Valgt punkt: 58.12346, 7.98765');
    klikk({ lat: 59, lng: 10 });
    assert.equal(markører.length, 1, 'det samme punktet flyttes, det lages ikke et nytt');
    assert.equal(elementer.latitude.value, '59.000000');
});
