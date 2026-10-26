const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync, existsSync } = require('node:fs');
const { resolve } = require('node:path');
const root = resolve(__dirname, '../..');
const read = file => readFileSync(resolve(root, file), 'utf8');

test('both home states share a local photograph and practical guidance instead of navigation tiles', () => {
    const home = read('Views/Home/Index.cshtml');
    assert.doesNotMatch(home, /Hva vil du gjøre|feature-grid|feature-card|dashboard/);
    assert.equal((home.match(/class="home-photo"/g) || []).length, 1);
    const photo = home.match(/<img[\s\S]*?class="home-photo" \/>/)[0];
    assert.match(photo, /src="~\/images\/Heimevernet-background.jpg"/);
    assert.match(photo, /alt="[^"]+"/);
    assert.match(photo, /width="1024" height="707"/);
    assert.ok(existsSync(resolve(root, 'wwwroot/images/Heimevernet-background.jpg')));
    assert.ok(home.indexOf(photo) > home.indexOf('Opprett brukerkonto'));
    assert.match(home, /<section class="home-hero" aria-labelledby="home-title">/);
    assert.match(home, /<section class="home-guide" aria-labelledby="home-guide-title">/);
    assert.match(home, /<ol class="home-steps">/);
    for (const heading of ['Start med området', 'Gi ressursen en plass', 'Hold bidraget ditt relevant'])
        assert.ok(home.includes(`<h3>${heading}</h3>`));
});

test('home actions use existing authenticated and anonymous routes and retain the operations permission gate', () => {
    const home = read('Views/Home/Index.cshtml');
    const hero = home.slice(home.indexOf('@if (User.Identity'), home.indexOf('<img'));
    const [authenticated, anonymous] = hero.split('else');
    assert.match(authenticated, /asp-action="Map" class="btn-submit"/);
    assert.match(authenticated, /asp-action="Map" asp-route-register="true" class="btn-secondary"/);
    assert.doesNotMatch(authenticated, /asp-controller="Account"/);
    assert.match(anonymous, /asp-controller="Account" asp-action="Login" class="btn-submit"/);
    assert.match(anonymous, /asp-controller="Account" asp-action="Register" class="btn-secondary"/);
    assert.doesNotMatch(anonymous, /asp-action="Map"|asp-controller="Operations"/);
    for (const state of [authenticated, anonymous])
        assert.equal((state.match(/class="btn-submit"/g) || []).length, 1);
    assert.match(home, /canUseOperations = User.Identity\?\.IsAuthenticated == true\s*&& !string.IsNullOrEmpty\(User.FindFirst\(System.Security.Claims.ClaimTypes.NameIdentifier\)\?\.Value\)\s*&& \(User.IsInRole\("Bruker"\) \|\| User.IsInRole\("Admin"\)\)/);
    assert.match(home, /@if \(canUseOperations\)\s*\{\s*<a asp-controller="Operations" asp-action="Index">/);
    assert.match(home, /@if \(User.Identity\?\.IsAuthenticated == true\)\s*\{\s*<a asp-action="Resources" asp-route-mine="true">/);
    assert.match(anonymous, /Studentprosjekt · prototype.*Ikke en offisiell tjeneste/);
});

test('home layout is scoped, theme-aware and stacks the image and guidance on mobile', () => {
    const css = read('wwwroot/css/home.css');
    assert.match(css, /\.home-hero \{[^}]*grid-template-columns: 1fr 1fr/);
    assert.match(css, /\.home-photo \{[^}]*width: 100%; height: auto;[^}]*object-fit: cover/);
    assert.match(css, /@media \(max-width: 767px\)[\s\S]*\.home-hero \{ grid-template-columns: 1fr/);
    assert.match(css, /@media \(max-width: 767px\)[\s\S]*\.home-steps \{ grid-template-columns: 1fr/);
    for (const token of ['heading', 'muted', 'accent', 'border'])
        assert.ok(css.includes(`var(--${token})`));
    assert.doesNotMatch(css, /background-image|overlay|100vh/);
    assert.match(read('wwwroot/css/workspace.css'), /a:focus-visible[^}]*outline: 3px solid var\(--accent\)/);
});

test('layout removes the unused footer while retaining accurate prototype branding and navigation', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.doesNotMatch(layout, /<footer|Studentprototype, ikke en offisiell beredskapstjeneste/);
    assert.match(layout, /Studentprosjekt · prototype/);
    assert.match(layout, /aria-label="Hovedmeny"/);
    assert.match(layout, /@RenderBody\(\)/);
});
