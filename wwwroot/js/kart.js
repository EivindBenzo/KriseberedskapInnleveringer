// Tegner kartet med Leaflet. Leaflet er et JavaScript-bibliotek, så litt JavaScript trengs for kartet.
// Alt innholdet (hvor kartet starter, markører, symboler og tekst) lages på serveren i C#
// (KartVisning) og ligger som JSON i <script id="kartdata">. Dette skriptet bare tegner det.
(function () {
    const data = JSON.parse(document.getElementById('kartdata').textContent);
    const kart = L.map('map').setView([data.lat, data.lng], data.zoom);

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(kart);

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
    // Én markør per ressurs. Popup-teksten er ferdig HTML fra serveren, der teksten allerede er kodet.
    data.markorer.forEach(function (m) {
        const symbol = m.akutt
            ? L.divIcon({ className: 'urgent-map-marker', html: '<img src="' + m.ikon + '" alt=""><span aria-hidden="true">!</span>', iconSize: [44, 44], iconAnchor: [22, 22], popupAnchor: [0, -22] })
            : L.divIcon({
                className: 'resource-map-marker ' + (m.kanForespørres ? 'is-available' : 'is-unavailable') +
                    (m.leveranseStatus ? ' delivery-' + m.leveranseStatus.toLowerCase().replace(' ', '-') : ''),
                html: '<img src="' + m.ikon + '" alt=""><span>' + (m.leveranseStatus || (m.kanForespørres ? '✓' : '−')) + '</span>',
                iconSize: [52, 52], iconAnchor: [26, 26], popupAnchor: [0, -24]
            });
        const markor = L.marker([m.lat, m.lng], { icon: symbol, title: m.tittel, alt: m.tittel })
            .addTo(kart)
            .bindPopup(m.popup);
        if (m.apen) markor.openPopup();
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
