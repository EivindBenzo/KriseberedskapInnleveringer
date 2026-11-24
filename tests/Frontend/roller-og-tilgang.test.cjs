// Sjekker at hver rolle bare ser og kan bruke det den skal, og at innlogging og videresending er trygge.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync, readdirSync } = require('node:fs');
const { resolve, join } = require('node:path');

const root = resolve(__dirname, '../..');
const read = file => readFileSync(resolve(root, file), 'utf8');

// Alle filer med en bestemt endelse i en mappe (og undermapper).
function filer(mappe, endelse) {
    return readdirSync(resolve(root, mappe), { withFileTypes: true }).flatMap(f =>
        f.isDirectory() ? filer(join(mappe, f.name), endelse) : f.name.endsWith(endelse) ? [join(mappe, f.name)] : []);
}

// Finner betingelsen til seksjonen et sted ligger i: nærmeste «@if (...)» helt til venstre i Razor-siden.
function betingelseFør(tekst, indeks) {
    const start = tekst.lastIndexOf('\n@if (', indeks) + 1;
    assert.ok(start > 0, 'fant ingen @if over');
    return tekst.slice(start, tekst.indexOf('\n', start));
}

test('systemet har fire roller, og verifiseringsansvarlig er borte fra sidene og koden', () => {
    const roller = read('Models/AppRoles.cs');
    for (const navn of ['Offentlig aktør', 'Ressursleverandør', 'Dispatch-team', 'Kriseledelse'])
        assert.ok(roller.includes(`"${navn}"`), navn);
    assert.match(roller, /All =\s*\[OffentligAktor, Ressursleverandor, DispatchTeam, Kriseledelse\]/);

    // IdentitySeeder (i Data) har bevisst med det gamle navnet for å flytte gamle brukere over til dispatch.
    for (const fil of [...filer('Views', '.cshtml'), ...filer('Controllers', '.cs'), ...filer('Models', '.cs'), ...filer('Services', '.cs')])
        assert.doesNotMatch(read(fil), /Verifiseringsansvarlig/i, fil);
});

test('ved registrering kan man bare velge de rollene man får gi seg selv', () => {
    const view = read('Views/Account/Register.cshtml');
    assert.match(view, /@foreach \(var role in AppRoles\.SelfService\)/);
    assert.match(view, /tildeles av kriseledelsen/);
    for (const felt of ['Fornavn', 'Etternavn', 'Telefon'])
        assert.match(view, new RegExp(`asp-for="${felt}"[^>]*required`), felt);
    assert.match(read('Models/AppRoles.cs'), /SelfService = \[Ressursleverandor, OffentligAktor\]/);
    // Serveren sjekker valget også, så det hjelper ikke å endre skjemaet i nettleseren.
    assert.match(read('Controllers/AccountController.cs'), /!AppRoles\.SelfService\.Contains\(model\.Role\)/);
});

test('menypunktene vises bare for rollene som har tilgang til siden', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    const krav = [
        { lenke: 'asp-controller="Behov" asp-action="Registrer"', roller: ['OffentligAktor'], kontroller: 'BehovController' },
        { lenke: 'asp-controller="Hendelse" asp-action="Index"', roller: ['Kriseledelse', 'DispatchTeam'], kontroller: 'HendelseController' },
        { lenke: 'asp-controller="Admin" asp-action="Index"', roller: ['Kriseledelse'], kontroller: 'AdminController' }
    ];
    for (const { lenke, roller, kontroller } of krav) {
        const indeks = layout.indexOf(lenke);
        assert.ok(indeks >= 0, lenke);
        const betingelse = layout.slice(layout.lastIndexOf('@if (', indeks), indeks);
        const tilgang = read(`Controllers/${kontroller}.cs`).match(/^\[Authorize\(Roles = ([^\]]+)\)\]/m)[1];
        for (const rolle of roller) {
            assert.ok(betingelse.includes(`AppRoles.${rolle}`), `${lenke} vises for ${rolle}`);
            assert.ok(tilgang.includes(`AppRoles.${rolle}`), `${kontroller} slipper inn ${rolle}`);
        }
    }
});

test('oversiktssiden viser handlingene bare til rollen som får utføre dem', () => {
    const view = read('Views/Home/Dashboard.cshtml');
    const krav = {
        'asp-action="Respond"': 'provider',     // ressursleverandør svarer på forespørsler
        'asp-action="Verify"': 'dispatch',      // dispatch verifiserer ressurser
        'asp-action="Allocate"': 'dispatch',    // dispatch tildeler
        'asp-action="Cancel"': 'dispatch',      // dispatch avbryter forespørsler
        'asp-controller="Admin"': 'leader'      // kriseledelsen styrer brukere
    };
    for (const [handling, rolle] of Object.entries(krav)) {
        const indeks = view.indexOf(handling);
        assert.ok(indeks >= 0, handling);
        assert.ok(betingelseFør(view, indeks).includes(rolle), `${handling} ligger under @if (${rolle} ...)`);
    }
    assert.match(read('Views/Shared/_AssignDialog.cshtml'), /asp-action="CreateRequest"/);
    // Serveren sjekker rollen en gang til, så skjulte knapper er ikke den eneste beskyttelsen.
    const operations = read('Controllers/OperationsController.cs');
    assert.match(operations, /\[HttpPost, Authorize\(Roles = AppRoles\.DispatchTeam\)\]/);
    assert.match(operations, /\[HttpPost, Authorize\(Roles = AppRoles\.Ressursleverandor\)\]/);
});

test('videresending etter innlogging og skjemaer går bare til sider i appen', () => {
    // Url.IsLocalUrl hindrer at en lenke kan sende brukeren videre til en falsk side utenfor appen.
    for (const fil of filer('Controllers', '.cs')) {
        const kode = read(fil);
        assert.doesNotMatch(kode, /[^l]Redirect\(/, `${fil} bruker vanlig Redirect`);
        if (/returnUrl/i.test(kode) && /LocalRedirect/.test(kode))
            assert.match(kode, /Url\.IsLocalUrl\(/, fil);
    }
});

test('etter en handling kommer man tilbake til samme sted på siden', () => {
    const dashboard = read('Views/Home/Dashboard.cshtml');
    assert.match(dashboard, /string Tilbake\(string anker\) => Url\.Action\("Dashboard", "Home"\) \+ "#" \+ anker;/);
    assert.match(dashboard, /name="returnUrl" value="@Tilbake\("min-ressurs-" \+ resource\.RessursID\)"/);
    assert.match(dashboard, /id="min-ressurs-@resource\.RessursID"/);
});

test('passordet krever ikke spesialtegn, og det står forklart på siden', () => {
    assert.match(read('Program.cs'), /options\.Password\.RequireNonAlphanumeric = false;/);
    assert.match(read('Views/Account/Register.cshtml'), /Spesialtegn er ikke nødvendig/);
});
