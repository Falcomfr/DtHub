// Pont entre la page de guide et la fenêtre de quêtes.
//
// Il fait trois choses, et rien d'autre : il cadre la page sur le guide, il
// repère les étapes, et il dit à l'application où l'on se trouve. Il ne modifie
// jamais le texte du guide lui-même.
//
// Le décor du site est masqué parce que la fenêtre le reprend à son compte :
// la chaîne des quêtes est rejouée dans notre pied de fenêtre, et le titre dans
// notre bandeau. L'afficher deux fois volerait de la place au seul contenu qui
// compte, sur une fenêtre volontairement étroite posée à côté du jeu.
(function () {
    'use strict';

    if (window.__dtHubQuestBridge) {
        return;
    }

    window.__dtHubQuestBridge = true;

    // Ce qui est masqué à l'intérieur même de l'article, parce que la fenêtre
    // le reprend à son compte : le titre, la chaîne des quêtes, les crédits.
    var HIDDEN = [
        '.entry-header',
        'nav.pqt-progress',
        'footer.papycha-article-footer',
        'footer.entry-footer'
    ];

    // Tout le reste de la page est masqué par la règle inverse : on remonte de
    // l'article jusqu'au corps et, à chaque étage, on cache ses frères.
    //
    // Nommer un par un les blocs du thème était voué à l'échec : le bandeau, le
    // menu, le fil d'Ariane, le volet contextuel, chacun avec sa classe, et un
    // bouton qui réapparaît ailleurs à la moindre mise à jour du site. Garder
    // l'article et écarter le reste ne dépend d'aucun nom.
    function content() {
        return document.querySelector('.entry-content');
    }

    function applyFraming() {
        var style = document.getElementById('dthub-framing');

        if (!style) {
            style = document.createElement('style');
            style.id = 'dthub-framing';
            document.head.appendChild(style);
        }

        style.textContent =
            HIDDEN.join(',') + '{display:none !important}' +
            'body,main.content,#main,#container{margin-top:0 !important;padding-top:0 !important}' +
            // Le guide occupe toute la largeur : la fenêtre est étroite, et les
            // marges d'un site prévu pour un grand écran y coûtent cher.
            '.entry-content{max-width:none !important;margin:0 !important;padding:12px !important}';
    }

    function keepOnlyArticle() {
        var node = content();

        if (!node) {
            return;
        }

        while (node && node !== document.body) {
            var parent = node.parentElement;

            if (!parent) {
                break;
            }

            for (var i = 0; i < parent.children.length; i++) {
                var sibling = parent.children[i];

                if (sibling !== node) {
                    sibling.style.setProperty('display', 'none', 'important');
                }
            }

            node = parent;
        }
    }

    // Le thème pose après coup des éléments flottants : un bouton de menu, un
    // volet contextuel. Les nommer un par un serait à refaire à chaque
    // changement du site ; on traite la catégorie. Tout ce qui flotte au-dessus
    // de la page sans appartenir au guide est du décor, et la fenêtre est trop
    // étroite pour en supporter.
    function hideFloating() {
        var guide = content();
        var nodes = document.body ? document.body.querySelectorAll('*') : [];

        for (var i = 0; i < nodes.length; i++) {
            var node = nodes[i];

            if (guide && (node === guide || guide.contains(node))) {
                continue;
            }

            var position = window.getComputedStyle(node).position;

            if (position === 'fixed' || position === 'sticky') {
                node.style.setProperty('display', 'none', 'important');
            }
        }
    }

    // Une étape est un paragraphe de consigne, et le site les distingue de la
    // narration en les mettant en gras. Mesuré sur trois quêtes de types
    // différents : 35 paragraphes donnent 17 consignes, 6 en donnent 5, 3 en
    // donnent 3, et tout ce qui ressort est bien une instruction. Compter tous
    // les paragraphes annoncerait trente-quatre étapes là où il y en a dix-sept,
    // dont la moitié serait du récit.
    //
    // Une quête ancienne emploie une couleur au lieu du gras : les deux marques
    // valent, faute de balisage propre à cet usage.
    // Ce que le site met en gras sans que ce soit une consigne : ses propres
    // encarts, et les apartés. Relevé sur 335 étapes de 36 quêtes.
    var ETIQUETTES = /^\s*(pr[ée].?requis|source|plage habituelle|dur[ée]e|note|notes|attention|astuce|remarque|rappel|info|informations?)\s*:/i;

    function isNoise(text) {
        if (ETIQUETTES.test(text)) {
            return true;
        }

        // Un aparté entier entre parenthèses commente, il n'ordonne pas.
        return text.charAt(0) === '(' && text.charAt(text.length - 1) === ')';
    }

    function isObjective(node) {
        if (node.querySelector('strong, b')) {
            return true;
        }

        return node.className.indexOf('has-text-color') >= 0
            || (node.getAttribute('style') || '').indexOf('color:') >= 0;
    }

    function steps() {
        var root = content();

        if (!root) {
            return [];
        }

        var found = [];

        for (var i = 0; i < root.children.length; i++) {
            var node = root.children[i];

            if (node.tagName !== 'P' || !isObjective(node)) {
                continue;
            }

            var text = (node.textContent || '').replace(/\s+/g, ' ').trim();

            if (text.length > 0 && !isNoise(text)) {
                found.push({ node: node, text: text });
            }
        }

        return found;
    }

    var current = -1;

    function post(message) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(JSON.stringify(message));
        }
    }

    function describe() {
        var intro = document.querySelector('.pqa-quest-intro');
        var chain = document.querySelector('nav.pqt-progress');

        post({
            kind: 'loaded',
            intro: intro ? intro.outerHTML : '',
            chain: chain ? chain.outerHTML : '',
            steps: steps().map(function (s) { return s.text; })
        });
    }

    // L'étape courante est le dernier paragraphe dont le haut est passé au-
    // dessus du tiers supérieur de la fenêtre : c'est celui qu'on est en train
    // de lire, pas celui qui vient d'apparaître en bas.
    function currentStep() {
        var found = steps();
        var mark = window.innerHeight / 3;
        var index = 0;

        for (var i = 0; i < found.length; i++) {
            if (found[i].node.getBoundingClientRect().top <= mark) {
                index = i;
            }
        }

        return found.length === 0 ? -1 : index;
    }

    function reportStep() {
        var index = currentStep();

        if (index !== current) {
            current = index;
            post({ kind: 'step', index: index });
        }
    }

    var pending = false;

    window.addEventListener('scroll', function () {
        if (pending) {
            return;
        }

        pending = true;

        // Un rapport par image plutôt qu'un par pixel parcouru : le défilement
        // en émet des centaines par seconde.
        window.requestAnimationFrame(function () {
            pending = false;
            reportStep();
        });
    }, { passive: true });

    window.__dtHubGoToStep = function (index) {
        var found = steps();

        if (index < 0 || index >= found.length) {
            return;
        }

        var top = found[index].node.getBoundingClientRect().top + window.scrollY;

        // Une marge au-dessus : un paragraphe collé au bord haut se lit mal.
        window.scrollTo({ top: Math.max(0, top - 16), behavior: 'smooth' });
    };

    function start() {
        applyFraming();
        keepOnlyArticle();
        hideFloating();
        describe();
        reportStep();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }

    // Le thème ajoute des blocs après coup : on recadre une fois la page
    // entièrement chargée, sinon le décor réapparaît.
    window.addEventListener('load', function () {
        start();

        // Et une dernière passe une seconde plus tard, pour ce que les scripts
        // du site posent encore après leur propre chargement.
        window.setTimeout(function () {
            applyFraming();
            keepOnlyArticle();
            hideFloating();
            describe();
        }, 1000);
    });
})();
