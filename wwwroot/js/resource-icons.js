(function () {
    'use strict';
    const base = document.currentScript.dataset.iconBase;
    const icons = new Map([
        ['Kjøretøy', 'car'],
        ['Maskin', 'excavator'],
        ['Drone', 'drone'],
        ['Generator', 'generator'],
        ['Personell', 'personnel'],
        ['Utstyr', 'toolbox']
    ]);

    window.resourceMarkerOptions = function (type, preview = false) {
        const known = icons.has(type);
        const label = known ? type : 'Ukjent / ikke valgt ressurstype';
        return {
            icon: L.icon({
                iconUrl: base + (known ? icons.get(type) : 'unknown') + '.svg',
                iconSize: [48, 48],
                iconAnchor: [24, 24],
                popupAnchor: [0, -24],
                className: preview ? 'resource-marker resource-marker-preview' : 'resource-marker'
            }),
            title: (preview ? 'Ny ressurs: ' : '') + label,
            alt: label
        };
    };
}());
