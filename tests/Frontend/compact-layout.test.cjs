const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('desktop workspace uses remaining viewport with internal results scrolling', () => {
    const css = read('wwwroot/css/workspace-map.css');
    assert.match(css, /body:has\(\.map-workspace\) \{[^}]*height: 100dvh;[^}]*min-height: 0/);
    assert.match(css, /\.map-workspace \{[^}]*flex: 1;[^}]*min-height: 0/);
    assert.match(css, /\.workspace-grid \{[^}]*flex: 1;[^}]*min-height: 0/);
    assert.match(css, /overflow-y: auto/);
    assert.doesNotMatch(css, /min-height: 480px/);
    assert.doesNotMatch(css, /--workspace-height|min-height: 3[23]0px|overflow: hidden/);
    // På mobil vises kartet øverst og listen under, uten en egen Kart/Liste-knapp.
    assert.match(css, /@media \(max-width: 760px\)[\s\S]*\.workspace-map-region \{[^}]*order: -1/);
    assert.match(css, /@media \(max-width: 760px\)[\s\S]*#map \{[^}]*height: 60vh/);
    assert.doesNotMatch(css, /data-mobile-view/);
});

test('compact pages retain readable controls and hide only empty feedback', () => {
    const css = read('wwwroot/css/workspace.css');
    const map = read('wwwroot/css/workspace-map.css');
    const account = read('wwwroot/css/account.css');
    assert.match(css, /\[role="status"\]:empty/);
    assert.match(css, /\.validation-summary-valid\[role="alert"\]/);
    for (const styles of [css, map, account]) {
        assert.match(styles, /min-height: 44px/);
        assert.doesNotMatch(styles, /(?:^|[;{\s])zoom\s*:|transform:\s*scale\(/);
    }
    assert.match(css, /\.main-content \{[^}]*min-height: 0/);
    assert.match(read('Views/Behov/Registrer.cshtml'), />Meld behov<\/button>/);
});
