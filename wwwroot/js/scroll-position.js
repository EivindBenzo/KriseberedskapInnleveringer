// Beholder scroll-posisjonen når et skjema sendes og siden lastes på nytt.
// Eksempel: dispatch trykker «Sett til fremme» langt nede på Oversikt. Serveren sender brukeren
// tilbake til samme side, og uten dette skriptet ville siden startet øverst igjen.
(function () {
    'use strict';
    const key = 'heimevernet-scroll';
    const maxAgeMs = 15000;

    function read() {
        try { return JSON.parse(sessionStorage.getItem(key) || 'null'); } catch { return null; }
    }
    function write(value) {
        try { sessionStorage.setItem(key, JSON.stringify(value)); } catch { /* lagring kan være blokkert */ }
    }
    function clear() {
        try { sessionStorage.removeItem(key); } catch { /* ignorer */ }
    }

    // Husk posisjonen når et POST-skjema sendes.
    document.addEventListener('submit', function (event) {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || (form.method || '').toLowerCase() !== 'post') return;
        write({ path: window.location.pathname, y: window.scrollY, at: Date.now() });
    }, true);

    // Gå tilbake til posisjonen hvis vi kom tilbake til samme side rett etter innsending.
    function restore() {
        const saved = read();
        clear();
        if (!saved || saved.path !== window.location.pathname || Date.now() - saved.at > maxAgeMs) return;
        if (window.location.hash) return; // en lenke til et bestemt sted på siden vinner
        window.scrollTo({ top: saved.y, left: 0, behavior: 'instant' });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', restore);
    else restore();
})();
