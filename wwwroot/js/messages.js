(function () {
    const composer = document.getElementById('messageComposer');
    if (composer) {
        const recipientId = document.getElementById('messageRecipientId');
        const heading = document.getElementById('messageComposerHeading');
        const subject = composer.querySelector('[name="subject"]');

        document.querySelectorAll('.contact-message-button, .message-reply-button').forEach(function (button) {
            button.addEventListener('click', function () {
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
        unreadDialog.showModal();
        unreadDialog.querySelector('[data-close-unread]').addEventListener('click', function () { unreadDialog.close(); });
        unreadDialog.addEventListener('click', function (event) {
            if (event.target === unreadDialog) unreadDialog.close();
        });
    }
})();
