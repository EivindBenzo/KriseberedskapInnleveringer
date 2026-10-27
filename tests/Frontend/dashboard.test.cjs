const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('registration offers every account type from AppRoles and no longer mentions assigned roles', () => {
    const view = read('Views/Account/Register.cshtml');
    assert.match(view, /@foreach \(var role in AppRoles\.SelfService\)[\s\S]*AppRoles\.Label\(role\)/);
    assert.doesNotMatch(view, /tildeles av kriseledelsen/);
    const roles = read('Models/AppRoles.cs');
    assert.match(roles, /SelfService = All;/);
    for (const label of ['Offentlig aktør', 'Ressursleverandør', 'Dispatch-team', 'Kriseledelse', 'Verifiseringsansvarlig'])
        assert.ok(roles.includes(`"${label}"`), label);
});

test('navigation shows Oversikt to every role and role-specific entries only to their roles', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /asp-action="Dashboard" class="nav-link">Oversikt/);
    const operations = layout.slice(layout.indexOf('@if (canUseOperations)'), layout.indexOf('asp-controller="Admin"'));
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
        publicActor: ['CreateNeed', 'Mine behov']
    };
    for (const [gate, needles] of Object.entries(gates)) {
        const start = view.indexOf(`@if (${gate})`);
        assert.ok(start >= 0, gate);
        const body = view.slice(start, view.indexOf('\n@if (', start + 1) === -1 ? undefined : view.indexOf('\n@if (', start + 1));
        for (const needle of needles) assert.ok(body.includes(needle), `${gate}: ${needle}`);
    }
    assert.match(view, /name="returnUrl" value="@back"/);
    assert.match(view, /Begrunnelse \(kreves ved avvisning\)/);
    assert.match(view, /@if \(dispatch \|\| leader\)[\s\S]*asp-action="Assignment"/);
});

test('timeline partial and styles reuse the existing design tokens', () => {
    assert.match(read('Views/Shared/_Timeline.cshtml'), /<summary>Historikk<\/summary>/);
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /\.timeline ol/);
    assert.match(css, /\.progress-steps li\.done \{[^}]*var\(--accent\)/);
});

test('legend header shows a grab cursor and the panel cannot be resized', () => {
    const css = read('wwwroot/css/map-controls.css');
    assert.match(css, /\.map-legend summary \{[^}]*cursor: grab;[^}]*touch-action: none/);
    assert.match(css, /\.map-legend summary:active \{\s*cursor: grabbing/);
    assert.doesNotMatch(css, /resize:\s*(?:both|vertical|horizontal)/);
});
