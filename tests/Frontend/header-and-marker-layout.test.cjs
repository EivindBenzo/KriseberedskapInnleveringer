const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('shared header reserves scrollbar space and stable desktop slots independently of the active route', () => {
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /html \{ scrollbar-gutter: stable; \}/);
    assert.match(css, /body > header \{ flex: none; align-self: stretch; \}/);
    assert.match(css, /\.nav-container \{[^}]*display: grid/);
    assert.match(css, /\.nav-brand > span \{ min-width: 0; overflow-wrap: anywhere; \}/);
    assert.match(css, /grid-template-columns: minmax\(0, 1fr\) max-content minmax\(0, 1fr\)/);
    const link = css.match(/\.nav-menu \.nav-link \{([^}]+)\}/)[1];
    const active = css.match(/\.nav-menu \.nav-link\[aria-current="page"\] \{([^}]+)\}/)[1];
    assert.match(link, /font-weight: 700/);
    assert.doesNotMatch(active, /padding:|margin:|border:|font-size:|transform:|width:/);
    assert.match(css, /\.nav-menu \.nav-link:focus-visible/);
    assert.match(css, /@media \(max-width: 767px\)[\s\S]*\.nav-menu \{[^}]*position: absolute;[^}]*overflow-y: auto/);
    const layout = read('Views/Shared/_Layout.cshtml');
    // Aktivt menypunkt bestemmes på serveren (Aktiv-funksjonen i layouten), ikke med JavaScript.
    assert.match(layout, /string\? Aktiv\(string c, params string\[\] actions\)/);
    assert.match(layout, /aria-current="@Aktiv\("Home", "Resources", "Mine", "Edit", "Delete"\)"/);
    assert.doesNotMatch(layout, /<script/);
    assert.match(layout, /aria-controls="navMenu"/);
    assert.match(layout, /@if \(canUseOperations\)/);
    assert.match(read('wwwroot/css/site.css'), /grid-template-rows: max-content minmax\(min-content, 1fr\)/);
});

test('resource icons are chosen on the server and the SVG files have a thin outline', () => {
    const css = read('wwwroot/css/site.css');
    assert.doesNotMatch(css, /drop-shadow\(0 0 0\.75px/);
    const map = read('Views/Home/Map.cshtml');
    const summary = map.match(/<summary><img src="@Url\.Content\(RessursIkon\.Sti\(typeNavn, status\)\)"[\s\S]*?<\/summary>/)[0];
    assert.match(summary, /width="32" height="32"/);
    assert.doesNotMatch(summary, /<svg|style=/);
    for (const file of ['car', 'excavator', 'drone', 'generator', 'personnel', 'toolbox', 'other', 'unknown']) {
        for (const suffix of ['', '-available', '-unavailable']) {
            const svg = read(`wwwroot/images/resources/${file}${suffix}.svg`);
            assert.match(svg, /stroke="#000" stroke-width="1"/);
            assert.doesNotMatch(svg, /<filter|<fe|style=/);
        }
    }
});
