(function () {
    const composer = document.getElementById('messageComposer');
    if (composer) {
        const recipientId = document.getElementById('messageRecipientId');
        const heading = document.getElementById('messageComposerHeading');
        const subject = composer.querySelector('[name="subject"]');

        document.querySelectorAll('.contact-message-button, .message-reply-button').forEach(function (button) {
            button.addEventListener('click', function (event) {
                if (button.classList.contains('contact-message-button') && button.dataset.recipientId) {
                    event.stopImmediatePropagation();
                    window.location.href = '/Messages/Inbox?with=' + encodeURIComponent(button.dataset.recipientId);
                    return;
                }
                recipientId.value = button.dataset.recipientId;
                heading.textContent = 'Til ' + button.dataset.contactName;
                if (button.dataset.subject) subject.value = 'SV: ' + button.dataset.subject;
                composer.showModal();
                subject.focus();
            });
        });

        composer.querySelectorAll('[data-close-message]').forEach(function (button) {
            button.addEventListener('click', function () { composer.close(); });
        });
        composer.addEventListener('click', function (event) {
            if (event.target === composer) composer.close();
        });
    }

    const unreadDialog = document.getElementById('unreadMessages');
    if (unreadDialog) {
        const key = 'unread-message-popup:' + unreadDialog.dataset.unreadMessageIds;
        const dismiss = function () {
            sessionStorage.setItem(key, 'dismissed');
            unreadDialog.close();
        };
        if (sessionStorage.getItem(key) !== 'dismissed') unreadDialog.showModal();
        unreadDialog.querySelector('[data-close-unread]').addEventListener('click', dismiss);
        unreadDialog.addEventListener('click', function (event) {
            if (event.target === unreadDialog) dismiss();
        });
    }
})();
