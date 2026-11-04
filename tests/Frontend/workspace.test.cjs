const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('navigation exposes operational destinations, prototype identity and keyboard escape', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /Studentprosjekt · prototype/);
    assert.match(layout, /asp-action="Resources"/);
    assert.doesNotMatch(layout, /asp-action="Mine"/);
    assert.doesNotMatch(layout, /asp-fragment="(?:resources|needs)"/);
    for (const view of [layout, read('Views/Home/Index.cshtml')]) {
        assert.match(view, /asp-controller="Operations" asp-action="Index"/);
        assert.doesNotMatch(view, /asp-controller="Operations" asp-action="(?:Requests|Queue|Needs)"/);
        assert.match(view, /AppRoles.HasAny\(User\)/);
        assert.match(view, /ClaimTypes.NameIdentifier/);
        assert.match(view, /@if \(canUseOperations\)/);
    }
    assert.match(layout, /aria-current="@Aktiv\(/);
    // Menyen åpnes med en avkrysningsboks og CSS. Knappen er en <label> for boksen.
    assert.match(layout, /<input type="checkbox" id="menuToggle" class="menu-checkbox" aria-controls="navMenu" \/>/);
    assert.match(layout, /<label for="menuToggle" class="menu-toggle"/);
    assert.match(read('wwwroot/css/site.css'), /\.menu-checkbox:checked ~ \.nav-menu \{/);
    assert.match(layout, /Hopp til innhold/);
    assert.match(layout, /asp-controller="Account" asp-action="Logout" method="post"/);
});

test('resources keep mine actions inside their context', () => {
    const resources = read('Views/Home/Verify.cshtml');
    assert.match(resources, /aria-label="Ressursutvalg"/);
    assert.match(resources, /asp-action="Resources" asp-route-mine="true"/);
    for (const action of ['Edit', 'Delete'])
        assert.match(resources, new RegExp(`asp-action="${action}" asp-route-id="@r.RessursID"`));
    assert.match(resources, /asp-action="Edit" asp-route-id="@r.RessursID" class="btn-secondary">Rediger<\/a>/);
    assert.doesNotMatch(resources, /Rediger \//);
    assert.doesNotMatch(resources, /ResetMap|reset-map|IsOnMap/, 'kartnullstilling er fjernet');
});

test('requests lead with actions and status while secondary work is expandable', () => {
    const operations = read('Views/Operations/Index.cshtml');
    assert.ok(operations.indexOf('<h2 id="requests"') < operations.indexOf('id="resources"'));
    for (const section of ['resources', 'needs'])
        assert.match(operations, new RegExp(`<details class="workflow-section" id="${section}"`));
    assert.match(operations, /<details class="workflow-section">\s*<summary>Hendelseslogg/);
    assert.match(operations, /value="true" type="submit" class="btn-submit">Godta</);
    assert.match(operations, /Statuser.Prioritet.Akutt \? "status-critical"/);
    assert.match(operations, /<summary>Avbryt forespørsel/);
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /data-theme="dark"\] \.status-positive/);
    assert.match(css, /\.data-container \.resource-row \{ grid-template-columns: 1fr;/);
    assert.match(css, /summary:focus-visible/);
});

test('mine and edit routes keep resources active and section links open the right section on the server', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /aria-current="@Aktiv\("Home", "Resources", "Mine", "Edit", "Delete"\)"/);
    const operations = read('Views/Operations/Index.cshtml');
    assert.match(operations, /asp-route-vis="resources" asp-fragment="resources"/);
    assert.match(operations, /asp-route-vis="needs" asp-fragment="needs"/);
    assert.match(operations, /id="needs" hidden="@\(!showNeeds\)" open="@\(vis == "needs"\)"/);
    assert.match(read('Controllers/OperationsController.cs'), /ViewBag\.Vis = vis is "resources" or "needs" \? vis : null;/);
});

