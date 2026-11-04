const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('ressursleverandør kan velge «Annen type …» og skrive inn en ny ressurstype', () => {
    const map = read('Views/Home/Map.cshtml');
    assert.match(map, /<option value="@RessursSkjemaViewModel\.AnnenType"[^>]*>Annen type …<\/option>/);
    assert.match(map, /id="nyRessurstypeGroup" hidden="@\(!annenType\)"/);
    assert.match(map, /name="NyRessurstype"/);
    assert.match(map, /@foreach \(var type in ressurstyper\)/, 'listen hentes fra databasen, ikke en fast liste');
    assert.match(read('Models/FormViewModels.cs'), /public const string AnnenType = "__annen__";/);
    assert.match(map, /resourceType\.value === '__annen__'/);
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

test('scroll-posisjonen huskes etter innsending og gjenopprettes på samme side', () => {
    const source = read('wwwroot/js/scroll-position.js');
    assert.match(read('Views/Shared/_Layout.cshtml'), /<script src="~\/js\/scroll-position\.js" asp-append-version="true"><\/script>/);
    const storage = new Map();
    const listeners = {};
    let scrolledTo = null;
    class HTMLFormElement { constructor(method) { this.method = method; } }
    const window = { location: { pathname: '/Home/Dashboard', hash: '' }, scrollY: 1234,
        scrollTo: options => { scrolledTo = options.top; } };
    const context = {
        window, HTMLFormElement, Date, JSON,
        sessionStorage: { getItem: k => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) },
        document: { readyState: 'complete', addEventListener: (type, fn) => { listeners[type] = fn; } }
    };
    vm.runInNewContext(source, context);
    listeners.submit({ target: new HTMLFormElement('post') });
    assert.ok(storage.size === 1, 'posisjonen lagres ved POST');

    // Simulerer at siden lastes på nytt etter redirect tilbake til samme adresse.
    window.scrollY = 0;
    vm.runInNewContext(source, context);
    assert.equal(scrolledTo, 1234);
    assert.equal(storage.size, 0, 'posisjonen brukes bare én gang');

    listeners.submit({ target: new HTMLFormElement('get') });
    assert.equal(storage.size, 0, 'GET-skjemaer (søk/filter) lagrer ikke posisjon');
});

test('offentlig aktør kan velge «Annen type …» og skrive inn en ny behovstype', () => {
    const view = read('Views/Behov/Registrer.cshtml');
    assert.match(view, /<option value="@BehovSkjemaViewModel\.AnnenType" selected="@annenType">Annen type …<\/option>/);
    assert.match(view, /id="nyBehovstypeGroup" hidden="@\(!annenType\)"/);
    assert.match(view, /name="NyBehovstype"/);
    assert.match(view, /typeSelect\.value === '-1'/);
    assert.match(read('Models/FormViewModels.cs'), /public const int AnnenType = -1;/);
});

test('kartlista og filteret samler egne ressurstyper under «Annet» med ett felles symbol', () => {
    const map = read('Views/Home/Map.cshtml');
    assert.match(map, /<option value="Annet">Annet<\/option>/);
    assert.match(map, /ResourceIcons\.group\(item\.resource\.type\) === type/);
    assert.match(map, /ResourceIcons\.group\(card\.dataset\.resourceType\)/);
    for (const file of ['other', 'other-available', 'other-unavailable'])
        assert.match(read(`wwwroot/images/resources/${file}.svg`), /^<svg xmlns="http:\/\/www\.w3\.org\/2000\/svg" viewBox="0 0 48 48"/);
});
