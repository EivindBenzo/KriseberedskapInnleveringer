(function () {
    var list = document.getElementById('klList');
    if (!list) return;
    var rows = Array.prototype.slice.call(list.querySelectorAll('.kl-row'));
    var $ = function (id) { return document.getElementById(id); };
    var search = $('klSearch'), role = $('klRole'), org = $('klOrg'), place = $('klPlace'), sort = $('klSort');

    function areaFor(rolle) {
        var r = (rolle || '').toLowerCase();
        if (r.indexOf('dispatch') >= 0 || r.indexOf('koordin') >= 0) return 'Koordinering og tildeling av ressurser';
        if (r.indexOf('verifi') >= 0) return 'Verifisering av ressurser';
        if (r.indexOf('leverand') >= 0) return 'Registrering og drift av egne ressurser';
        return 'Beredskap og samarbeid';
    }

    function select(row) {
        var c = JSON.parse(row.dataset.contact);
        rows.forEach(function (r) { r.classList.toggle('is-selected', r === row); r.setAttribute('aria-pressed', String(r === row)); });
        $('klEmpty').hidden = true;
        $('klCard').hidden = false;
        $('dAvatar').textContent = c.initials;
        $('dName').textContent = c.name;
        $('dRole').textContent = c.role;
        $('dOrg').textContent = c.org || 'Ikke oppgitt';
        $('dPlace').textContent = c.place || 'Ikke oppgitt';
        $('dArea').textContent = areaFor(c.role);
        var phone = $('dPhone'); phone.textContent = '';
        var call = $('dCall');
        if (c.phone) {
            var a = document.createElement('a'); a.href = 'tel:' + c.phone; a.textContent = c.phone; phone.appendChild(a);
            call.href = 'tel:' + c.phone; call.hidden = false;
        } else { phone.textContent = 'Ikke oppgitt'; call.hidden = true; }
        var mail = $('dEmail'); mail.textContent = '';
        var m = document.createElement('a'); m.href = 'mailto:' + c.email; m.textContent = c.email; mail.appendChild(m);
        var msg = $('dMsg');
        msg.hidden = c.self;
        msg.dataset.recipientId = c.id;
        msg.dataset.contactName = c.name;
        $('dResCount').textContent = c.resources.length;
        var res = $('dRes'); res.textContent = '';
        if (!c.resources.length) { var e = document.createElement('span'); e.className = 'kl-none'; e.textContent = 'Ingen registrerte ressurser'; res.appendChild(e); }
        c.resources.forEach(function (n) { var s = document.createElement('span'); s.className = 'kl-chip'; s.textContent = n; res.appendChild(s); });
        var act = $('dAct'); act.textContent = '';
        if (!c.activity.length) { var li0 = document.createElement('li'); li0.className = 'kl-none'; li0.textContent = 'Ingen aktivitet registrert'; act.appendChild(li0); }
        c.activity.forEach(function (x, i) {
            var li = document.createElement('li'); li.className = 'tone-' + (i % 3);
            var s = document.createElement('span'); s.textContent = x.text;
            var t = document.createElement('time'); t.textContent = x.when;
            li.appendChild(s); li.appendChild(t); act.appendChild(li);
        });
    }

    function apply() {
        var q = search.value.trim().toLowerCase();
        var n = 0;
        rows.forEach(function (r) {
            var ok = (!q || r.dataset.search.indexOf(q) >= 0) && (!role.value || r.dataset.role === role.value) &&
                (!org.value || r.dataset.org === org.value) && (!place.value || r.dataset.place === place.value);
            r.hidden = !ok;
            if (ok) n++;
        });
        $('klCount').textContent = n;
        $('klNone').hidden = n > 0;
    }

    function order() {
        var key = sort.value === 'role' ? 'role' : 'name';
        rows.sort(function (a, b) { return (a.dataset[key] + a.dataset.name).localeCompare(b.dataset[key] + b.dataset.name, 'nb'); })
            .forEach(function (r) { list.appendChild(r); });
    }

    list.addEventListener('click', function (e) {
        if (e.target.closest('[data-stop]')) return;
        var row = e.target.closest('.kl-row');
        if (row) select(row);
    });
    list.addEventListener('keydown', function (e) {
        if ((e.key === 'Enter' || e.key === ' ') && e.target.classList.contains('kl-row')) { e.preventDefault(); select(e.target); }
    });
    [search, role, org, place].forEach(function (el) { el.addEventListener('input', apply); });
    sort.addEventListener('change', order);
    $('klReset').addEventListener('click', function () { setTimeout(apply, 0); });
    order();
})();
