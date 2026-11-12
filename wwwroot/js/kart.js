// Tegner kartet med Leaflet. Leaflet er et JavaScript-bibliotek, så litt JavaScript trengs for kartet.
// Alt innholdet (hvor kartet starter, markører, symboler og tekst) lages på serveren i C#
// (KartVisning) og ligger som JSON i <script id="kartdata">. Dette skriptet bare tegner det.
(function () {
    const data = JSON.parse(document.getElementById('kartdata').textContent);
    // Kartet kan flyttes fritt. Kartverkets topografiske kart er svært detaljert i Norge, og et vanlig gatekart
    // ligger under, slik at man også ser noe når man zoomer ut eller flytter seg utenfor Kartverkets dekning.
    const kart = L.map('map', { minZoom: 3, worldCopyJump: true, zoomSnap: 0.5 }).setView([data.lat, data.lng], data.zoom);
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
        document.querySelectorAll('.map-popup-distance').forEach(oppdaterAvstand);
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

    const resourceBrowser = document.getElementById('resourceBrowser');
    const openResourceBrowser = document.getElementById('openResourceBrowser');
    closeWhenBackdropClicked(resourceBrowser);
    if (openResourceBrowser) {
        openResourceBrowser.addEventListener('click', function () {
            resourceBrowser.showModal();
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

    // Under serverens popup kommer en stor rutekapp (Google Maps starter ved brukerens posisjon).
    function popupHandlinger(m) {
        const rute = 'https://www.google.com/maps/dir/?api=1&destination=' + encodeURIComponent(m.lat + ',' + m.lng) + '&travelmode=driving';
        return '<a class="map-popup-route" href="' + rute + '" target="_blank" rel="noopener noreferrer">' +
            '<svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path fill="currentColor" d="M21.7 11.3l-9-9a1 1 0 0 0-1.4 0l-9 9a1 1 0 0 0 0 1.4l9 9a1 1 0 0 0 1.4 0l9-9a1 1 0 0 0 0-1.4zM14 14.5V12h-4v3H8v-4a1 1 0 0 1 1-1h5V7.5l3.5 3.5-3.5 3.5z"/></svg>' +
            'Veibeskrivelse hit</a>';
    }

    function medRute(m) {
        const marker = '<div class="map-popup-links">';
        return m.popup.indexOf(marker) >= 0 ? m.popup.replace(marker, popupHandlinger(m) + marker) : m.popup + popupHandlinger(m);
    }

    function settAvstand(element) {
        if (!brukerPosisjon || !element.dataset.lat) return;
        const km = brukerPosisjon.distanceTo(L.latLng(+element.dataset.lat, +element.dataset.lng)) / 1000;
        element.textContent = (km < 1 ? Math.round(km * 1000) + ' m' : km.toFixed(km < 10 ? 1 : 0).replace('.', ',') + ' km') + ' fra deg i luftlinje';
        element.hidden = false;
    }
    function oppdaterAvstand(element) { settAvstand(element); }

    kart.on('popupopen', function (e) {
        const element = e.popup.getElement && e.popup.getElement();
        if (!element) return;
        const posisjon = e.popup.getLatLng();
        const avstand = element.querySelector('.map-popup-distance');
        if (avstand) {
            avstand.dataset.lat = posisjon.lat;
            avstand.dataset.lng = posisjon.lng;
            settAvstand(avstand);
        }
        const rad = element.querySelector('.map-popup-links');
        const detaljer = rad && rad.querySelector('a:not([href^="tel:"])');
        if (!rad || !detaljer || rad.querySelector('[data-popup-action]')) return;
        // «Del» kopierer lenken til ressursen eller behovet, slik at den kan sendes til andre.
        const del = document.createElement('button');
        del.type = 'button';
        del.className = 'map-popup-btn';
        del.dataset.popupAction = 'share';
        del.textContent = 'Del';
        del.addEventListener('click', function () {
            const adresse = new URL(detaljer.getAttribute('href'), window.location.href).href;
            const ferdig = function (melding) {
                del.textContent = melding;
                window.setTimeout(function () { del.textContent = 'Del'; }, 1800);
            };
            if (navigator.share) navigator.share({ title: document.title, url: adresse }).catch(function () { });
            else if (navigator.clipboard) navigator.clipboard.writeText(adresse).then(function () { ferdig('Lenke kopiert ✓'); }, function () { ferdig('Kunne ikke kopiere'); });
            else ferdig('Kunne ikke kopiere');
        });
        rad.appendChild(del);
    });
    // Én markør per ressurs. Popup-teksten er ferdig HTML fra serveren, der teksten allerede er kodet.
    data.markorer.forEach(function (m) {
        const klasser = 'map-pin' + (m.akutt ? ' is-urgent' : '') +
            (m.leveranseStatus ? ' delivery-' + m.leveranseStatus.toLowerCase().replace(' ', '-') : '') +
            (!m.akutt && !m.kanForespørres ? ' is-unavailable' : '');
        const merke = m.akutt ? '!' : (m.leveranseStatus || (m.kanForespørres ? '✓' : '−'));
        const symbol = L.divIcon({
            className: 'map-pin-wrapper',
            html: '<span class="' + klasser + '"><i style="--pin-icon:url(\'' + m.ikon + '\')"></i><b>' + merke + '</b></span>',
            iconSize: [48, 48], iconAnchor: [24, 24], popupAnchor: [0, -22]
        });
        const markor = L.marker([m.lat, m.lng], { icon: symbol, title: m.tittel, alt: m.tittel, akutt: !!m.akutt, zIndexOffset: m.akutt ? 1000 : 0 })
            .bindPopup(medRute(m), { className: 'map-popup', minWidth: 250, maxWidth: 290 });
        gruppe.addLayer(markor);
        if (m.apen) {
            if (harGruppering) gruppe.zoomToShowLayer(markor, function () { markor.openPopup(); });
            else markor.openPopup();
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
        kart.on('click', function (e) {
            if (needMarker) needMarker.setLatLng(e.latlng);
            else needMarker = L.marker(e.latlng).addTo(kart);
            latFelt.value = e.latlng.lat.toFixed(6);
            lngFelt.value = e.latlng.lng.toFixed(6);
            if (needPlace) needPlace.textContent = 'Valgt punkt: ' + e.latlng.lat.toFixed(5) + ', ' + e.latlng.lng.toFixed(5);
        });
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
