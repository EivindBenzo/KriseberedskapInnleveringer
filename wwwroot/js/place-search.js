(function () {
    'use strict';

    function normalize(value) {
        return value.toLocaleLowerCase('nb').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
            .replace(/æ/g, 'ae').replace(/ø/g, 'o');
    }

    function validPlace(place) {
        return place && typeof place.label === 'string' && place.label.length > 0 && place.label.length <= 220
            && !/[\u0000-\u001f\u007f]/.test(place.label)
            && Number.isFinite(place.lat) && Math.abs(place.lat) <= 90
            && Number.isFinite(place.lng) && Math.abs(place.lng) <= 180;
    }

    function attach({ input, list, status, button, searchUrl, localitiesUrl, onSelect, onInput }) {
        let version = 0;
        let timer;
        let controller;
        let localities;
        let loadingLocalities;
        let results = [];
        let active = -1;

        function close() {
            results = [];
            active = -1;
            list.replaceChildren();
            list.classList.remove('open');
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
        }

        function cancel() {
            version++;
            clearTimeout(timer);
            if (controller) controller.abort();
            input.setAttribute('aria-busy', 'false');
            close();
        }

        function select(index) {
            const place = results[index];
            if (!place) return;
            cancel();
            input.value = place.label;
            status.textContent = 'Valgt: ' + place.label;
            onSelect(place);
            input.focus();
        }

        function highlight(index) {
            active = index;
            Array.from(list.children).forEach((item, i) => {
                item.firstChild.setAttribute('aria-selected', String(i === active));
            });
            const option = list.children[active]?.firstChild;
            if (option) {
                input.setAttribute('aria-activedescendant', option.id);
                option.scrollIntoView({ block: 'nearest' });
            }
        }

        function render(data, local) {
            close();
            results = Array.isArray(data) ? data.filter(validPlace).slice(0, 6) : [];
            results.forEach((place, index) => {
                const item = document.createElement('li');
                item.setAttribute('role', 'presentation');
                const option = document.createElement('button');
                option.type = 'button';
                option.id = list.id + '-option-' + index;
                option.className = 'city-suggestion-item';
                option.tabIndex = -1;
                option.setAttribute('role', 'option');
                option.setAttribute('aria-selected', 'false');
                option.textContent = place.label;
                option.addEventListener('mousedown', event => event.preventDefault());
                option.addEventListener('click', event => {
                    event.stopPropagation();
                    select(index);
                });
                item.appendChild(option);
                list.appendChild(item);
            });
            if (results.length) {
                list.classList.add('open');
                input.setAttribute('aria-expanded', 'true');
            }
            status.textContent = results.length
                ? results.length + ' treff' + (local ? ' i lokal stedsoversikt.' : '.')
                : 'Ingen treff' + (local ? ' lokalt. Trykk Søk for adresser og flere steder.' : '.');
        }

        async function loadLocalities() {
            if (localities) return localities;
            if (!loadingLocalities) {
                const dataController = new AbortController();
                const timeout = setTimeout(() => dataController.abort(), 6000);
                loadingLocalities = fetch(localitiesUrl, { signal: dataController.signal })
                    .then(response => {
                        if (!response.ok) throw new Error('Localities unavailable');
                        return response.json();
                    })
                    .then(data => {
                        if (!Array.isArray(data) || data.length > 2000 || !data.every(validPlace)) {
                            throw new Error('Invalid localities');
                        }
                        localities = data.map(place => ({ ...place, key: normalize(place.label) }));
                        return localities;
                    })
                    .finally(() => {
                        clearTimeout(timeout);
                        loadingLocalities = null;
                    });
            }
            return loadingLocalities;
        }

        function queryValue() {
            const query = input.value.trim();
            return query.length >= 2 && query.length <= 120 && !/[\u0000-\u001f\u007f]/.test(query) ? query : null;
        }

        async function suggest(query, requestVersion) {
            input.setAttribute('aria-busy', 'true');
            status.textContent = 'Henter lokale stedsforslag…';
            try {
                const data = await loadLocalities();
                if (requestVersion !== version) return;
                const key = normalize(query);
                const matches = data.filter(place => place.key.startsWith(key));
                const remaining = data.filter(place => !place.key.startsWith(key) && place.key.includes(key));
                render(matches.concat(remaining), true);
            } catch {
                if (requestVersion === version) status.textContent = 'Kunne ikke hente lokale forslag. Prøv Søk eller velg i kartet.';
            } finally {
                if (requestVersion === version) input.setAttribute('aria-busy', 'false');
            }
        }

        async function search() {
            cancel();
            const query = queryValue();
            if (!query) {
                status.textContent = 'Skriv mellom 2 og 120 tegn.';
                return;
            }
            const requestVersion = version;
            controller = new AbortController();
            const requestController = controller;
            const timeout = setTimeout(() => requestController.abort(), 6000);
            input.setAttribute('aria-busy', 'true');
            status.textContent = 'Søker hos OpenStreetMap…';
            try {
                const response = await fetch(searchUrl + '?query=' + encodeURIComponent(query),
                    { signal: requestController.signal });
                if (!response.ok) throw new Error('Search unavailable');
                const data = await response.json();
                if (requestVersion === version && !requestController.signal.aborted) render(data, false);
            } catch {
                if (requestVersion === version) status.textContent = 'Kunne ikke hente steder. Velg et punkt i kartet eller prøv igjen.';
            } finally {
                clearTimeout(timeout);
                if (requestVersion === version) input.setAttribute('aria-busy', 'false');
            }
        }

        input.addEventListener('input', function () {
            cancel();
            onInput?.();
            const query = queryValue();
            status.textContent = query ? '' : 'Skriv mellom 2 og 120 tegn for lokale forslag.';
            if (!query) return;
            const requestVersion = version;
            timer = setTimeout(() => suggest(query, requestVersion), 250);
        });
        input.addEventListener('keydown', function (event) {
            if (event.isComposing) {
                if (event.key === 'Enter') event.preventDefault();
                return;
            }
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                if (results.length) {
                    const next = active < 0 ? (event.key === 'ArrowDown' ? 0 : results.length - 1)
                        : (active + (event.key === 'ArrowDown' ? 1 : -1) + results.length) % results.length;
                    highlight(next);
                }
            } else if (event.key === 'Enter') {
                event.preventDefault();
                if (active >= 0) select(active);
                else if (results.length) select(0);
                else search();
            } else if (event.key === 'Escape' || event.key === 'Tab') {
                if (event.key === 'Escape') event.preventDefault();
                cancel();
                status.textContent = '';
            }
        });
        button.addEventListener('click', search);
        document.addEventListener('click', function (event) {
            if (!list.contains(event.target) && event.target !== input && event.target !== button) {
                cancel();
                status.textContent = '';
            }
        });
        return Object.freeze({
            setValue(label) {
                cancel();
                input.value = label;
                status.textContent = '';
            }
        });
    }

    window.PlaceSearch = Object.freeze({ attach });
}());
