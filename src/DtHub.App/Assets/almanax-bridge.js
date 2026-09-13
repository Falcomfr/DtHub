// Reading the Almanax page from Ankama's portal.
//
// The window does not display the page: it reads it and draws itself. The
// portal renders a full desktop page, with its decor, its callout boxes and
// its mood text, none of which helps to know what to bring today.
//
// This script touches nothing: it reads and it posts. No color forced, no
// element hidden, no click simulated. The page stays what it is, it simply
// is not shown.
(function () {
    'use strict';

    function texte(noeud) {
        return noeud ? (noeud.textContent || '').replace(/\s+/g, ' ').trim() : '';
    }

    // An element's own text, without that of its children of a given
    // class: the bonus detail lives in ".more", which also contains the
    // quest block, and the two would otherwise mix together.
    function texteSansEnfants(noeud, exclus) {
        if (!noeud) {
            return '';
        }

        var morceaux = [];

        for (var i = 0; i < noeud.childNodes.length; i++) {
            var enfant = noeud.childNodes[i];

            if (enfant.nodeType === 3) {
                morceaux.push(enfant.nodeValue);
                continue;
            }

            if (enfant.nodeType === 1 && !enfant.matches(exclus)) {
                morceaux.push(enfant.textContent || '');
            }
        }

        return morceaux.join(' ').replace(/\s+/g, ' ').trim();
    }

    function lire() {
        // The right game's block, not just any one: without a filter, the
        // page carries one block per game, and DOFUS's comes first. So we
        // look for the one that names itself, and failing that we take
        // the first so that the C# side sees a title that is not Touch's
        // and refuses it.
        var blocs = document.querySelectorAll('#almanax_center .achievement');
        var bloc = blocs[0] || null;

        for (var b = 0; b < blocs.length; b++) {
            var t = texte(blocs[b].querySelector('.top h4'));

            if (t.toLowerCase().indexOf('dofus touch') >= 0) {
                bloc = blocs[b];
                break;
            }
        }

        // Without the game's block, there is nothing to read and above
        // all nothing certifying which game it is. We post anyway, with
        // an empty title: the C# side will refuse it, and the window will
        // say so.
        var titre = texte(bloc ? bloc.querySelector('.top h4') : null);
        var mid = bloc ? bloc.querySelector('.mid') : null;
        var more = mid ? mid.querySelector('.more') : null;
        var infos = more ? more.querySelector('.more-infos') : null;

        return {
            heading: titre,
            day: texte(document.querySelector('#almanax_day .day-number')),
            month: texte(document.querySelector('#almanax_day .day-text')),
            meryde: texte(document.querySelector('#almanax_boss_desc .title')),
            bonus: texteSansEnfants(mid, '.more'),
            bonusDetail: texteSansEnfants(more, '.more-infos'),
            quest: texte(infos ? infos.querySelector('p') : null),
            offering: texte(infos ? infos.querySelector('.more-infos-content p') : null),

            // "12 Septange : L'Aurore Pourpre". The link carries the date
            // and the name together, and it holds for the whole month:
            // this is what we come to prepare when looking at the days
            // ahead.
            monthEvent: texte(document.querySelector('#idbar_almanax_month_events a'))
        };
    }

    function poster() {
        try {
            window.chrome.webview.postMessage(JSON.stringify(lire()));
        } catch (erreur) {
            // Nothing to do: the window has its own timeout and will say
            // it could not read.
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', poster);
    } else {
        poster();
    }
})();
