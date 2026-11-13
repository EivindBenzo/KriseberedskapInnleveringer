// Støtte for «Meld behov»: trekkspill, oppsummering, sjekkliste, stedssøk og lokalt utkast.
// Selve kartklikket håndteres av kart.js (window.kartSettPunkt).
(function () {
    const form = document.getElementById('needForm');
    if (!form) return;
    const $ = function (id) { return document.getElementById(id); };
    const type = form.querySelector('[name="BehovstypeID"]');
    const beskrivelse = form.querySelector('[name="Beskrivelse"]');
    const prio = form.querySelector('[name="Prioritet"]');
    const antall = form.querySelector('[name="Antall"]');
    const frist = form.querySelector('[name="Frist"]');
    const lat = $('needLat'), lng = $('needLng');
    const prioTekst = { akutt: 'Akutt', planlagt: 'Planlagt' };

    // Bare ett avsnitt er åpent om gangen.
    const acc = Array.prototype.slice.call(form.querySelectorAll('[data-acc]'));
    acc.forEach(function (d) {
        d.addEventListener('toggle', function () {
            if (!d.open) return;
            const scroller = d.parentElement, before = d.getBoundingClientRect().top;
            acc.forEach(function (o) { if (o !== d) o.open = false; });
            scroller.scrollTop += d.getBoundingClientRect().top - before;
        });
    });

    function valgtTekst(sel) { return sel && sel.selectedIndex > 0 ? sel.options[sel.selectedIndex].text : '–'; }
    function fristTekst() {
        if (!frist || !frist.value) return '–';
        const d = new Date(frist.value);
        return isNaN(d) ? '–' : d.toLocaleString('nb-NO', { dateStyle: 'short', timeStyle: 'short' });
    }
    function sjekk(id, ok) { $(id).classList.toggle('is-ok', ok); }

    function oppdater() {
        $('nbCount').textContent = beskrivelse.value.length;
        const p = prio.value ? (prioTekst[prio.value] || prio.value) : '–';
        const a = antall.value || '–';
        $('nbPrioSum').textContent = 'Prioritet: ' + p + ' · Antall: ' + a;
        $('sumType').textContent = valgtTekst(type);
        $('sumPrio').textContent = p;
        $('sumAntall').textContent = a;
        $('sumFrist').textContent = fristTekst();
        sjekk('ckDesc', beskrivelse.value.trim().length >= 10);
        sjekk('ckPlace', !!(lat.value && lng.value));
        sjekk('ckPrio', !!prio.value && Number(antall.value) > 0);
        $('nbCoords').textContent = lat.value && lng.value ? lat.value + ', ' + lng.value : '–';
        document.querySelectorAll('.nb-steps li').forEach(function (li, i) {
            li.classList.toggle('is-done', i === 0 ? beskrivelse.value.trim().length >= 10 : i === 1 ? !!(lat.value && lng.value) : false);
        });
    }
    form.addEventListener('input', oppdater);
    form.addEventListener('change', oppdater);
    document.addEventListener('kart:plassering', oppdater);

    // Stedsnavn for valgt punkt vises i kortet.
    document.addEventListener('kart:plassering', function (e) {
        $('needPlace').textContent = 'Valgt punkt: ' + e.detail.lat.toFixed(5) + ', ' + e.detail.lng.toFixed(5);
    });

    // Søk etter sted via serveren.
    const sok = $('nbSearch'), liste = $('nbResults');
    let tidtaker = 0;
    function vis(treff) {
        liste.innerHTML = '';
        liste.hidden = treff.length === 0;
        treff.forEach(function (t) {
            const li = document.createElement('li');
            const b = document.createElement('button');
            b.type = 'button'; b.textContent = t.label;
            b.addEventListener('click', function () {
                liste.hidden = true; sok.value = t.label;
                if (window.kartInstans) window.kartInstans.setView([t.lat, t.lng], 13);
                if (window.kartSettPunkt) window.kartSettPunkt({ lat: t.lat, lng: t.lng });
                $('needPlace').textContent = t.label;
            });
            li.appendChild(b); liste.appendChild(li);
        });
    }
    sok.addEventListener('input', function () {
        clearTimeout(tidtaker);
        const q = sok.value.trim();
        if (q.length < 2) { vis([]); return; }
        tidtaker = setTimeout(function () {
            fetch('/Behov/Sok?q=' + encodeURIComponent(q), { headers: { Accept: 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(function (j) { vis(j.map(function (x) { return { lat: x.lat, lng: x.lng, label: x.label }; })); })
                .catch(function () { vis([]); });
        }, 300);
    });
    sok.addEventListener('keydown', function (e) { if (e.key === 'Enter') e.preventDefault(); });

    $('nbLocate').addEventListener('click', function () {
        if (!navigator.geolocation || !window.kartInstans) return;
        navigator.geolocation.getCurrentPosition(function (pos) {
            window.kartInstans.setView([pos.coords.latitude, pos.coords.longitude], 14);
        });
    });
    $('nbFull').addEventListener('click', function () {
        const w = document.querySelector('.nb-mapwrap');
        w.classList.toggle('is-full');
        if (window.kartInstans) setTimeout(function () { window.kartInstans.invalidateSize(); }, 50);
    });
    $('nbChange').addEventListener('click', function () {
        const m = $('map'); m.focus({ preventScroll: true });
    });

    // Lokalt utkast (lagres bare i nettleseren).
    const nøkkel = 'behov-utkast';
    $('nbDraft').addEventListener('click', function () {
        const d = { t: type.value, b: beskrivelse.value, p: prio.value, a: antall.value, f: frist ? frist.value : '', la: lat.value, ln: lng.value };
        localStorage.setItem(nøkkel, JSON.stringify(d));
        const knapp = $('nbDraft'); const gammel = knapp.textContent;
        knapp.textContent = 'Utkast lagret ✓'; setTimeout(function () { knapp.textContent = gammel; }, 1800);
    });
    try {
        const d = JSON.parse(localStorage.getItem(nøkkel) || 'null');
        if (d && !beskrivelse.value && !lat.value) {
            if (d.t) type.value = d.t; beskrivelse.value = d.b || ''; if (d.p) prio.value = d.p; if (d.a) antall.value = d.a;
            if (frist && d.f) frist.value = d.f;
            if (d.la && d.ln && window.kartSettPunkt) window.kartSettPunkt({ lat: parseFloat(d.la), lng: parseFloat(d.ln) });
        }
    } catch (e) { }
    form.addEventListener('submit', function () { localStorage.removeItem(nøkkel); });
    oppdater();
})();
