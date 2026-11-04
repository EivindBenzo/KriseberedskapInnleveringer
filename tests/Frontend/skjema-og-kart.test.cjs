const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('ressursleverandør kan velge «Annen type …» og skrive inn en ny ressurstype', () => {
    const map = read('Views/Home/Map.cshtml');
    assert.match(map, /<option value="@RessursSkjemaViewModel\.AnnenType" selected="@annenType">Annen type …<\/option>/);
    assert.match(map, /class="form-group annen-type" id="nyRessurstypeGroup"/);
    assert.match(map, /name="NyRessurstype"/);
    assert.match(map, /@foreach \(var type in ressurstyper\)/, 'listen hentes fra databasen, ikke en fast liste');
    assert.match(read('Models/FormViewModels.cs'), /public const string AnnenType = "__annen__";/);
});

test('feltet for egen type vises med CSS (:has), uten JavaScript', () => {
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /\.annen-type \{ display: none; \}/);
    assert.match(css, /form:has\(option\[value="__annen__"\]:checked\) \.annen-type/);
    assert.match(css, /form:has\(option\[value="-1"\]:checked\) \.annen-type \{ display: block; \}/);
    assert.match(css, /@supports not selector\(:has\(\*\)\)/, 'eldre nettlesere viser feltet alltid');
});

test('kontaktfeltene starter tomme og viser bare eksempeltekst', () => {
    for (const view of ['Views/Home/Map.cshtml', 'Views/Behov/Registrer.cshtml']) {
        const html = read(view);
        assert.match(html, /placeholder="f\.eks\. Ola Nordmann"/, view);
        assert.match(html, /placeholder="f\.eks\. 999 99 999"/, view);
    }
    assert.doesNotMatch(read('Controllers/HomeController.cs'), /Kontaktperson = bruker\.FulltNavn/);
    assert.doesNotMatch(read('Controllers/BehovController.cs'), /Kontaktperson = bruker\.FulltNavn/);
});

test('passordet krever ikke spesialtegn', () => {
    assert.match(read('Program.cs'), /options\.Password\.RequireNonAlphanumeric = false;/);
    assert.match(read('Views/Account/Register.cshtml'), /Spesialtegn er ikke nødvendig/);
});

test('etter en statusendring kommer man tilbake til samme element på siden (#anker)', () => {
    // Serveren sender brukeren til f.eks. /Home/Dashboard#tildeling-12, og nettleseren går rett dit.
    const dashboard = read('Views/Home/Dashboard.cshtml');
    assert.match(dashboard, /name="returnUrl" value="@Tilbake\("min-ressurs-" \+ resource\.RessursID\)"/);
    assert.match(dashboard, /<article id="min-ressurs-@resource\.RessursID"/);
    const operations = read('Views/Operations/Index.cshtml');
    assert.equal((operations.match(/<form /g) || []).length, (operations.match(/name="returnUrl"/g) || []).length,
        'alle skjemaene på Forespørsler-siden har returnUrl');
    assert.match(read('Controllers/OperationsController.cs'), /Url\.IsLocalUrl\(returnUrl\) \? LocalRedirect\(returnUrl\)/);
});

test('offentlig aktør kan velge «Annen type …» og skrive inn en ny behovstype', () => {
    const view = read('Views/Behov/Registrer.cshtml');
    assert.match(view, /<option value="@BehovSkjemaViewModel\.AnnenType" selected="@annenType">Annen type …<\/option>/);
    assert.match(view, /class="form-group annen-type" id="nyBehovstypeGroup"/);
    assert.match(view, /name="NyBehovstype"/);
    assert.match(read('Models/FormViewModels.cs'), /public const int AnnenType = -1;/);
});

test('kartlista og filteret samler egne ressurstyper under «Annet» med ett felles symbol', () => {
    const map = read('Views/Home/Map.cshtml');
    assert.match(map, /@foreach \(var gruppe in RessursIkon\.Grupper\)/);
    assert.match(read('Models/RessursIkon.cs'), /public static IReadOnlyList<string> Grupper \{ get; \} = \[\.\. FasteTyper, Annet\];/);
    assert.match(read('Models/ResourceQueries.cs'), /if \(filter\.Type == RessursIkon\.Annet\)/);
    for (const file of ['other', 'other-available', 'other-unavailable'])
        assert.match(read(`wwwroot/images/resources/${file}.svg`), /^<svg xmlns="http:\/\/www\.w3\.org\/2000\/svg" viewBox="0 0 48 48"/);
});

