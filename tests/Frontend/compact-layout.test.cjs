const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');

test('desktop workspace uses remaining viewport with internal results scrolling', () => {
    const css = read('wwwroot/css/workspace-map.css');
    assert.match(css, /--workspace-height: calc\(100dvh - 206px\)/);
    assert.match(css, /max-height: var\(--workspace-height\)/);
    assert.match(css, /height: calc\(var\(--workspace-height\) - 28px\)/);
    assert.match(css, /overflow-y: auto/);
    assert.doesNotMatch(css, /min-height: 480px/);
    assert.match(css, /@media \(max-height: 600px\)[\s\S]*max-height: none; overflow: visible/);
    assert.match(css, /@media \(max-width: 760px\)[\s\S]*max-height: none;[\s\S]*overflow: visible/);
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
    assert.match(read('Views/Home/Form.cshtml'), />Meld behov<\/button>/);
});
