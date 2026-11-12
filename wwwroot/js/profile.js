(function () {
    var root = document.documentElement;
    function lagre(k, v) { try { localStorage.setItem(k, v); } catch (e) { } }
    function hent(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }

    function bruk() {
        root.setAttribute('data-text', hent('pref-text') === 'large' ? 'large' : 'normal');
        root.setAttribute('data-motion', hent('pref-motion') === 'reduced' ? 'reduced' : 'normal');
    }
    bruk();

    // Skjuler profilbilder som ikke lastes, slik at initialer/ikon vises i stedet.
    document.querySelectorAll('.nav-profile img, [data-avatar-img]').forEach(function (img) {
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
