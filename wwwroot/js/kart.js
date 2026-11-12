// Tegner kartet med Leaflet. Leaflet er et JavaScript-bibliotek, så litt JavaScript trengs for kartet.
// Alt innholdet (hvor kartet starter, markører, symboler og tekst) lages på serveren i C#
// (KartVisning) og ligger som JSON i <script id="kartdata">. Dette skriptet bare tegner det.
(function () {
    const data = JSON.parse(document.getElementById('kartdata').textContent);
    // Kartet er begrenset til Norge: Kartverkets gråtonekart som bakgrunn, og alt utenfor landegrensen dempes.
    const norgeGrenser = L.latLngBounds([57.5, 3.0], [71.6, 31.5]);
    const kart = L.map('map', { maxBounds: norgeGrenser, maxBoundsViscosity: 1, minZoom: 4 }).setView([data.lat, data.lng], data.zoom);

    L.tileLayer('https://cache.kartverket.no/v1/wmts/1.0.0/topograatone/default/webmercator/{z}/{y}/{x}.png', {
        minZoom: 4,
        maxZoom: 18,
        bounds: norgeGrenser,
        attribution: '&copy; <a href="https://www.kartverket.no/">Kartverket</a>'
    }).addTo(kart);

    if (data.omrissUrl && typeof fetch === 'function') {
        fetch(data.omrissUrl).then(function (svar) { return svar.json(); }).then(function (ringer) {
            const verden = [[85, -180], [85, 180], [-85, 180], [-85, -180]];
            L.polygon([verden].concat(ringer), { stroke: false, fillColor: '#0b2f3a', fillOpacity: .38, interactive: false }).addTo(kart);
            L.polygon(ringer, { color: '#087181', weight: 2, fill: false, interactive: false }).addTo(kart);
        }).catch(function () { /* Kartet fungerer uten omriss. */ });
    }
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

    function popupHandlinger(m) {
        const rute = 'https://www.google.com/maps/dir/?api=1&destination=' + encodeURIComponent(m.lat + ',' + m.lng) + '&travelmode=driving';
        return '<div class="map-popup-actions">' +
            '<a class="map-popup-btn is-primary" href="' + rute + '" target="_blank" rel="noopener noreferrer">Veibeskrivelse fra min posisjon</a>' +
            '<button type="button" class="map-popup-btn" data-popup-action="zoom">Zoom hit</button>' +
            '<button type="button" class="map-popup-btn" data-popup-action="copy">Kopier koordinater</button></div>';
    }

    kart.on('popupopen', function (e) {
        const element = e.popup.getElement && e.popup.getElement();
        if (!element) return;
        const posisjon = e.popup.getLatLng();
        element.querySelectorAll('[data-popup-action]').forEach(function (knapp) {
            knapp.addEventListener('click', function () {
                if (knapp.dataset.popupAction === 'zoom') {
                    kart.setView(posisjon, Math.max(kart.getZoom(), 15));
                    return;
                }
                const tekst = posisjon.lat.toFixed(5) + ', ' + posisjon.lng.toFixed(5);
                const ferdig = function (melding) {
                    knapp.textContent = melding;
                    window.setTimeout(function () { knapp.textContent = 'Kopier koordinater'; }, 1800);
                };
                if (navigator.clipboard) navigator.clipboard.writeText(tekst).then(function () { ferdig('Kopiert ✓'); }, function () { ferdig(tekst); });
                else ferdig(tekst);
            });
        });
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
            .bindPopup(m.popup + popupHandlinger(m), { className: 'map-popup', minWidth: 270, maxWidth: 340 });
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
