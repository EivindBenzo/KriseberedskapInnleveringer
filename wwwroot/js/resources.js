(function () {
    var liste = document.getElementById('rlList');
    if (!liste) return;

    function hent(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
    function lagre(k, v) { try { localStorage.setItem(k, v); } catch (e) { } }

    var visningsknapper = document.querySelectorAll('[data-rl-view]');
    function velgVisning(v) {
        liste.classList.toggle('rl-grid', v !== 'list');
        liste.classList.toggle('rl-list', v === 'list');
        visningsknapper.forEach(function (b) { b.setAttribute('aria-pressed', String(b.getAttribute('data-rl-view') === v)); });
        lagre('res-visning', v);
    }
    visningsknapper.forEach(function (b) {
        b.addEventListener('click', function () { velgVisning(b.getAttribute('data-rl-view')); });
    });
    velgVisning(hent('res-visning') === 'list' ? 'list' : 'grid');

    var sok = document.querySelector('[data-rl-search]');
    document.addEventListener('keydown', function (e) {
        var mål = e.target && e.target.tagName;
        if (e.key === '/' && sok && mål !== 'INPUT' && mål !== 'TEXTAREA' && mål !== 'SELECT') {
            e.preventDefault();
            sok.focus();
        }
    });

    var sortering = document.querySelector('[data-rl-sort]');
    var skjema = document.getElementById('rlForm');
    if (sortering && skjema) {
        sortering.addEventListener('change', function () {
            skjema.querySelector('input[name=sort]').value = sortering.value;
            skjema.submit();
        });
    }

    function lagKart(el) {
        if (!window.L || el.dataset.klar) return;
        el.dataset.klar = '1';
        var pos = [parseFloat(el.dataset.lat), parseFloat(el.dataset.lng)];
        var kart = L.map(el, { center: pos, zoom: 13, minZoom: 3, maxZoom: 18, scrollWheelZoom: false });
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '&copy; OpenStreetMap' }).addTo(kart);
        L.tileLayer('https://cache.kartverket.no/v1/wmts/1.0.0/topo/default/webmercator/{z}/{y}/{x}.png', {
            maxZoom: 18, attribution: '&copy; <a href="https://www.kartverket.no/">Kartverket</a>'
        }).addTo(kart);
        L.circle(pos, { radius: 600, color: '#087181', weight: 2, fillColor: '#087181', fillOpacity: .15 }).addTo(kart);
        L.marker(pos, {
            icon: L.divIcon({
                className: 'rc-pin',
                html: '<span><img src="' + el.dataset.icon + '" alt="" width="26" height="26"></span>',
                iconSize: [40, 40], iconAnchor: [20, 20]
            }),
            title: el.dataset.label
        }).addTo(kart);
        var full = L.DomUtil.create('button', 'rc-full leaflet-bar');
        full.type = 'button';
        full.title = 'Fullskjerm';
        full.setAttribute('aria-label', 'Fullskjerm');
        full.innerHTML = '&#x26F6;';
        L.DomEvent.disableClickPropagation(full);
        full.addEventListener('click', function () {
            el.classList.toggle('is-full');
            setTimeout(function () { kart.invalidateSize(); kart.setView(pos); }, 60);
        });
        el.appendChild(full);
        setTimeout(function () { kart.invalidateSize(); }, 80);
    }

    liste.addEventListener('click', function (e) {
        if (e.target.closest('a, button, input, select, label, .rc-detail')) return;
        var kort = e.target.closest('[data-rc]');
        if (!kort) return;
        var detalj = kort.querySelector('.rc-detail');
        var åpen = detalj.hidden;
        if (åpen) {
            liste.querySelectorAll('[data-rc].is-open').forEach(function (annen) {
                if (annen === kort) return;
                var d = annen.querySelector('.rc-detail');
                if (d) d.hidden = true;
                annen.classList.remove('is-open');
                annen.setAttribute('aria-expanded', 'false');
            });
        }
        detalj.hidden = !åpen;
        kort.classList.toggle('is-open', åpen);
        kort.setAttribute('aria-expanded', String(åpen));
        if (åpen) {
            var kartEl = detalj.querySelector('.rc-map');
            if (kartEl) lagKart(kartEl);
            kort.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }
    });
})();
