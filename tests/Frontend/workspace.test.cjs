const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('navigation exposes operational destinations, prototype identity and keyboard escape', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /Studentprosjekt · prototype/);
    for (const action of ['Resources', 'Mine', 'Queue', 'Needs', 'Requests'])
        assert.match(layout, new RegExp(`asp-action="${action}"`));
    assert.match(layout, /aria-current/);
    assert.match(layout, /event.key === 'Escape'/);
    assert.match(layout, /Hopp til innhold/);
    assert.match(layout, /asp-controller="Account" asp-action="Logout" method="post"/);
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

test('dashboard is permission-aware and resource overview separates eligibility from registration', () => {
    const home = read('Views/Home/Index.cshtml');
    assert.match(home, /User.Identity\?\.IsAuthenticated/);
    assert.match(home, /Hva vil du gjøre/);
    assert.match(home, /User.IsInRole\("Admin"\)/);
    assert.doesNotMatch(home, /hero-title-big|hero-photo/);
    const resources = read('Views/Home/Verify.cshtml');
    assert.match(resources, /WorkflowEligibility.IsEligible/);
    assert.match(resources, /Godkjenning utløpt/);
    assert.match(resources, /Sist oppdatert/);
    assert.doesNotMatch(resources, /Owner\?\.UserName/);
    assert.match(read('Views/Home/Delete.cshtml'), /name="version"/);
});
