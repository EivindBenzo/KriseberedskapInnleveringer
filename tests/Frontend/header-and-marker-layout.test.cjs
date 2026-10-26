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
    assert.match(layout, /setAttribute\('aria-current', 'page'\)/);
    assert.match(layout, /aria-controls="navMenu"/);
    assert.match(layout, /@if \(canUseOperations\)/);
    assert.match(read('wwwroot/css/site.css'), /grid-template-rows: max-content minmax\(min-content, 1fr\)/);
});

test('map-only upward pointer sits below restored art with a two CSS pixel outline', () => {
    const css = read('wwwroot/css/map-controls.css');
    const pointer = css.match(/\.resource-marker::before \{([^}]+)\}/)[1];
    assert.match(pointer, /bottom: 0/);
    assert.doesNotMatch(pointer, /top:/);
    assert.match(pointer, /border-bottom: 8px solid #000/);
    assert.match(css, /\.resource-marker > img \{[^}]*filter: url\(#resource-map-outline\)/);
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /<feMorphology in="SourceAlpha" operator="dilate" radius="2"/);
    assert.match(layout, /<feMergeNode in="SourceGraphic"/);
    assert.doesNotMatch(read('wwwroot/js/resource-icons.js'), /border-bottom|resource-marker.*<span/);
});
