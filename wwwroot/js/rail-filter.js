(() => {
    const list = document.getElementById("resourceResults");
    const tools = document.getElementById("railTools");
    const chipBox = document.getElementById("railStatusChips");
    const sort = document.getElementById("railSort");
    if (!list || !tools || !chipBox || !sort) return;

    const items = Array.from(list.querySelectorAll(":scope > li")).filter(li => li.querySelector(".rail-card"));
    if (items.length < 2) return;

    const original = items.slice();
    const collator = new Intl.Collator("nb", { numeric: true, sensitivity: "base" });
    const data = li => li.querySelector(".rail-card").dataset;
    const rank = s => (s || "").toLowerCase() === "tilgjengelig" ? 0 : (s || "").toLowerCase() === "tildelt" ? 1 : 2;

    const counts = new Map();
    items.forEach(li => {
        const s = data(li).status || "Ukjent";
        counts.set(s, (counts.get(s) || 0) + 1);
    });

    let active = "";
    chipBox.innerHTML = [["", "Alle", items.length], ...Array.from(counts, ([s, n]) => [s, s, n])]
        .map(([v, t, n]) => `<button type="button" class="rail-schip${v === "" ? " is-active" : ""}" data-v="${v}">${t} <span>${n}</span></button>`)
        .join("");
    tools.hidden = false;

    function apply() {
        items.forEach(li => { li.hidden = !!active && (data(li).status || "Ukjent") !== active; });
        const sorted = sort.value === "" ? original.slice() : items.slice().sort((a, b) => {
            const x = data(a), y = data(b);
            switch (sort.value) {
                case "name-desc": return collator.compare(y.name, x.name);
                case "status": return rank(x.status) - rank(y.status) || collator.compare(x.name, y.name);
                case "type": return collator.compare(x.type, y.type) || collator.compare(x.name, y.name);
                case "place": return collator.compare(a.querySelector("small")?.textContent || "", b.querySelector("small")?.textContent || "");
                default: return collator.compare(x.name, y.name);
            }
        });
        list.prepend(...sorted);
    }

    chipBox.addEventListener("click", e => {
        const b = e.target.closest(".rail-schip");
        if (!b) return;
        active = b.dataset.v;
        chipBox.querySelectorAll(".rail-schip").forEach(x => x.classList.toggle("is-active", x === b));
        apply();
    });
    sort.addEventListener("change", apply);
})();