test('workspace styles keep desktop navigation visible and hamburger mobile-only', () => {
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /\.menu-toggle \{ display: none; \}/);
    assert.match(css, /@media \(max-width: 767px\)[\s\S]*\.menu-toggle \{ display: flex;/);
    assert.match(css, /header::after \{ display: none; \}/);
    assert.match(css, /\.nav-brand img \{ height: 48px; width: auto; object-fit: contain;/);
    assert.match(css, /text-transform: none/);
    assert.match(css, /\.btn-danger \{ color: var\(--error\)/);
});

test('main navigation has a persistent non-color selected cue, distinct hover and high-contrast focus', () => {
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /\.nav-menu \.nav-link\s*\{[^}]*min-height: 44px;[^}]*padding: \.65rem 1rem;[^}]*border-radius: \.75rem;/);
    assert.match(css, /\.nav-menu \.nav-link:hover\s*\{[^}]*background: #203f63;/);
    assert.match(css, /\.nav-menu \.nav-link\[aria-current="page"\]\s*\{[^}]*background: #2b527e;[^}]*font-weight: 700;[^}]*text-decoration: underline;/);
    assert.match(css, /\.nav-menu \.nav-link:focus-visible\s*\{[^}]*outline: 3px solid #c6e2ff;[^}]*outline-offset: 3px;/);
});

test('home is permission-aware and resource overview keeps operational filtering without assessment prose', () => {
    const home = read('Views/Home/Index.cshtml');
    assert.match(home, /User.Identity\?\.IsAuthenticated/);
    assert.match(home, /Finn det som finnes/);
    assert.match(home, /AppRoles.Ressursleverandor/);
    assert.doesNotMatch(home, /hero-title-big|hero-photo/);
    const resources = read('Views/Home/Verify.cshtml');
    assert.match(resources, /ResourceOperationalStatus.From/);
    assert.doesNotMatch(resources, /Operativ vurdering|@status.StatusText/);
    assert.match(resources, /<label for="eligible">Operativ status<\/label>/);
    assert.match(resources, /<option value="true" selected="@\(ViewBag.Eligible is true\)">Kan forespørres nå/);
    assert.match(resources, /<dt>Registrert<\/dt>/);
    assert.doesNotMatch(resources, /Owner\?\.UserName/);
    assert.match(read('Views/Home/Delete.cshtml'), /asp-action="Delete" asp-route-id="@Model.RessursID" method="post"/);
});

test('resource cards have bounded theme-aware surfaces, readable facts and separated destructive actions', () => {
    const view = read('Views/Home/Verify.cshtml');
    const css = read('wwwroot/css/workspace.css');
    const tokens = read('wwwroot/css/site.css');
    assert.match(view, /class="data-container resource-overview"/);
    assert.match(view, /class="resource-facts"[\s\S]*?<dl>[\s\S]*?<dt>Registrert<\/dt>/);
    assert.match(view, /class="resource-status"[\s\S]*?@status.Lifecycle[\s\S]*?Godkjenning: @status.Verification/);
    assert.doesNotMatch(view, /Operativ vurdering|data-resource-status-text/);
    assert.match(view, /ResourcePermissions.CanManage\(User, r.EierID\)[\s\S]*?class="resource-destructive-actions"[\s\S]*?asp-action="Delete"/);
    assert.match(css, /\.resource-overview \{ max-width: 1240px/);
    assert.match(css, /\.resource-overview \{[^}]*background: transparent; border: 0;/);
    assert.match(css, /\.data-container \.resource-row \{[^}]*border: 1px solid var\(--border-strong\)[^}]*background: var\(--surface\)/);
    assert.match(css, /\.resource-destructive-actions \{[^}]*border-top:/);
    assert.equal((tokens.match(/--border-strong:/g) || []).length, 2, 'each theme defines a readable card outline');
});

test('registration layout widens without sacrificing responsive map and form controls', () => {
    const css = read('wwwroot/css/workspace-map.css');
    assert.match(css, /\[data-registration-open="true"\] \.workspace-grid \{\s*grid-template-columns: minmax\(340px, 400px\) minmax\(0, 1fr\)/);
    assert.match(css, /\.registration-section \{[^}]*min-width: 0/);
    assert.match(css, /#registrationToggle\[aria-expanded="true"\]/);
    assert.match(css, /@media \(max-width: 760px\)[\s\S]*\.workspace-grid \{\s*display: flex;\s*flex-direction: column/);
    assert.match(css, /\.resource-card \{[^}]*border: 1px solid var\(--workspace-border\)/);
});
