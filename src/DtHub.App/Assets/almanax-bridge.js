// Lecture de la page de l'Almanax du portail d'Ankama.
//
// La fenêtre n'affiche pas la page : elle la lit et se dessine elle-même. Le
// portail rend une page de bureau entière, avec son décor, ses encarts et ses
// textes d'ambiance, dont rien n'aide à savoir quoi apporter aujourd'hui.
//
// Ce script ne touche à rien : il lit et il poste. Aucune couleur imposée,
// aucun élément masqué, aucun clic simulé. La page reste ce qu'elle est, elle
// n'est simplement pas montrée.
(function () {
    'use strict';

    function texte(noeud) {
        return noeud ? (noeud.textContent || '').replace(/\s+/g, ' ').trim() : '';
    }

    // Le texte propre d'un élément, sans celui de ses enfants d'une classe
    // donnée : le détail du bonus vit dans « .more », qui contient aussi le
    // bloc de la quête, et les deux se mélangeraient.
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
        // Le bloc du bon jeu, et non le premier venu : sans filtre, la page
        // porte un bloc par jeu, et celui de DOFUS vient en tête. On cherche
        // donc celui qui se nomme, et à défaut on prend le premier pour que
        // le côté C# voie un titre qui n'est pas celui de Touch et refuse.
        var blocs = document.querySelectorAll('#almanax_center .achievement');
        var bloc = blocs[0] || null;

        for (var b = 0; b < blocs.length; b++) {
            var t = texte(blocs[b].querySelector('.top h4'));

            if (t.toLowerCase().indexOf('dofus touch') >= 0) {
                bloc = blocs[b];
                break;
            }
        }

        // Sans le bloc du jeu, il n'y a rien à lire et surtout rien qui
        // certifie de quel jeu il s'agit. On poste quand même, avec un titre
        // vide : le côté C# refusera, et la fenêtre le dira.
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
            offering: texte(infos ? infos.querySelector('.more-infos-content p') : null)
        };
    }

    function poster() {
        try {
            window.chrome.webview.postMessage(JSON.stringify(lire()));
        } catch (erreur) {
            // Rien à faire : la fenêtre a son propre délai et dira qu'elle
            // n'a pas pu lire.
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', poster);
    } else {
        poster();
    }
})();
