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
        assert.match(view, /User.IsInRole\("Bruker"\) \|\| User.IsInRole\("Admin"\)/);
        assert.match(view, /ClaimTypes.NameIdentifier/);
        assert.match(view, /@if \(canUseOperations\)/);
    }
    assert.match(layout, /aria-current/);
    assert.match(layout, /!link.hash \|\| link.hash === window.location.hash/);
    assert.match(layout, /addEventListener\('hashchange', updateCurrentNavigation\)/);
    assert.match(layout, /event.key === 'Escape'/);
    assert.match(layout, /Hopp til innhold/);
    assert.match(layout, /asp-controller="Account" asp-action="Logout" method="post"/);
});

test('resources keep mine actions inside their context and reset outside filtered rows', () => {
    const resources = read('Views/Home/Verify.cshtml');
    assert.match(resources, /aria-label="Ressursutvalg"/);
    assert.match(resources, /asp-action="Resources" asp-route-mine="true"/);
    for (const action of ['Edit', 'Delete'])
        assert.match(resources, new RegExp(`asp-action="${action}" asp-route-id="@r.Id"`));
    assert.match(resources, /asp-action="Edit" asp-route-id="@r.Id" class="btn-secondary">Rediger<\/a>/);
    assert.doesNotMatch(resources, /Rediger \//);
    assert.match(resources, /@if \(r.IsOnMap\)/);
    assert.match(resources, /Ikke på kartet/);
    const reset = resources.slice(resources.indexOf('<details class="danger-zone"'));
    assert.match(reset, /asp-action="ResetMap" method="post"/);
    assert.match(reset, /type="checkbox" name="confirmed" value="true" required/);
    assert.equal((resources.match(/id="reset-map"/g) || []).length, 1);
    assert.ok(resources.indexOf('id="reset-map"') > resources.indexOf('\n        }\n    </div>'));
    assert.match(resources, /også utenfor valgt filter/);
    assert.match(resources, /User.IsInRole\("Admin"\)[\s\S]*class="danger-zone"/);
});

test('requests lead with actions and status while secondary work is expandable', () => {
    const operations = read('Views/Operations/Index.cshtml');
    assert.ok(operations.indexOf('<h2 id="requests"') < operations.indexOf('id="resources"'));
    for (const section of ['resources', 'needs'])
        assert.match(operations, new RegExp(`<details class="workflow-section" id="${section}">`));
    assert.match(operations, /<details class="workflow-section">\s*<summary>Beslutningslogg/);
    assert.match(operations, /value="true" type="submit" class="btn-submit">Aksepter/);
    assert.match(operations, /NeedPriority.Critical \? "status-critical"/);
    assert.match(operations, /<summary>Avbryt forespørsel/);
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /data-theme="dark"\] \.status-positive/);
    assert.match(css, /\.data-container \.resource-row \{ grid-template-columns: 1fr;/);
    assert.match(css, /summary:focus-visible/);
});

test('mine and edit routes keep resources active and section links reveal collapsed content', () => {
    const vm = require('node:vm');
    const script = read('Views/Shared/_Layout.cshtml').match(/<script>([\s\S]*?)<\/script>/)[1];
    const listeners = {};
    const links = ['/', '/Home/Resources', '/Operations'].map(pathname => ({
        pathname, hash: '', attributes: {},
        setAttribute(key, value) { this.attributes[key] = value; },
        removeAttribute(key) { delete this.attributes[key]; }
    }));
    const section = { tagName: 'DETAILS', open: false, scrollIntoView() { this.scrolled = true; } };
    const control = { addEventListener() {}, contains() {} };
    const document = {
        querySelectorAll: () => links,
        getElementById: id => id === 'resources' ? section : control,
        addEventListener() {}
    };
    const window = {
        location: { pathname: '/Home/Mine', hash: '' },
        addEventListener(event, handler) { (listeners[event] ||= []).push(handler); }
    };
    vm.runInNewContext(script, { document, window });
    assert.equal(links[1].attributes['aria-current'], 'page');
    for (const pathname of ['/Home/Resources', '/Home/Edit/42', '/Home/Delete/42', '/home/mine']) {
        window.location = { pathname, hash: '' };
        listeners.hashchange.forEach(handler => handler());
        assert.equal(links[1].attributes['aria-current'], 'page', pathname);
        assert.equal(links.filter(link => link.attributes['aria-current'] === 'page').length, 1);
    }
    window.location = { pathname: '/Operations', hash: '#resources' };
    listeners.hashchange.forEach(handler => handler());
    assert.equal(links[2].attributes['aria-current'], 'page');
    assert.equal(links[1].attributes['aria-current'], undefined);
    assert.equal(section.open, true);
    assert.equal(section.scrolled, true);
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
    assert.match(home, /User.IsInRole\("Admin"\)/);
    assert.doesNotMatch(home, /hero-title-big|hero-photo/);
    const resources = read('Views/Home/Verify.cshtml');
    assert.match(resources, /ResourceOperationalStatus.From/);
    assert.doesNotMatch(resources, /Operativ vurdering|@status.StatusText/);
    assert.match(resources, /<label for="eligible">Operativ status<\/label>/);
    assert.match(resources, /<option value="true" selected="@\(ViewBag.Eligible is true\)">Kan forespørres nå/);
    assert.match(resources, /Sist oppdatert/);
    assert.doesNotMatch(resources, /Owner\?\.UserName/);
    assert.match(read('Views/Home/Delete.cshtml'), /name="version"/);
});

test('resource cards have bounded theme-aware surfaces, readable facts and separated destructive actions', () => {
    const view = read('Views/Home/Verify.cshtml');
    const css = read('wwwroot/css/workspace.css');
    const tokens = read('wwwroot/css/site.css');
    assert.match(view, /class="data-container resource-overview"/);
    assert.match(view, /class="resource-facts"[\s\S]*?<dl>[\s\S]*?<dt>Sist oppdatert<\/dt>/);
    assert.match(view, /class="resource-status"[\s\S]*?@status.Availability[\s\S]*?Godkjenning: @status.Verification/);
    assert.doesNotMatch(view, /Operativ vurdering|data-resource-status-text/);
    assert.match(view, /ResourcePermissions.CanManage\(User, r.OwnerId\)[\s\S]*?class="resource-destructive-actions"[\s\S]*?asp-action="Delete"/);
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
