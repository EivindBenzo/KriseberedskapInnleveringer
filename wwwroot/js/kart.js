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
                const container = kart.getContainer();
                container.classList.toggle('map-expanded');
                button.classList.toggle('is-expanded', container.classList.contains('map-expanded'));
                button.title = container.classList.contains('map-expanded') ? 'Lukk utvidet kart' : 'Utvid kart';
                button.setAttribute('aria-label', button.title);
                window.setTimeout(function () { kart.invalidateSize(); }, 150);
            });
            return button;
        }
    });
    kart.addControl(new ExpandControl());

    // Én markør per ressurs. Popup-teksten er ferdig HTML fra serveren, der teksten allerede er kodet.
    data.markorer.forEach(function (m) {
        const symbol = L.icon({ iconUrl: m.ikon, iconSize: [32, 32], iconAnchor: [16, 16], popupAnchor: [0, -14] });
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
    let valgt = p.lat != null ? L.marker([p.lat, p.lng]).addTo(kart) : null;
    let placementConfirmed = p.lat != null;

    kart.getContainer().classList.add('placement-active');
    function showConfirmation(latlng) {
        confirmation.hidden = false;
        confirmationLabel.textContent = 'Valgt punkt: ' + latlng.lat.toFixed(5) + ', ' + latlng.lng.toFixed(5);
    }

    if (p.lat != null) showConfirmation({ lat: p.lat, lng: p.lng });

    if (choosePlacement) {
        choosePlacement.addEventListener('click', function () {
            if (!form.reportValidity()) return;
            dialog.close();
            kart.invalidateSize();
            kart.getContainer().focus();
        });
    }

    kart.on('click', function (e) {
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
            confirmation.hidden = false;
        }
    });
})();