test('bare kartet bruker JavaScript, og alle de gamle skriptene er borte', () => {
    const { readdirSync } = require('node:fs');
    assert.deepEqual(readdirSync(resolve(__dirname, '../../wwwroot/js')), ['kart.js']);
    for (const view of ['Views/Shared/_Layout.cshtml', 'Views/Home/Verify.cshtml', 'Views/Home/Dashboard.cshtml', 'Views/Operations/Index.cshtml'])
        assert.doesNotMatch(read(view), /<script/, view);
    for (const view of ['Views/Home/Map.cshtml', 'Views/Behov/Registrer.cshtml']) {
        const html = read(view);
        assert.match(html, /<script type="application\/json" id="kartdata">/, view);
        assert.match(html, /<script src="~\/js\/kart\.js" asp-append-version="true"><\/script>/, view);
        assert.equal((html.match(/<script>/g) || []).length, 0, view + ' har ikke eget innebygd skript');
    }
});

test('tema velges på serveren med en cookie', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /<html lang="no" data-theme="@tema">/);
    assert.match(layout, /<form asp-controller="Tema" asp-action="Bytt" method="post">/);
    const controller = read('Controllers/TemaController.cs');
    assert.match(controller, /\[HttpPost, ValidateAntiForgeryToken\]/);
    assert.match(controller, /Url\.IsLocalUrl\(returnUrl\) \? LocalRedirect\(returnUrl\)/);
});

// kart.js kjøres i en liten falsk nettleser for å sjekke at det bare tegner det serveren har bestemt.
function kjørKart(data) {
    const elementer = {
        kartdata: { textContent: JSON.stringify(data) },
        latitude: { value: '' }, longitude: { value: '' }, selectedPlace: { textContent: '' }
    };
    const markører = [];
    const hendelser = {};
    const kart = {
        setView(senter, zoom) { this.senter = senter; this.zoom = zoom; return this; },
        on(navn, fn) { hendelser[navn] = fn; },
        getContainer: () => ({ classList: { add(c) { kart.klasse = c; } } })
    };
    const L = {
        map: () => kart,
        tileLayer: () => ({ addTo() { return this; } }),
        icon: valg => ({ valg }),
        marker(punkt, valg) {
            const m = { punkt, valg, addTo() { markører.push(m); return m; }, bindPopup(html) { m.popup = html; return m; },
                openPopup() { m.åpen = true; }, setLatLng(p) { m.punkt = p; } };
            return m;
        }
    };
    vm.runInNewContext(read('wwwroot/js/kart.js'), { L, document: { getElementById: id => elementer[id] } });
    return { kart, markører, hendelser, elementer };
}

test('kart.js tegner markørene og popup-teksten som serveren har laget', () => {
    const { kart, markører, hendelser } = kjørKart({
        lat: 58.1, lng: 7.9, zoom: 15,
        markorer: [{ lat: 58.1, lng: 7.9, ikon: '/images/resources/car-available.svg', tittel: 'Bil', popup: '<strong>Bil</strong>', apen: true }],
        plassering: null
    });
    assert.equal(JSON.stringify(kart.senter), '[58.1,7.9]');
    assert.equal(kart.zoom, 15);
    assert.equal(markører.length, 1);
    assert.equal(markører[0].valg.icon.valg.iconUrl, '/images/resources/car-available.svg');
    assert.equal(markører[0].popup, '<strong>Bil</strong>');
    assert.equal(markører[0].åpen, true);
    assert.equal(hendelser.click, undefined, 'uten skjema gjør et klikk i kartet ingenting');
});

test('kart.js fyller ut breddegrad og lengdegrad når man klikker i kartet i et skjema', () => {
    const { markører, hendelser, elementer, kart } = kjørKart({
        lat: 58, lng: 8, zoom: 12, markorer: [],
        plassering: { latFelt: 'latitude', lngFelt: 'longitude', tekstFelt: 'selectedPlace', lat: null, lng: null }
    });
    assert.equal(kart.klasse, 'placement-active');
    hendelser.click({ latlng: { lat: 58.123456789, lng: 7.987654321 } });
    assert.equal(elementer.latitude.value, '58.123457');
    assert.equal(elementer.longitude.value, '7.987654');
    assert.match(elementer.selectedPlace.textContent, /^58\.12346, 7\.98765/);
    hendelser.click({ latlng: { lat: 59, lng: 10 } });
    assert.equal(markører.length, 1, 'det samme punktet flyttes, det lages ikke et nytt');
});
