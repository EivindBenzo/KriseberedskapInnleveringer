(function () {
    'use strict';
    const base = document.currentScript.dataset.iconBase;
    const versionedUrls = JSON.parse(document.currentScript.dataset.iconUrls || '{}');
    // Bump with glyph changes; the versioned script refreshes every consumer's asset URLs.
    const assetVersion = 'availability-20261026';
    const icons = new Map([
        ['Kjøretøy', 'car'],
        ['Maskin', 'excavator'],
        ['Drone', 'drone'],
        ['Generator', 'generator'],
        ['Personell', 'personnel'],
        ['Utstyr', 'toolbox']
    ]);

    const entries = Object.freeze(Array.from(icons, ([type, file]) => Object.freeze({ type, file })));

    function stateFor(type, status, preview = false, now = Date.now()) {
        if (preview || !status) return 'neutral';
        const verified = Date.parse(status.verifiedAtUtc);
        const expires = Date.parse(status.verificationExpiresAtUtc);
        return icons.has(type) && status.isEligible === true &&
            status.availability === 'Tilgjengelig' && status.verification === 'Godkjent' &&
            Number.isFinite(verified) && verified <= now && Number.isFinite(expires) && expires > now
            ? 'available' : 'unavailable';
    }

    function statusText(status, now = Date.now()) {
        if (!status) return '';
        if (status.isEligible === true && Date.parse(status.verificationExpiresAtUtc) <= now)
            return 'Kan ikke forespørres nå · Tilgjengelig · Godkjenning utløpt';
        return status.statusText || 'Kan ikke forespørres nå · Ukjent status';
    }

    function get(type, status = null, preview = false) {
        const known = icons.has(type);
        const file = known ? icons.get(type) : 'unknown';
        const state = stateFor(type, status, preview);
        const asset = state === 'neutral' ? file : file + '-' + state;
        return {
            label: known ? type : 'Ukjent / ikke valgt ressurstype',
            state,
            url: versionedUrls[asset] || base + asset + '.svg?v=' + assetVersion
        };
    }

    function sizeForZoom(zoom = 12) {
        const level = Number.isFinite(zoom) ? zoom : 12;
        return Math.max(28, Math.min(36, 32 + (level - 12)));
    }

    function markerOptions(type, preview = false, zoom = 12, status = null) {
        if (!preview && !status) status = {};
        const resource = get(type, status, preview);
        const size = sizeForZoom(zoom);
        const description = (preview ? 'Ny ressurs (ikke lagret): ' : '') + resource.label +
            (!preview && status ? ' · ' + statusText(status) : '');
        return {
            icon: L.icon({
                iconUrl: resource.url,
                iconSize: [size, size],
                iconAnchor: [size / 2, size / 2],
                popupAnchor: [0, -size / 2],
                className: 'resource-marker resource-state-' + resource.state + (preview ? ' resource-marker-preview' : '')
            }),
            title: description,
            alt: description
        };
    }

    function watchAvailability(callback, statuses) {
        if (typeof window.setTimeout !== 'function') return;
        const expiries = statuses.filter(status => status && status.isEligible === true)
            .map(status => Date.parse(status.verificationExpiresAtUtc)).filter(Number.isFinite);
        function schedule() {
            const now = Date.now();
            const next = Math.min(...expiries.filter(expiry => expiry > now));
            if (Number.isFinite(next))
                window.setTimeout(() => { callback(); schedule(); }, Math.min(next - now + 1, 2147483647));
        }
        schedule();
    }

    window.ResourceIcons = Object.freeze({ entries, get, stateFor, statusText, sizeForZoom, markerOptions, watchAvailability });
    window.resourceMarkerOptions = markerOptions;
    window.resourceIconUrl = (type, status) => get(type, status).url;

    function initializeImages() {
        const images = Array.from(document.querySelectorAll('img[data-resource-icon]'));
        const automaticAlt = images.map(image => !image.hasAttribute('alt'));
        const statuses = images.map(image => {
            try { return JSON.parse(image.dataset.resourceStatus || '{}') || {}; }
            catch { return {}; }
        });
        function refresh() {
            images.forEach((image, index) => {
                const status = statuses[index];
                const resource = get(image.dataset.resourceIcon, status);
                image.src = resource.url;
                image.dataset.resourceState = resource.state;
                image.title = status ? statusText(status) : resource.label;
                if (automaticAlt[index]) image.alt = resource.label + (status ? ' · ' + statusText(status) : '');
                const row = image.closest?.('.resource-row, .resource-card');
                const text = row?.querySelector('[data-resource-status-text]');
                if (text && status) text.textContent = statusText(status);
            });
        }
        refresh();
        watchAvailability(refresh, statuses);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializeImages, { once: true });
    } else {
        initializeImages();
    }
}());
