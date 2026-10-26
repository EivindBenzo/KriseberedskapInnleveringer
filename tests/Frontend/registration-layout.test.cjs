const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = file => readFileSync(resolve(__dirname, '../..', file), 'utf8');
const css = read('wwwroot/css/workspace-map.css');
const theme = read('wwwroot/css/workspace.css');
const view = read('Views/Home/Map.cshtml');

function rule(source, selector) {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const match = source.match(new RegExp(`(?:^|})\\s*${escaped}\\s*\\{([^}]+)\\}`));
    assert.ok(match, `Missing CSS rule: ${selector}`);
    return match[1];
}

test('registration fieldset headings participate in flow instead of cutting through borders', () => {
    assert.match(rule(css, '.map-workspace fieldset.registration-section'), /display: flow-root/);
    const legend = rule(css, '.map-workspace .registration-section legend');
    assert.match(legend, /float: inline-start/);
    assert.match(legend, /width: 100%/);
    assert.match(legend, /border-bottom: 1px solid var\(--workspace-section-border\)/);
    assert.match(rule(css, '.map-workspace .registration-section legend + *'), /clear: both/);
    for (const block of [legend, rule(css, '.map-workspace .registration-section')]) {
        assert.doesNotMatch(block, /position:|transform:|(?:^|;)\s*(?:top|left|right|bottom):|margin[^:]*:\s*-/);
    }
});

test('optional sections share the fieldset inset and keep native disclosure semantics', () => {
    assert.match(rule(css, '.map-workspace .registration-section'), /padding: \.85rem/);
    assert.match(rule(css, '.map-workspace details.registration-section'), /padding: 0 \.85rem/);
    const summary = rule(css, '.map-workspace .registration-section > summary');
    assert.match(summary, /display: list-item/);
    assert.match(summary, /min-height: 44px/);
    assert.match(rule(css, '.map-workspace details.registration-section[open]'), /padding-bottom: \.85rem/);
    assert.match(rule(css, '.map-workspace .registration-section[open] > summary'), /margin-bottom: \.75rem/);
    assert.match(view, /<details class="registration-section"[^>]*>[\s\S]*?<summary>Kapasitet og transportbehov/);
});

test('registration content stays inside the sidebar without clipping feedback or map placement', () => {
    const section = rule(css, '.map-workspace .registration-section');
    assert.match(section, /min-inline-size: 0/);
    assert.match(section, /width: 100%/);
    assert.match(section, /overflow-wrap: anywhere/);
    assert.doesNotMatch(section, /overflow:\s*(?:hidden|clip)/);
    assert.match(rule(css, '.map-workspace .form-control'), /min-width: 0;[\s\S]*max-width: 100%/);
    assert.match(rule(css, '.map-workspace .workspace-sidebar'), /overflow-y: auto/);
    assert.match(css, /@media \(max-width: 760px\)[\s\S]*\.workspace-sidebar \{[^}]*overflow-y: auto/);
    for (const field of ['ResourceName', 'ResourceType', 'Availability', 'Capability', 'TransportRequirements', 'Latitude', 'Longitude']) {
        assert.match(view, new RegExp(`asp-validation-for="${field}"`));
    }
    for (const id of ['resourceName', 'resourceType', 'availability', 'capability', 'transportRequirements', 'latitude', 'longitude', 'locationMapLink', 'registrationCancel']) {
        assert.ok(view.includes(`id="${id}"`), `Preserve ${id}`);
    }
    assert.match(view, /id="resourceName"[^>]*minlength="2"[^>]*maxlength="100" required/);
    assert.match(view, /id="resourceType"[^>]*required/);
    assert.match(view, /id="selectedPlace" role="status"/);
});

test('navy surfaces are scoped to the map and light mode inherits the existing palette', () => {
    const light = rule(theme, '.map-workspace');
    const dark = rule(theme, ':root[data-theme="dark"] .map-workspace');
    for (const token of ['surface', 'section-surface', 'input-surface', 'section-border', 'surface-border', 'hover']) {
        assert.match(light, new RegExp(`--workspace-${token}: var\\(--[\\w-]+\\)`));
        assert.match(dark, new RegExp(`--workspace-${token}: #[\\da-f]{6}`));
    }
    assert.match(rule(css, '.map-workspace .workspace-sidebar'), /background: var\(--workspace-surface\)/);
    assert.match(rule(css, '.map-workspace .registration-section'), /background: var\(--workspace-section-surface\)/);
    for (const selector of ['.map-workspace .form-control', '.map-workspace .form-control:focus', '.map-workspace .form-control option']) {
        assert.match(rule(css, selector), /background: var\(--workspace-input-surface\)/);
    }
});

function luminance(hex) {
    const channels = hex.match(/[\da-f]{2}/gi).map(value => {
        const channel = parseInt(value, 16) / 255;
        return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4;
    });
    return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722;
}

function contrast(a, b) {
    const values = [luminance(a), luminance(b)].sort((x, y) => y - x);
    return (values[0] + .05) / (values[1] + .05);
}

test('dark registration surfaces retain readable inherited text and control boundaries', () => {
    const dark = rule(theme, ':root[data-theme="dark"] .map-workspace');
    const palette = rule(read('wwwroot/css/site.css'), ':root[data-theme="dark"]');
    const color = (source, token) => source.match(new RegExp(`--${token}: (#[\\da-f]{6})`))[1];
    for (const surface of ['surface', 'section-surface', 'input-surface']) {
        const background = color(dark, `workspace-${surface}`);
        for (const text of ['text', 'heading', 'muted', 'error']) {
            assert.ok(contrast(color(palette, text), background) >= 4.5, `${text} on ${surface}`);
        }
    }
    assert.ok(contrast(color(dark, 'workspace-section-border'), color(dark, 'workspace-input-surface')) >= 3);
});
