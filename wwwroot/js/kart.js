// Tegner kartet med Leaflet. Leaflet er et JavaScript-bibliotek, så litt JavaScript trengs for kartet.
// Alt innholdet (hvor kartet starter, markører, symboler og tekst) lages på serveren i C#
// (KartVisning) og ligger som JSON i <script id="kartdata">. Dette skriptet bare tegner det.
(function () {
    const data = JSON.parse(document.getElementById('kartdata').textContent);
    // Kartet kan flyttes fritt. Kartverkets topografiske kart er svært detaljert i Norge, og et vanlig gatekart
    // ligger under, slik at man også ser noe når man zoomer ut eller flytter seg utenfor Kartverkets dekning.
    const kart = L.map('map', { minZoom: 3, worldCopyJump: true, zoomSnap: 0.5 }).setView([data.lat, data.lng], data.zoom);
    window.kartInstans = kart;
    const tomRute = 'data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==';
    const gatekart = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    });
    const topo = L.layerGroup([
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19 }),
        L.tileLayer('https://cache.kartverket.no/v1/wmts/1.0.0/topo/default/webmercator/{z}/{y}/{x}.png', {
            minZoom: 3, maxZoom: 19, maxNativeZoom: 18, bounds: [[57, 3], [81, 34]], errorTileUrl: tomRute,
            attribution: '&copy; <a href="https://www.kartverket.no/">Kartverket</a>'
        })
    ]);
    const flyfoto = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
        maxZoom: 19, attribution: 'Tiles &copy; Esri'
    });
    topo.addTo(kart);
    L.control.layers({ 'Topografisk (Kartverket)': topo, 'Gatekart': gatekart, 'Flyfoto': flyfoto }, null, { position: 'topright' }).addTo(kart);

    // Kartet åpner der brukeren er. Vi lar være når siden allerede har et bestemt sted: et søk, en ressurs
    // som skal vises eller et punkt som er valgt i et skjema.
    let brukerPosisjon = null;
    let brukerMarkor = null;
    const harBestemtSted = !!data.melding || data.markorer.some(function (m) { return m.apen; }) ||
        (data.plassering && data.plassering.lat != null) || /[?&]sted=/.test(window.location.search);
    function visBrukerPosisjon(pos, flytt) {
        brukerPosisjon = L.latLng(pos.coords.latitude, pos.coords.longitude);
        if (brukerMarkor) brukerMarkor.setLatLng(brukerPosisjon);
        else {
            brukerMarkor = L.marker(brukerPosisjon, {
                interactive: false, keyboard: false, zIndexOffset: -500,
                icon: L.divIcon({ className: 'map-user-wrapper', html: '<span class="map-user-dot"></span>', iconSize: [22, 22], iconAnchor: [11, 11] })
            }).addTo(kart);
        }
        if (flytt) kart.setView(brukerPosisjon, 13);
        oppdaterAvstand();
    }
    function finnBruker(flytt) {
        if (!navigator.geolocation) return;
        navigator.geolocation.getCurrentPosition(function (pos) { visBrukerPosisjon(pos, flytt); }, function () { /* Uten tillatelse vises kartet der serveren startet det. */ },
            { enableHighAccuracy: false, timeout: 8000, maximumAge: 300000 });
    }
    finnBruker(!harBestemtSted);
    const LocateControl = L.Control.extend({
        options: { position: 'bottomright' },
        onAdd: function () {
            const knapp = L.DomUtil.create('button', 'leaflet-bar map-expand-control map-locate-control');
            knapp.type = 'button';
            knapp.title = 'Vis min posisjon';
            knapp.setAttribute('aria-label', 'Vis min posisjon');
            knapp.innerHTML = '&#x2316;';
            L.DomEvent.disableClickPropagation(knapp);
            L.DomEvent.on(knapp, 'click', function () {
                if (brukerPosisjon) kart.setView(brukerPosisjon, Math.max(kart.getZoom(), 13));
                else finnBruker(true);
            });
            return knapp;
        }
    });
    kart.addControl(new LocateControl());
    const ExpandControl = L.Control.extend({
        options: { position: 'bottomright' },
        onAdd: function () {
            const button = L.DomUtil.create('button', 'leaflet-bar map-expand-control');
            button.type = 'button';
            button.title = 'Utvid kart';
            button.setAttribute('aria-label', 'Utvid kart');
            button.innerHTML = '&#x26F6;';
            L.DomEvent.disableClickPropagation(button);
            L.DomEvent.on(button, 'click', function () {
                const region = document.getElementById('mapRegion');
                if (document.fullscreenElement) document.exitFullscreen();
                else if (region.requestFullscreen) region.requestFullscreen();
            });
            document.addEventListener('fullscreenchange', function () {
                const expanded = document.fullscreenElement === document.getElementById('mapRegion');
                button.classList.toggle('is-expanded', expanded);
                button.title = expanded ? 'Lukk fullskjermkart' : 'Utvid kart';
                button.setAttribute('aria-label', button.title);
                window.setTimeout(function () { kart.invalidateSize(); }, 150);
            });
            return button;
        }
    });
    kart.addControl(new ExpandControl());

    function closeWhenBackdropClicked(dialog) {
        if (!dialog) return;
        dialog.addEventListener('click', function (event) {
            if (event.target === dialog) dialog.close();
        });
    }

    // Alle symboler har samme farge: et hvitt symbol på en mørk blå sirkel. Status vises i det lille merket,
    // og akutte behov har en rød ramme som blinker.
    const harGruppering = typeof L.markerClusterGroup === 'function';
    const gruppe = harGruppering
        ? L.markerClusterGroup({
            maxClusterRadius: 48,
            showCoverageOnHover: false,
            spiderfyOnMaxZoom: true,
            iconCreateFunction: function (klynge) {
                const akutt = klynge.getAllChildMarkers().some(function (mark) { return mark.options.akutt; });
                return L.divIcon({
                    className: 'map-cluster' + (akutt ? ' is-urgent' : ''),
                    html: '<span>' + klynge.getChildCount() + '</span>',
                    iconSize: [44, 44]
                });
            }
        }).addTo(kart)
        : kart;

    const panel = document.getElementById('mapPanel');
    const panelBody = document.getElementById('mapPanelBody');
    const markorPerRessurs = {};
    let valgtMarkor = null;
    let forrigeVisning = null;
    let forrigeVisning = null;

    function avstandTekst(lat, lng) {
        const km = brukerPosisjon.distanceTo(L.latLng(lat, lng)) / 1000;
        return (km < 1 ? Math.round(km * 1000) + ' m' : km.toFixed(km < 10 ? 1 : 0).replace('.', ',') + ' km') + ' fra deg i luftlinje';
    }
    function oppdaterAvstand() {
        const element = panelBody && panelBody.querySelector('.mp-distance');
        if (!element || !brukerPosisjon || !valgtMarkor) return;
        const ll = valgtMarkor.getLatLng();
        element.textContent = avstandTekst(ll.lat, ll.lng);
        element.hidden = false;
    }
    function velgKort(id) {
        document.querySelectorAll('.rail-card').forEach(function (kort) {
            const aktiv = id != null && kort.dataset.res === String(id);
            kort.classList.toggle('is-selected', aktiv);
            if (aktiv) kort.scrollIntoView({ block: 'nearest' });
        });
    }
    function lukkPanel() {
        if (!panel) return;
        forrigeVisning = null;
        panel.hidden = true;
        valgtMarkor = null;
        velgKort(null);
    }
    // Detaljpanelet fylles med ferdig HTML fra serveren (all tekst er kodet der). Her kobles bare fanene,
    // delingsknappen og avstanden til brukeren.
    function visPanel(m, markor) {
        valgtMarkor = markor;
        panelBody.innerHTML = m.popup;
        panel.hidden = false;
        panel.style.animation = 'none';
        void panel.offsetWidth;
        panel.style.animation = '';
        velgKort(m.ressursId);
        const faner = panelBody.querySelectorAll('.mp-tabs button');
        faner.forEach(function (knapp) {
            knapp.addEventListener('click', function () {
                faner.forEach(function (k) { k.setAttribute('aria-selected', k === knapp ? 'true' : 'false'); });
                panelBody.querySelectorAll('.mp-pane').forEach(function (p) { p.hidden = p.dataset.pane !== knapp.dataset.tab; });
            });
        });
        const del = panelBody.querySelector('[data-share]');
        if (del) {
            del.addEventListener('click', function (event) {
                event.preventDefault();
                const adresse = new URL(del.getAttribute('href'), window.location.href).href;
                const tekst = del.querySelector('span');
                const ferdig = function (melding) {
                    tekst.textContent = melding;
                    window.setTimeout(function () { tekst.textContent = 'Del lenke'; }, 1800);
                };
                if (navigator.share) navigator.share({ title: document.title, url: adresse }).catch(function () { });
                else if (navigator.clipboard) navigator.clipboard.writeText(adresse).then(function () { ferdig('Lenke kopiert ✓'); }, function () { ferdig('Kunne ikke kopiere'); });
                else ferdig('Kunne ikke kopiere');
            });
        }
        oppdaterAvstand();
        // Flytter kartet slik at markøren ikke havner under panelet.
        const pt = kart.latLngToContainerPoint(markor.getLatLng());
        const venstreKant = panel.getBoundingClientRect().left - kart.getContainer().getBoundingClientRect().left;
        if (window.innerWidth > 860 && pt.x > venstreKant - 60) kart.panBy([pt.x - (venstreKant - 150), 0]);
    }
    if (panel) {
        document.getElementById('mapPanelClose').addEventListener('click', lukkPanel);
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') lukkPanel(); });
        kart.on('click', lukkPanel);
    }
    const workspace = document.getElementById('mapWorkspace');
    const railToggle = document.getElementById('railToggle');
    const railReopen = document.getElementById('railReopen');
    function settRail(skjult) {
        if (!workspace) return;
        workspace.classList.toggle('rail-collapsed', skjult);
        railToggle.setAttribute('aria-expanded', skjult ? 'false' : 'true');
        window.setTimeout(function () { kart.invalidateSize(); }, 300);
    }
    if (railToggle) railToggle.addEventListener('click', function () { settRail(true); });
    if (railReopen) railReopen.addEventListener('click', function () { settRail(false); });
    document.querySelectorAll('.rail-card').forEach(function (kort) {
        kort.addEventListener('click', function () {
            const markor = markorPerRessurs[kort.dataset.res];
            if (!markor) return;
            if (kort.classList.contains('is-selected')) {
                const tilbake = forrigeVisning;
                lukkPanel();
                if (tilbake) kart.setView(tilbake.senter, tilbake.zoom);
                return;
            }
            if (!forrigeVisning) forrigeVisning = { senter: kart.getCenter(), zoom: kart.getZoom() };
            const vis = function () { kart.setView(markor.getLatLng(), Math.max(kart.getZoom(), 14)); visPanel(markor.kartdata, markor); };
            if (harGruppering) gruppe.zoomToShowLayer(markor, vis); else vis();
            if (window.innerWidth <= 860) settRail(true);
        });
    });

    // Én markør per ressurs. Klikk åpner detaljpanelet (eller en vanlig popup på sider uten panel).
    data.markorer.forEach(function (m) {
        const klasser = 'map-pin' + (m.akutt ? ' is-urgent' : '') +
            (m.leveranseStatus ? ' delivery-' + m.leveranseStatus.toLowerCase().replace(' ', '-') : '') +
            (!m.akutt && !m.kanForespørres ? ' is-unavailable' : '');
        const merke = m.akutt ? '!' : (m.leveranseStatus || (m.kanForespørres ? '✓' : '−'));
        const symbol = m.hendelse ? L.divIcon({
            className: 'map-pin-wrapper',
            html: '<span class="map-incident" title="Hendelse"><svg viewBox="0 0 24 24" width="30" height="30" aria-hidden="true"><path d="M12 2.5 22.5 20.5h-21z" fill="#d92d20" stroke="#fff" stroke-width="1.6" stroke-linejoin="round"/><path d="M12 9v5.5M12 17.2v.1" stroke="#fff" stroke-width="2.2" stroke-linecap="round"/></svg></span>',
            iconSize: [40, 40], iconAnchor: [20, 20], popupAnchor: [0, -20]
        }) : L.divIcon({
            className: 'map-pin-wrapper',
            html: '<span class="' + klasser + '"><i style="--pin-icon:url(\'' + m.ikon + '\')"></i><b>' + merke + '</b></span>',
            iconSize: [48, 48], iconAnchor: [24, 24], popupAnchor: [0, -22]
        });
        const markor = L.marker([m.lat, m.lng], { icon: symbol, title: m.tittel, alt: m.tittel, akutt: !!m.akutt, zIndexOffset: m.hendelse ? 1500 : (m.akutt ? 1000 : 0) });
        markor.kartdata = m;
        if (panel) markor.on('click', function () { visPanel(m, markor); });
        else markor.bindPopup(m.popup, { className: 'map-popup', minWidth: 250, maxWidth: 290 });
        if (m.ressursId != null) markorPerRessurs[m.ressursId] = markor;
        gruppe.addLayer(markor);
        if (m.apen) {
            const vis = function () { if (panel) visPanel(m, markor); else markor.openPopup(); };
            if (harGruppering) gruppe.zoomToShowLayer(markor, vis); else vis();
        }
    });
    // Når et skjema er åpent: et klikk i kartet fyller ut de skjulte feltene for breddegrad og lengdegrad.
    // Stedsnavnet finner serveren når skjemaet lagres.
    const p = data.plassering;
    if (!p) return;
    const latFelt = document.getElementById(p.latFelt);
    const lngFelt = document.getElementById(p.lngFelt);
    const dialog = document.getElementById('resourceRegistration');
    const form = document.getElementById('resourceRegistrationForm');
    const choosePlacement = document.getElementById('choosePlacement');
    const confirmation = document.getElementById('placementConfirmation');
    const confirmationLabel = document.getElementById('placementLabel');
    const confirmPlacement = document.getElementById('confirmPlacement');
    const cancelPlacement = document.getElementById('cancelPlacement');
    const needPlace = document.getElementById('needPlace');

    // «Meld behov» bruker det samme kartdatasettet som ressursregistrering, men har ingen dialog eller
    // bekreftelseslinje. Her skal hvert klikk velge punktet direkte.
    if (!form) {
        let needMarker = p.lat != null ? L.marker([p.lat, p.lng]).addTo(kart) : null;
        kart.getContainer().classList.add('placement-active');
        const settPunkt = function (latlng) {
            if (needMarker) needMarker.setLatLng(latlng);
            else needMarker = L.marker(latlng).addTo(kart);
            latFelt.value = latlng.lat.toFixed(6);
            lngFelt.value = latlng.lng.toFixed(6);
            if (needPlace) needPlace.textContent = 'Valgt punkt: ' + latlng.lat.toFixed(5) + ', ' + latlng.lng.toFixed(5);
            document.dispatchEvent(new CustomEvent('kart:plassering', { detail: { lat: latlng.lat, lng: latlng.lng } }));
        };
        window.kartSettPunkt = settPunkt;
        kart.on('click', function (e) { settPunkt(e.latlng); });
        return;
    }

    let valgt = p.lat != null ? L.marker([p.lat, p.lng]).addTo(kart) : null;
    let placementConfirmed = p.lat != null;
    let placementEnabled = p.lat != null;

    kart.getContainer().classList.add('placement-active');
    closeWhenBackdropClicked(dialog);
    if (dialog) dialog.showModal();
    function showConfirmation(latlng) {
        confirmation.hidden = false;
        confirmationLabel.textContent = 'Valgt punkt: ' + latlng.lat.toFixed(5) + ', ' + latlng.lng.toFixed(5);
        const set = function (id, v) { const e = document.getElementById(id); if (e) e.textContent = v; };
        const nm = document.getElementById('resourceName'), ty = document.getElementById('resourceType');
        set('pcName', nm && nm.value.trim() ? nm.value.trim() : 'Ny ressurs');
        const tv = ty && ty.value ? ty.value : 'Ressurs';
        set('pcType', tv); set('pcType2', tv);
        set('pcLat', latlng.lat.toFixed(5)); set('pcLng', latlng.lng.toFixed(5));
    }

    if (p.lat != null) showConfirmation({ lat: p.lat, lng: p.lng });

    if (choosePlacement) {
        choosePlacement.addEventListener('click', function () {
            if (!form.reportValidity()) return;
            dialog.close();
            placementEnabled = true;
            kart.invalidateSize();
            kart.getContainer().focus();
        });
    }

    kart.on('click', function (e) {
        if (!placementEnabled) return;
        if (valgt) valgt.setLatLng(e.latlng);
        else valgt = L.marker(e.latlng).addTo(kart);
        latFelt.value = e.latlng.lat.toFixed(6);
        lngFelt.value = e.latlng.lng.toFixed(6);
        placementConfirmed = false;
        showConfirmation(e.latlng);
    });

    confirmPlacement.addEventListener('click', function () {
        placementConfirmed = true;
        confirmation.hidden = true;
        form.requestSubmit();
    });

    const pcClose = document.getElementById('pcClose');
    if (pcClose) pcClose.addEventListener('click', function () { cancelPlacement.click(); });
    cancelPlacement.addEventListener('click', function () {
        placementConfirmed = false;
        placementEnabled = false;
        confirmation.hidden = true;
        if (valgt) {
            kart.removeLayer(valgt);
            valgt = null;
        }
        latFelt.value = '';
        lngFelt.value = '';
        dialog.showModal();
    });

    form.addEventListener('submit', function (event) {
        if (!placementConfirmed) {
            event.preventDefault();
            if (placementEnabled) confirmation.hidden = false;
        }
    });
})();
