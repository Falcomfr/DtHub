// The Papycha tracker page as DT Hub shows it, on top of the guide bridge in
// framing mode, which already hides everything around the article.
//
// Signed out, the page offers three cards: create a space, open one with its
// phrase, recover a forgotten phrase. A player opening DT Hub's window has
// usually come to sign in, so the creation card waits behind a button.
//
// Signed in, the window opens on the player's stats, with the quests tab
// already filtered on those in progress. They live on the site's home page,
// the "dashboard"; the tracker page itself only manages the account. The bar
// with the full access phrase and the account tools stays hidden: it is not
// what the window is for, and the phrase has no business on screen. What the
// page shows around the tracker (welcome text, home cards, Discord) is the
// dashboard's "home" tab, and shows only under it.
(function () {
    'use strict';

    // Set by DT Hub before injection: the two labels, and whether the window
    // was opened to show the player's quests.
    var TEXT = __DTHUB_TEXT__;
    var QUESTS = __DTHUB_QUESTS__;
    // The character chosen in DT Hub, or '' when none.
    var CHARACTER = __DTHUB_CHARACTER__;
    var OFF = 'data-dthub-off';
    var HOME = 'data-dthub-home';

    // The site keeps the open tab in its own storage and reads it as the app
    // starts: written before any of its scripts runs.
    if (QUESTS) {
        try {
            localStorage.setItem('pqt_active_panel', 'stats');

            // The site opens on the character it remembers in the same
            // storage: DT Hub's choice is written there, once per window,
            // since this script runs again on every reload and the player
            // may have picked another character on the site meanwhile.
            if (CHARACTER && !sessionStorage.getItem('dthub-character')) {
                localStorage.setItem('pqt_active_character', CHARACTER);
                sessionStorage.setItem('dthub-character', '1');
            }
        } catch (e) {
            // Storage refused: the site opens on its home tab, nothing worse.
        }

        // And the other way: a character picked on the site is the one the
        // site writes to that storage key. DT Hub is told, so its own bar
        // follows. Wrapped once, before any of the site's scripts runs.
        var write = Storage.prototype.setItem;
        var told = CHARACTER;

        Storage.prototype.setItem = function (key, value) {
            write.apply(this, arguments);

            if (this === window.localStorage && key === 'pqt_active_character' && value && value !== told) {
                told = value;

                try {
                    window.chrome.webview.postMessage(JSON.stringify({ kind: 'trackerCharacter', id: value }));
                } catch (e) {
                    // Outside DT Hub there is no one to tell.
                }
            }
        };
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
            '#papycha-quest-tracker [' + OFF + '],[' + OFF + ']{display:none !important}' +
            // Framed, the tracker's block is the whole window: no bottom
            // margin left over from the page it came from.
            '#papycha-quest-tracker{margin-bottom:0 !important}' +
            '.dthub-help{margin-top:14px}' +
            '.dthub-help summary{cursor:pointer}' +
            '.dthub-help ul{margin:8px 0 0;padding-left:18px}' +
            '#dthub-create-toggle{margin-top:16px}' +
            // Under the home tab, the site alone decides: it shows its cards
            // there and hides them elsewhere, but leaves its welcome text
            // under every tab. Signed out, there is no tab: hidden too.
            'body:not(:has([data-panel="home"].is-active)) [' + HOME + ']{display:none !important}}';
        document.head.appendChild(sheet);
    }

    // The site paints its page grey around a darker block, the tracker. With
    // the page cut down to that block, the grey only showed as a band below
    // it: the window takes the block's own colour instead, read here so it
    // follows the site's light or dark theme.
    function backdrop() {
        var root = document.getElementById('papycha-quest-tracker');
        var colour = root ? getComputedStyle(root).backgroundColor : '';

        if (!colour || colour === 'rgba(0, 0, 0, 0)' || colour === 'transparent') {
            return;
        }

        var sheet = document.createElement('style');
        sheet.textContent = '@layer dthub{html,body,#container,.wrap,#main,main#content{' +
            'background:' + colour + ' !important;background-image:none !important}}';
        document.head.appendChild(sheet);
    }

    // Where the phrase can be found, under the field that asks for it.
    function help(card) {
        if (card.querySelector('.dthub-help')) {
            return;
        }

        var details = document.createElement('details');
        details.className = 'dthub-help';

        var summary = document.createElement('summary');
        summary.textContent = TEXT.helpTitle;
        details.appendChild(summary);

        var list = document.createElement('ul');

        // The account page, in the player's own browser where they are
        // signed in: target=_blank sends it there, this window being the
        // one that is not.
        var first = document.createElement('li');
        var link = document.createElement('a');
        link.href = TEXT.open.url;
        link.target = '_blank';
        link.rel = 'noopener';
        link.textContent = TEXT.open.label;
        first.appendChild(link);
        list.appendChild(first);

        TEXT.help.forEach(function (line) {
            var item = document.createElement('li');
            item.textContent = line;
            list.appendChild(item);
        });

        details.appendChild(list);
        card.appendChild(details);
    }

    // The tracker alone: its siblings in the article go, whatever they are,
    // except under the home tab, which is them.
    function isolate() {
        var root = document.getElementById('papycha-quest-tracker');

        if (!root || !root.parentNode) {
            return;
        }

        Array.prototype.forEach.call(root.parentNode.children, function (sibling) {
            if (sibling !== root) {
                sibling.setAttribute(HOME, '');
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

        help(open);

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

    // The site draws the quests tab's filters only while that tab is shown,
    // and the window opens on the stats: the "in progress" filter is pressed
    // the first time it exists, once per page.
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
        backdrop();
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
    });
})();
