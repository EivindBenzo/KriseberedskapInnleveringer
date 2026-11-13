(function () {
    var root = document.documentElement;
    function lagre(k, v) { try { localStorage.setItem(k, v); } catch (e) { } }
    function hent(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }

    function bruk() {
        root.setAttribute('data-text', hent('pref-text') === 'large' ? 'large' : 'normal');
        root.setAttribute('data-motion', hent('pref-motion') === 'reduced' ? 'reduced' : 'normal');
    }
    bruk();

    // Bjelle- og kontomenyen lukkes ved klikk utenfor eller Escape.
    var menyer = document.querySelectorAll('.um-pop');
    document.addEventListener('click', function (e) {
        menyer.forEach(function (m) { if (!m.contains(e.target)) m.removeAttribute('open'); });
    });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') menyer.forEach(function (m) { m.removeAttribute('open'); });
    });
    menyer.forEach(function (m) {
        m.addEventListener('toggle', function () {
            if (m.open) menyer.forEach(function (a) { if (a !== m) a.removeAttribute('open'); });
        });
    });

    // Varsler markeres som sett enkeltvis ved klikk, eller alle samtidig med "Marker alle som lest".
    var bjelle = document.querySelector('.um-bell');
    if (bjelle) {
        var sendt = function (url, nokkel) {
            var token = document.querySelector('.user-menu input[name=__RequestVerificationToken]');
            var kropp = new URLSearchParams();
            if (token) kropp.set('__RequestVerificationToken', token.value);
            if (nokkel) kropp.set('key', nokkel);
            return fetch(url, { method: 'POST', body: kropp, credentials: 'same-origin', keepalive: true });
        };
        var oppdater = function (n) {
            var prikk = bjelle.querySelector('.um-dot');
            if (!prikk) return;
            if (n <= 0) prikk.remove(); else prikk.textContent = n > 9 ? '9+' : String(n);
        };
        var teller = function () { return bjelle.querySelectorAll('li:not(.is-read) [data-notif-key]').length; };
        bjelle.querySelectorAll('[data-notif-key]').forEach(function (a) {
            a.addEventListener('click', function () {
                var li = a.closest('li');
                if (li.classList.contains('is-read')) return;
                li.classList.add('is-read');
                oppdater(teller());
                sendt('/Account/MarkNotificationSeen', a.getAttribute('data-notif-key')).catch(function () { });
            });
        });
        var alle = bjelle.querySelector('[data-mark-all]');
        if (alle) alle.addEventListener('click', function () {
            sendt('/Account/MarkNotificationsSeen').then(function (r) {
                if (!r.ok) return;
                bjelle.querySelectorAll('li').forEach(function (li) { li.classList.add('is-read'); });
                oppdater(0);
                alle.remove();
            }).catch(function () { });
        });
    }
    var vakt = document.querySelector('[data-vakt]');
    if (vakt) {
        vakt.addEventListener('change', function () { vakt.className = 'ph-select vakt-' + vakt.value; });
    }

    // Skjuler profilbilder som ikke lastes, slik at initialer/ikon vises i stedet.
    document.querySelectorAll('.nav-avatar, [data-avatar-img]').forEach(function (img) {
        img.addEventListener('error', function () { img.remove(); });
    });

    document.querySelectorAll('[data-pref-text]').forEach(function (b) {
        var valg = b.getAttribute('data-pref-text');
        b.setAttribute('aria-pressed', String(root.getAttribute('data-text') === valg));
        b.addEventListener('click', function () {
            lagre('pref-text', valg);
            bruk();
            document.querySelectorAll('[data-pref-text]').forEach(function (x) {
                x.setAttribute('aria-pressed', String(x.getAttribute('data-pref-text') === valg));
            });
        });
    });

    var motion = document.querySelector('[data-pref-motion]');
    if (motion) {
        var vis = function () {
            var på = root.getAttribute('data-motion') === 'reduced';
            motion.textContent = på ? 'På' : 'Av';
            motion.setAttribute('aria-pressed', String(på));
        };
        vis();
        motion.addEventListener('click', function () {
            lagre('pref-motion', root.getAttribute('data-motion') === 'reduced' ? 'normal' : 'reduced');
            bruk();
            vis();
        });
    }

    var fil = document.querySelector('[data-avatar-file]');
    var skjema = document.querySelector('[data-avatar-form]');
    var data = document.querySelector('[data-avatar-data]');
    if (fil && skjema && data) {
        fil.addEventListener('change', function () {
            var f = fil.files && fil.files[0];
            if (!f) return;
            var url = URL.createObjectURL(f);
            var img = new Image();
            img.onload = function () {
                var s = 256, c = document.createElement('canvas');
                c.width = c.height = s;
                var ctx = c.getContext('2d');
                var kant = Math.min(img.width, img.height);
                ctx.fillStyle = '#fff';
                ctx.fillRect(0, 0, s, s);
                ctx.drawImage(img, (img.width - kant) / 2, (img.height - kant) / 2, kant, kant, 0, 0, s, s);
                data.value = c.toDataURL('image/jpeg', 0.85);
                URL.revokeObjectURL(url);
                skjema.submit();
            };
            img.onerror = function () { URL.revokeObjectURL(url); };
            img.src = url;
        });
    }
})();
