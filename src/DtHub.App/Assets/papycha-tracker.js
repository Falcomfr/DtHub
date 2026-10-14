// The Papycha tracker page as DT Hub shows it, on top of the guide bridge in
// framing mode, which already hides everything around the article.
//
// Signed out, the page offers three cards: create a space, open one with its
// phrase, recover a forgotten phrase. A player opening DT Hub's window has
// usually come to sign in, so the creation card waits behind a button.
//
// Signed in, the window opens on the player's quests in progress. They live
// on the site's home page, the "dashboard"; the tracker page itself only
// manages the account. The bar with the full access phrase and the account
// tools stays hidden: it is not what the window is for, and the phrase has no
// business on screen. So does the dashboard's "home" tab, empty there, and
// whatever the page shows around the tracker (the site's home cards).
(function () {
    'use strict';

    // Set by DT Hub before injection: the two labels, and whether the window
    // was opened to show the player's quests.
    var TEXT = __DTHUB_TEXT__;
    var QUESTS = __DTHUB_QUESTS__;
    var OFF = 'data-dthub-off';

    // The site keeps the open tab in its own storage and reads it as the app
    // starts: written before any of its scripts runs.
    if (QUESTS) {
        try {
            localStorage.setItem('pqt_active_panel', 'quests');
        } catch (e) {
            // Storage refused: the site opens on its home tab, nothing worse.
        }
    }

    function style() {
        if (document.getElementById('dthub-tracker')) {
            return;
        }

        var sheet = document.createElement('style');
        sheet.id = 'dthub-tracker';
        // In the bridge's layer, for the same reason: a site rule marked
        // important outside any layer cannot win against it.
        sheet.textContent = '@layer dthub{' +
            '.entry-header,.pqt-hero,.pqt-space-toolbar{display:none !important}' +
            '[' + OFF + ']{display:none !important}' +
            '#dthub-create-toggle{margin-top:16px}' +
            (QUESTS ? '[data-panel="home"]{display:none !important}' : '') + '}';
        document.head.appendChild(sheet);
    }

    // The tracker alone: its siblings in the article go, whatever they are.
    function isolate() {
        var root = document.getElementById('papycha-quest-tracker');

        if (!root || !root.parentNode) {
            return;
        }

        Array.prototype.forEach.call(root.parentNode.children, function (sibling) {
            if (sibling !== root) {
                sibling.setAttribute(OFF, '');
            }
        });
    }

    function cardOf(id) {
        var field = document.getElementById(id);

        return field ? field.closest('.pqt-card') : null;
    }

    function welcome() {
        var create = cardOf('pqt-create-space');
        var open = cardOf('pqt-use-code');
        var forgot = cardOf('pqt-recover');

        if (!create || !open || document.getElementById('dthub-create-toggle')) {
            return;
        }

        var creating = false;
        var toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.id = 'dthub-create-toggle';
        // Dressed like the site's own buttons.
        toggle.className = document.getElementById('pqt-use-code').className;

        function show() {
            create.toggleAttribute(OFF, !creating);
            open.toggleAttribute(OFF, creating);

            if (forgot) {
                forgot.toggleAttribute(OFF, creating);
            }

            toggle.textContent = creating ? TEXT.back : TEXT.create;
        }

        toggle.addEventListener('click', function () {
            creating = !creating;
            show();
        });

        var cards = create.closest('.pqt-welcome') || create.parentNode;
        cards.parentNode.insertBefore(toggle, cards.nextSibling);
        show();
    }

    // The site draws its tabs after loading: the "in progress" filter is
    // pressed as soon as it exists, once per page.
    function quests() {
        var active = document.querySelector('#papycha-quest-tracker [data-filter="active"]');

        if (!active) {
            return false;
        }

        active.click();

        return true;
    }

    document.addEventListener('DOMContentLoaded', function () {
        style();
        isolate();
        welcome();

        if (!QUESTS || quests()) {
            return;
        }

        var root = document.getElementById('papycha-quest-tracker');

        if (!root) {
            return;
        }

        var watch = new MutationObserver(function () {
            if (quests()) {
                watch.disconnect();
            }
        });

        watch.observe(root, { childList: true, subtree: true });
        setTimeout(function () {
            watch.disconnect();
        }, 10000);
    });
})();
