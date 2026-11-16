(() => {
    const collator = new Intl.Collator("nb", { numeric: true, sensitivity: "base" });
    const time = value => Date.parse(value) || 0;
    const ready = fn => document.readyState === "loading" ? document.addEventListener("DOMContentLoaded", fn) : fn();

    function initOwnResources() {
        const grid = document.getElementById("ownResourcesGrid");
        const sort = document.getElementById("ownResourceSort");
        if (!grid || !sort) return;

        const type = document.getElementById("ownResourceType");
        const status = document.getElementById("ownResourceStatus");
        const cards = Array.from(grid.querySelectorAll(":scope > article"));
        const first = (a, b, key, value) => Number(b.dataset[key] === value) - Number(a.dataset[key] === value);

        function apply() {
            cards.forEach(card => {
                card.hidden = !((!type?.value || card.dataset.resourceType === type.value)
                    && (!status?.value || card.dataset.resourceStatus === status.value));
            });

            cards.sort((a, b) => {
                const nameA = a.dataset.sortName || "";
                const nameB = b.dataset.sortName || "";
                switch (sort.value) {
                    case "name-desc": return collator.compare(nameB, nameA);
                    case "newest": return time(b.dataset.sortCreated) - time(a.dataset.sortCreated);
                    case "oldest": return time(a.dataset.sortCreated) - time(b.dataset.sortCreated);
                    case "available": return first(a, b, "resourceStatus", "tilgjengelig") || collator.compare(nameA, nameB);
                    case "assigned": return first(a, b, "resourceStatus", "tildelt") || collator.compare(nameA, nameB);
                    case "unavailable": return first(a, b, "resourceStatus", "utilgjengelig") || collator.compare(nameA, nameB);
                    case "status": return collator.compare(a.dataset.resourceStatus || "", b.dataset.resourceStatus || "") || collator.compare(nameA, nameB);
                    default: return collator.compare(nameA, nameB);
                }
            });
            grid.append(...cards);
        }

        [type, status, sort].forEach(el => el?.addEventListener("change", apply));
        apply();
    }

    function initNeeds(listEl, root) {
        const list = listEl || document.getElementById("ownNeedsList");
        if (!list) return;

        const sort = root ? root.querySelector("[data-pn-sort]") : document.getElementById("needSort");
        const search = root ? root.querySelector("[data-pn-search]") : document.getElementById("needSearch");
        const count = root ? null : document.getElementById("needsFilterCount");
        const tabs = root ? [] : Array.from(document.querySelectorAll("[data-need-tab]"));
        const cards = Array.from(list.querySelectorAll(":scope > .mb-row"));
        let tab = "";

        function apply() {
            const q = (search?.value || "").trim().toLowerCase();
            let visible = 0;
            cards.forEach(card => {
                const show = (!tab || card.dataset.needStatus === tab)
                    && (!q || (card.dataset.needSearch || "").includes(q));
                card.hidden = !show;
                if (show) visible++;
            });

            const d = c => c.dataset;
            cards.sort((a, b) => {
                switch (sort?.value) {
                    case "oldest": return time(d(a).needDate) - time(d(b).needDate);
                    case "urgent": return Number(d(b).needPriority === "akutt") - Number(d(a).needPriority === "akutt") || time(d(b).needDate) - time(d(a).needDate);
                    case "qty-desc": return Number(d(b).needQty) - Number(d(a).needQty);
                    case "qty-asc": return Number(d(a).needQty) - Number(d(b).needQty);
                    case "deadline": return (time(d(a).needDeadline) || Infinity) - (time(d(b).needDeadline) || Infinity);
                    case "name-asc": return collator.compare(d(a).needType || "", d(b).needType || "");
                    case "name-desc": return collator.compare(d(b).needType || "", d(a).needType || "");
                    default: return time(d(b).needDate) - time(d(a).needDate);
                }
            });
            list.append(...cards);
            if (count) count.textContent = visible === 0 ? "Ingen behov matcher valget." : `Viser ${visible} av ${cards.length} behov`;
        }

        tabs.forEach(btn => btn.addEventListener("click", () => {
            tab = btn.dataset.needTab || "";
            tabs.forEach(b => {
                const on = b === btn;
                b.classList.toggle("is-active", on);
                b.setAttribute("aria-selected", on ? "true" : "false");
            });
            apply();
        }));
        search?.addEventListener("input", apply);
        sort?.addEventListener("change", apply);

        list.addEventListener("click", event => {
            const btn = event.target.closest("[data-mb-toggle]");
            if (!btn) return;
            const more = btn.closest(".mb-row")?.querySelector(".mb-more");
            if (!more) return;
            more.hidden = !more.hidden;
            btn.setAttribute("aria-expanded", String(!more.hidden));
            btn.closest(".mb-row").classList.toggle("is-open", !more.hidden);
        });
        apply();
    }

    // Verktøylinje (søk, statusfiltre og sortering) over hver kortliste i oversikten.
    function initGrid(grid) {
        const cards = Array.from(grid.querySelectorAll(":scope > article.dc-card"));
        if (cards.length < 2) return;

        const nameOf = c => c.dataset.sName || c.querySelector(".dc-title strong")?.textContent.trim() || "";
        const labelsOf = c => Array.from(c.querySelectorAll(".dc-chips .status-badge")).map(b => b.textContent.trim()).filter(t => t && !/^(frist|sendt|registrert|\d)/i.test(t) && !/\d{1,2}\.\d{1,2}\.\d{2,4}/.test(t));
        const original = cards.slice();

        const counts = new Map();
        cards.forEach(c => new Set(labelsOf(c)).forEach(l => counts.set(l, (counts.get(l) || 0) + 1)));

        const options = [["", "Standard rekkefølge"], ["name-asc", "Navn A–Å"], ["name-desc", "Navn Å–A"]];
        if (cards.some(c => c.dataset.sDate)) options.push(["newest", "Nyest først"], ["oldest", "Eldst først"]);
        if (cards.some(c => c.dataset.sQty)) options.push(["qty-desc", "Størst antall"], ["qty-asc", "Minst antall"]);
        if (cards.some(c => c.dataset.sDeadline)) options.push(["deadline", "Frist (nærmest)"]);

        const bar = document.createElement("div");
        bar.className = "dc-toolbar";
        bar.innerHTML = `<label class="mb-search"><span aria-hidden="true">🔍</span><input type="search" placeholder="Søk…" aria-label="Søk i listen"></label>`
            + `<select aria-label="Sorter listen">${options.map(([v, t]) => `<option value="${v}">${t}</option>`).join("")}</select>`
            + (counts.size > 0 ? `<div class="dc-filter-chips" role="group" aria-label="Filtrer på status"></div>` : "")
            + `<span class="dc-toolbar-count" role="status" aria-live="polite"></span>`;
        grid.before(bar);

        const input = bar.querySelector("input");
        const select = bar.querySelector("select");
        const chipBox = bar.querySelector(".dc-filter-chips");
        const info = bar.querySelector(".dc-toolbar-count");
        const empty = document.createElement("p");
        empty.className = "empty-state";
        empty.textContent = "Ingen treff.";
        empty.hidden = true;
        grid.after(empty);

        let active = "";
        if (chipBox) {
            const entries = [["", "Alle", cards.length], ...Array.from(counts, ([l, n]) => [l, l, n])];
            chipBox.innerHTML = entries.map(([v, t, n]) => `<button type="button" class="dc-fchip${v === "" ? " is-active" : ""}" data-v="${v}">${t} <span>${n}</span></button>`).join("");
            chipBox.addEventListener("click", e => {
                const b = e.target.closest(".dc-fchip");
                if (!b) return;
                active = b.dataset.v;
                chipBox.querySelectorAll(".dc-fchip").forEach(x => x.classList.toggle("is-active", x === b));
                apply();
            });
        }

        function apply() {
            const q = input.value.trim().toLowerCase();
            let visible = 0;
            cards.forEach(c => {
                const show = (!active || labelsOf(c).includes(active)) && (!q || c.textContent.toLowerCase().includes(q));
                c.hidden = !show;
                if (show) visible++;
            });

            const ds = c => c.dataset;
            const sorted = select.value === "" ? original.slice() : cards.slice().sort((a, b) => {
                switch (select.value) {
                    case "name-desc": return collator.compare(nameOf(b), nameOf(a));
                    case "newest": return time(ds(b).sDate) - time(ds(a).sDate);
                    case "oldest": return time(ds(a).sDate) - time(ds(b).sDate);
                    case "qty-desc": return Number(ds(b).sQty || 0) - Number(ds(a).sQty || 0);
                    case "qty-asc": return Number(ds(a).sQty || 0) - Number(ds(b).sQty || 0);
                    case "deadline": return (time(ds(a).sDeadline) || Infinity) - (time(ds(b).sDeadline) || Infinity);
                    default: return collator.compare(nameOf(a), nameOf(b));
                }
            });
            cards.splice(0, cards.length, ...sorted);
            grid.append(...cards);
            empty.hidden = visible > 0;
            info.textContent = `Viser ${visible} av ${cards.length}`;
        }

        input.addEventListener("input", apply);
        select.addEventListener("change", apply);
        apply();
    }

    ready(() => {
        initOwnResources();
        initNeeds();
        document.querySelectorAll("[data-pn-list]").forEach(l => initNeeds(l, l.parentElement));
        document.querySelectorAll(".dc-panel .dc-grid").forEach(grid => {
            if (grid.id !== "ownResourcesGrid") initGrid(grid);
        });
    });
})();
