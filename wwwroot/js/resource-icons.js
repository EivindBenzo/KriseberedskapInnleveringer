(function () {
    'use strict';
    const base = document.currentScript.dataset.iconBase;
    const versionedUrls = JSON.parse(document.currentScript.dataset.iconUrls || '{}');
    // Bump with glyph changes; the versioned script refreshes every consumer's asset URLs.
    const assetVersion = 'solid-20261025';
    const icons = new Map([
        ['Kjøretøy', 'car'],
        ['Maskin', 'excavator'],
        ['Drone', 'drone'],
        ['Generator', 'generator'],
        ['Personell', 'personnel'],
        ['Utstyr', 'toolbox']
    ]);

    const entries = Object.freeze(Array.from(icons, ([type, file]) => Object.freeze({ type, file })));

    function get(type) {
        const known = icons.has(type);
        const file = known ? icons.get(type) : 'unknown';
        return {
            label: known ? type : 'Ukjent / ikke valgt ressurstype',
            url: versionedUrls[file] || base + file + '.svg?v=' + assetVersion
        };
    }

    function sizeForZoom(zoom = 12) {
        const level = Number.isFinite(zoom) ? zoom : 12;
        return Math.max(28, Math.min(36, 32 + (level - 12)));
    }

    function markerOptions(type, preview = false, zoom = 12) {
        const resource = get(type);
        const size = sizeForZoom(zoom);
        return {
            icon: L.icon({
                iconUrl: resource.url,
                iconSize: [size, size],
                iconAnchor: [size / 2, size / 2],
                popupAnchor: [0, -size / 2],
                className: preview ? 'resource-marker resource-marker-preview' : 'resource-marker'
            }),
            title: (preview ? 'Ny ressurs: ' : '') + resource.label,
            alt: resource.label
        };
    }

    window.ResourceIcons = Object.freeze({ entries, get, sizeForZoom, markerOptions });
    window.resourceMarkerOptions = markerOptions;
    window.resourceIconUrl = type => get(type).url;

    function initializeImages() {
        document.querySelectorAll('img[data-resource-icon]').forEach(image => {
            const resource = get(image.dataset.resourceIcon);
            image.src = resource.url;
            if (!image.hasAttribute('alt')) image.alt = resource.label;
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializeImages, { once: true });
    } else {
        initializeImages();
    }
}());
