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
    const tekst = document.getElementById(p.tekstFelt);
    let valgt = p.lat != null ? L.marker([p.lat, p.lng]).addTo(kart) : null;

    kart.getContainer().classList.add('placement-active');
    kart.on('click', function (e) {
        if (valgt) valgt.setLatLng(e.latlng);
        else valgt = L.marker(e.latlng).addTo(kart);
        latFelt.value = e.latlng.lat.toFixed(6);
        lngFelt.value = e.latlng.lng.toFixed(6);
        tekst.textContent = e.latlng.lat.toFixed(5) + ', ' + e.latlng.lng.toFixed(5) + ' (stedsnavnet hentes når du lagrer)';
    });
})();
