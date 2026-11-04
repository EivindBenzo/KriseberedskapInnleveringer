const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('registration only offers self-service roles and explains that the rest are assigned', () => {
    const view = read('Views/Account/Register.cshtml');
    assert.match(view, /@foreach \(var role in AppRoles\.SelfService\)[\s\S]*AppRoles\.Label\(role\)/);
    assert.match(view, /tildeles av kriseledelsen/);
    for (const field of ['Fornavn', 'Etternavn', 'Telefon', 'Organisasjon']) assert.match(view, new RegExp(`asp-for="${field}"`));
    const roles = read('Models/AppRoles.cs');
    assert.match(roles, /SelfService = \[Ressursleverandor, OffentligAktor\];/);
    for (const label of ['Offentlig aktør', 'Ressursleverandør', 'Dispatch-team', 'Kriseledelse', 'Verifiseringsansvarlig'])
        assert.ok(roles.includes(`"${label}"`), label);
});

test('navigation shows Oversikt to every role and role-specific entries only to their roles', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /asp-action="Dashboard" class="nav-link" aria-current="@Aktiv\("Home", "Dashboard"\)">Oversikt/);
    const operations = layout.slice(layout.indexOf('@if (canUseOperations)'), layout.indexOf('@if (User.IsInRole(HeimevernetInnlevering1.Models.AppRoles.OffentligAktor))'));
    assert.match(layout, /AppRoles\.OffentligAktor\)\)\s*\{\s*<li><a asp-controller="Behov" asp-action="Registrer"/);
    assert.match(layout, /AppRoles\.Kriseledelse\)\)\s*\{\s*<li><a asp-controller="Hendelse"/);
    for (const role of ['Ressursleverandor', 'DispatchTeam', 'Kriseledelse'])
        assert.ok(operations.includes(`AppRoles.${role}`), role);
    assert.doesNotMatch(operations, /AppRoles\.(OffentligAktor|Verifiseringsansvarlig)/);
});

test('dashboard sections are gated per role and post to the server-side workflow actions', () => {
    const view = read('Views/Home/Dashboard.cshtml');
    const gates = {
        provider: ['Respond', 'Mine ressurser'],
        verifier: ['Verify', 'Venter på verifisering'],
        dispatch: ['CreateRequest', 'Allocate', 'Cancel'],
        leader: ['Behov etter prioritet', 'asp-controller="Admin"'],
        publicActor: ['asp-controller="Behov" asp-action="Registrer"', 'Mine behov']
    };
    for (const [gate, needles] of Object.entries(gates)) {
        const start = view.indexOf(`@if (${gate})`);
        assert.ok(start >= 0, gate);
        const body = view.slice(start, view.indexOf('\n@if (', start + 1) === -1 ? undefined : view.indexOf('\n@if (', start + 1));
        for (const needle of needles) assert.ok(body.includes(needle), `${gate}: ${needle}`);
    }
    // Etter en handling sendes brukeren tilbake til samme element (#anker), ikke til toppen av siden.
    assert.match(view, /string Tilbake\(string anker\) => Url\.Action\("Dashboard", "Home"\) \+ "#" \+ anker;/);
    assert.match(view, /<article id="tildeling-@assignment\.TildelingID"/);
    assert.match(view, /name="returnUrl" value="@Tilbake\("tildeling-" \+ assignment\.TildelingID\)"/);
    assert.doesNotMatch(view, /value="@back"/);
    assert.match(view, /Begrunnelse \(kreves ved avvisning\)/);
    assert.match(view, /@if \(dispatch \|\| leader\)[\s\S]*asp-action="Assignment"/);
});

test('timeline partial and styles reuse the existing design tokens', () => {
    assert.match(read('Views/Shared/_Timeline.cshtml'), /<summary>Historikk<\/summary>/);
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /\.timeline ol/);
    assert.match(css, /\.progress-steps li\.done \{[^}]*var\(--accent\)/);
});
