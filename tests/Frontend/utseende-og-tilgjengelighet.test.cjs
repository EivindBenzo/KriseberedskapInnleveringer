// Sjekker grunnleggende universell utforming: tastatur, skjermleser, lesbare farger og at tema velges på serveren.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync, readdirSync } = require('node:fs');
const { resolve, join } = require('node:path');

const root = resolve(__dirname, '../..');
const read = file => readFileSync(resolve(root, file), 'utf8');

function filer(mappe, endelse) {
    return readdirSync(resolve(root, mappe), { withFileTypes: true }).flatMap(f =>
        f.isDirectory() ? filer(join(mappe, f.name), endelse) : f.name.endsWith(endelse) ? [join(mappe, f.name)] : []);
}

// Henter innholdet i en CSS-regel, f.eks. alt mellom { og } for «.map-workspace».
function regel(css, selektor) {
    const escaped = selektor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const treff = css.match(new RegExp(`(?:^|})\\s*${escaped}\\s*\\{([^}]+)\\}`));
    assert.ok(treff, `Mangler CSS-regel: ${selektor}`);
    return treff[1];
}

test('menyen kan brukes med tastatur og skjermleser', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /<html lang="no"/);
    assert.match(layout, /<a class="skip-link" href="#main">Hopp til innhold<\/a>/);
    assert.match(layout, /<main id="main"/);
    assert.match(layout, /aria-label="Hovedmeny"/);
    // Aktivt menypunkt markeres på serveren, slik at skjermlesere sier «gjeldende side».
    assert.match(layout, /string\? Aktiv\(string c, params string\[\] actions\)/);
    assert.match(layout, /aria-current="@Aktiv\("Home", "Dashboard"\)"/);
    // Mobilmenyen åpnes med en avkrysningsboks og CSS, uten JavaScript.
    assert.match(layout, /<input type="checkbox" id="menuToggle" class="menu-checkbox" aria-controls="navMenu" \/>/);
    assert.match(layout, /<label for="menuToggle" class="menu-toggle" aria-label="Meny">/);
    assert.match(layout, /Studentprosjekt · prototype/);
});

test('alt som kan klikkes, har synlig fokus og er stort nok til å treffe', () => {
    const css = read('wwwroot/css/workspace.css');
    assert.match(css, /a:focus-visible, button:focus-visible[^{]*\{[^}]*outline:/);
    assert.match(css, /min-height: 44px/, 'klikkflater er minst 44 piksler høye');
    assert.doesNotMatch(css, /outline:\s*(?:none|0)\s*;[^}]*\}[^{]*:focus-visible/);
});

test('alle bilder har alternativ tekst', () => {
    for (const fil of filer('Views', '.cshtml')) {
        for (const bilde of read(fil).match(/<img\b[^>]*>/g) ?? [])
            assert.match(bilde, /\balt=/, `${fil}: ${bilde.slice(0, 60)}`);
    }
});

test('tema velges på serveren med en cookie', () => {
    const layout = read('Views/Shared/_Layout.cshtml');
    assert.match(layout, /<html lang="no" data-theme="@tema">/);
    assert.match(layout, /<form asp-controller="Tema" asp-action="Bytt" method="post">/);
    const controller = read('Controllers/TemaController.cs');
    assert.match(controller, /\[HttpPost, ValidateAntiForgeryToken\]/);
    assert.match(controller, /Url\.IsLocalUrl\(returnUrl\) \? LocalRedirect\(returnUrl\)/);
});

// Kontrast etter WCAG: forholdet mellom lysstyrken til to farger. 4,5 er kravet for vanlig tekst.
function lysstyrke(hex) {
    const kanaler = hex.match(/[\da-f]{2}/gi).map(verdi => {
        const k = parseInt(verdi, 16) / 255;
        return k <= .04045 ? k / 12.92 : ((k + .055) / 1.055) ** 2.4;
    });
    return kanaler[0] * .2126 + kanaler[1] * .7152 + kanaler[2] * .0722;
}
function kontrast(a, b) {
    const [lys, mørk] = [lysstyrke(a), lysstyrke(b)].sort((x, y) => y - x);
    return (lys + .05) / (mørk + .05);
}

test('teksten er lesbar på kartsidens mørke flater', () => {
    const mørk = regel(read('wwwroot/css/workspace.css'), ':root[data-theme="dark"] .map-workspace');
    const palett = regel(read('wwwroot/css/site.css'), ':root[data-theme="dark"]');
    const farge = (kilde, navn) => kilde.match(new RegExp(`--${navn}: (#[\\da-f]{6})`))[1];
    for (const flate of ['surface', 'section-surface', 'input-surface']) {
        const bakgrunn = farge(mørk, `workspace-${flate}`);
        for (const tekst of ['text', 'heading', 'muted', 'error'])
            assert.ok(kontrast(farge(palett, tekst), bakgrunn) >= 4.5, `${tekst} på ${flate}`);
    }
    assert.ok(kontrast(farge(mørk, 'workspace-section-border'), farge(mørk, 'workspace-input-surface')) >= 3, 'feltkanter synes');
});
