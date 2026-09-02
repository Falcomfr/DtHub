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
        // Du bandeau à liseré vert, les deux blocs que notre propre bandeau
        // reprend déjà : le succès avec l'étape, et la phrase de départ.
        '.pqa-quest-intro__facts',
        '.pqa-quest-intro__start',

        'nav.pqt-progress',
        'footer.papycha-article-footer',
        'footer.entry-footer'
    ];

    // Le titre de l'article, repris par le bandeau de la fenêtre de quêtes.
    // Gardé dans la fenêtre des pages liées, qui n'en réaffiche aucun et où
    // c'est le seul repère.
    var TITLE = '.entry-header';

    // Ce que la seule fenêtre de quêtes masque, parce qu'elle le refait à sa
    // manière : le titre, et les quêtes précédentes, que son pied donne déjà
    // sous forme de boutons. Les afficher deux fois n'aidait pas et coûtait de
    // la place en tête de guide.
    var HIDDEN_IN_QUEST = [
        TITLE,
        '.pqa-quest-intro__requirements'
    ];

    // Marque posée sur ce qui n'appartient pas au guide.
    //
    // Un attribut plutôt qu'un style en ligne : le site en réaffiche certains
    // après coup, et le fadeIn de jQuery réaffecte style.display, ce qui perd
    // le !important. Une déclaration !important d'une feuille, elle, l'emporte
    // sur un style en ligne ordinaire ; l'inverse n'est pas vrai.
    var MARK = 'data-dthub-hidden';

    // Vrai quand seule la mise en page nous intéresse : la fenêtre des pages
    // liées n'a ni étapes ni chaîne à suivre.
    var framingOnly = window.__dtHubFramingOnly === true;

    // Tout le reste de la page est masqué par la règle inverse : on remonte de
    // l'article jusqu'au corps et, à chaque étage, on cache ses frères.
    //
    // Nommer un par un les blocs du thème était voué à l'échec : le bandeau, le
    // menu, le fil d'Ariane, le volet contextuel, chacun avec sa classe, et un
    // bouton qui réapparaît ailleurs à la moindre mise à jour du site. Garder
    // l'article et écarter le reste ne dépend d'aucun nom.
    // Le contenu propre de la page : tout ce qui l'entoure est du décor.
    //
    // Un guide et une page de rubrique ont un « .entry-content », et c'est le
    // meilleur ancrage : il exclut jusqu'au fil d'Ariane et au volet latéral.
    // La carte n'en a aucun, parce qu'elle n'est pas un article ; on retombe
    // alors sur le repère de contenu du thème, présent sur toutes les pages
    // mesurées. Sans ce recours, la carte gardait l'en-tête du site et sa
    // bannière, soit le quart haut de la fenêtre.
    function content() {
        return document.querySelector('.entry-content')
            || document.querySelector('main#content');
    }

    function applyFraming() {
        var style = document.getElementById('dthub-framing');

        if (!style) {
            style = document.createElement('style');
            style.id = 'dthub-framing';
            document.head.appendChild(style);
        }

        var hidden = framingOnly ? HIDDEN : HIDDEN.concat(HIDDEN_IN_QUEST);

        style.textContent =
            hidden.join(',') + '{display:none !important}' +
            '[' + MARK + ']{display:none !important}' +

            // « .wrap » manquait, et c'est lui qui laissait voir le fond vert
            // du site : le thème lui donne cinquante pixels de marge en haut et
            // en bas, et n'annule la première qu'en dessous de cinq cent
            // quarante pixels de large, seuil que la fenêtre franchit dès qu'on
            // l'élargit. Les bordures du corps ajoutaient deux pixels verts.
            'body,#container,.wrap,main#content,#main{margin-top:0 !important;' +
            'margin-bottom:0 !important;padding-top:0 !important;' +
            'border-top-width:0 !important;border-bottom-width:0 !important}' +

            // Le guide occupe toute la largeur : la fenêtre est étroite, et les
            // marges d'un site prévu pour un grand écran y coûtent cher.
            '.entry-content{max-width:none !important;margin:0 !important;padding:12px !important}' +

            // Le bandeau d'intro ouvre désormais la page, ses deux premiers
            // blocs étant masqués : sa marge haute, prévue pour suivre un
            // titre, ne laisserait qu'un vide en tête de fenêtre.
            '.pqa-quest-intro{margin-top:0 !important;padding-top:0 !important}' +

            // La carte d'un donjon remonte de quatre-vingt-quatorze pixels pour
            // se glisser à côté de l'en-tête. Elle le recouvrait, et avec lui le
            // niveau et la pierre d'âme.
            //
            // Ce n'est pas notre mise en page : le site passe à deux colonnes
            // au-delà de huit cent quatre-vingts pixels de contenu, et la marge
            // négative y vaut quatre-vingt-quatorze au lieu de dix-huit. Chez
            // lui la colonne de guide fait six cent cinquante pixels, un volet
            // latéral prenant le reste : il n'atteint donc jamais cette branche.
            // Nous écartons ce volet, la colonne prend toute la place, et nous
            // tombons dans une mise en page que le site n'éprouve pas.
            //
            // Mesuré sur deux donjons, de neuf cent vingt-deux à mille neuf
            // cents pixels : recouvrement partout avant, nulle part après.
            '.pcd-info__map{margin-top:0 !important}';
    }

    function keepOnlyArticle() {
        var node = content();

        if (!node) {
            return;
        }

        // Le titre est un frère du contenu, et le masquage des frères passe
        // avant la feuille : sans cette réserve, la fenêtre des pages liées le
        // perdrait quand même, et une rubrique s'ouvrirait sans rien qui la
        // nomme.
        var titre = framingOnly ? document.querySelector(TITLE) : null;

        while (node && node !== document.body) {
            var parent = node.parentElement;

            if (!parent) {
                break;
            }

            for (var i = 0; i < parent.children.length; i++) {
                var sibling = parent.children[i];

                if (sibling !== node && sibling !== titre) {
                    sibling.setAttribute(MARK, '');
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

        // Sans guide, on ne sait pas ce qui est du décor : la page de carte n'a
        // pas d'article, et tout masquer lui ôterait ses propres contrôles.
        if (!guide) {
            return;
        }

        var nodes = document.body ? document.body.querySelectorAll('*') : [];

        for (var i = 0; i < nodes.length; i++) {
            var node = nodes[i];

            if (node === guide || guide.contains(node)) {
                continue;
            }

            var position = window.getComputedStyle(node).position;

            if (position === 'fixed' || position === 'sticky') {
                node.setAttribute(MARK, '');
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

        // Un titre de liste : court et terminé par deux-points. Le site en met
        // en gras comme le reste, et ils devenaient des étapes qui disaient
        // « Conseils. », « Korbax. », « En résumé. ».
        if (text.length <= 45 && /:\s*$/.test(text)) {
            return true;
        }

        // Un aparté entier entre parenthèses commente, il n'ordonne pas.
        return text.charAt(0) === '(' && text.charAt(text.length - 1) === ')';
    }

    // Verbes à l'impératif que leur terminaison ne trahit pas.
    var IRREGULIERS = ['faites', 'dites', 'soyez', 'ayez', 'sachez', 'veuillez'];

    // Mots après lesquels un « -ez » n'est pas un ordre mais un présent : « vous
    // validez », « qui rapportez », « ne partez ».
    var SUJETS = ['vous', 'ne', 'n', 'qui', 'que', 'qu', 'et'];

    // Mots courants en « -ez » qui ne sont pas des verbes.
    var FAUX_AMIS = ['chez', 'assez', 'nez', 'rez'];

    // Vrai si le texte donne un ordre au lecteur.
    //
    // La marque est grammaticale et non lexicale : l'impératif de la deuxième
    // personne du pluriel se termine en « -ez », sauf six irréguliers. Une
    // liste de verbes avait été essayée d'abord ; elle jetait « Faites votre
    // lit. », « Consultez la lettre de Mériana. », « Protégez Juzie et
    // Mériana ! », et il s'en serait trouvé d'autres à chaque guide.
    function orders(text) {
        // Le découpage doit être unicode : « \W » ne connaît que l'ASCII, et
        // « Protégez » y devient « Prot » et « gez ». Vingt-trois impératifs
        // accentués du corpus s'y perdaient, dont « Récupérez » et « Défendez ».
        var words = text.match(/[\p{L}\p{M}]+/gu) || [];

        for (var i = 0; i < words.length; i++) {
            var word = words[i].toLowerCase();

            if (IRREGULIERS.indexOf(word) >= 0) {
                return true;
            }

            // Quatre lettres et non cinq : « Tuez » en fait quatre.
            if (word.length >= 4
                && word.slice(-2) === 'ez'
                && FAUX_AMIS.indexOf(word) < 0) {
                var before = i > 0 ? words[i - 1].toLowerCase() : '';

                if (SUJETS.indexOf(before) < 0) {
                    return true;
                }
            }
        }

        return false;
    }

    var COORDONNEES = /\[\s*-?\d+\s*,\s*-?\d+\s*\]/;

    function isObjective(node) {
        if (node.querySelector('strong, b')) {
            return true;
        }

        return node.className.indexOf('has-text-color') >= 0
            || (node.getAttribute('style') || '').indexOf('color:') >= 0;
    }

    // Le départ de la quête, tel que le site le donne dans son bandeau d'intro.
    // Ce n'est pas un paragraphe du guide : c'est ce qu'il faut faire avant de
    // le lire.
    function departure() {
        return document.querySelector('.pqa-quest-intro__start .pqa-quest-intro__lead');
    }

    // Une page de donjon se reconnaît à son bloc d'en-tête, qui est du code du
    // site et non de la prose.
    function dungeon() {
        return document.querySelector('section.pcd-info');
    }

    // Les sections d'une page, dans leur ordre.
    //
    // Toutes les pages du site ne se parcourent pas comme un guide de quête. Un
    // donjon, un raid, une tanière, un chemin sont des dossiers : les monstres,
    // les salles, le boss pour l'un ; les étapes du trajet pour l'autre. Ce sont
    // ces sections qu'on suit, et non des paragraphes à résumer.
    //
    // Deux sources, dans cet ordre. Les titres d'abord : quatre-vingt-trois
    // donjons sur quatre-vingt-trois les écrivent au second rang, et huit
    // chemins sur vingt et un. À défaut, le sommaire que la page se donne : les
    // deux raids n'ont d'autre balise de titre que « Sommaire », et sept
    // tanières sur huit descendent les leurs au quatrième rang, hors de portée
    // d'une règle qui ne lit que le second. Aucun des sept cent quatre-vingt-deux
    // guides de quête ne porte de sommaire : la seconde source ne peut pas les
    // atteindre, et la première ne les atteignait déjà pas.
    function sections() {
        var found = headings();

        return found.length > 0 ? found : summary();
    }

    // Les titres de second rang, dans leur ordre.
    //
    // Deux sont écartés : « Position du PNJ sur la carte » double la carte que
    // le bloc d'en-tête porte déjà, et « Papycha remercie » est le pied de page.
    function headings() {
        var found = [];
        var seen = {};
        var titles = document.querySelectorAll('.entry-content h2');

        for (var i = 0; i < titles.length; i++) {
            var text = (titles[i].textContent || '').replace(/\s+/g, ' ').trim();

            if (text.length === 0
                || seen[text] === true
                || /^Position du PNJ/i.test(text)
                || /^Papycha remercie/i.test(text)) {
                continue;
            }

            seen[text] = true;
            found.push({ node: titles[i], text: text });
        }

        return found;
    }

    // Le plan que la page se donne : la liste de liens qui suit son
    // « Sommaire », chaque lien pointant une ancre de la page.
    function summary() {
        var list = summaryList();

        if (!list) {
            return [];
        }

        var found = [];
        var seen = {};
        var links = list.querySelectorAll('a[href]');

        for (var i = 0; i < links.length; i++) {
            var text = (links[i].textContent || '').replace(/\s+/g, ' ').trim();

            if (text.length === 0 || seen[text] === true) {
                continue;
            }

            var node = anchor(links[i], text);

            if (node === null) {
                continue;
            }

            seen[text] = true;
            found.push({ node: node, text: text });
        }

        return found;
    }

    // La liste qui suit le titre « Sommaire ». Elle le suit de près : au-delà de
    // quelques éléments, ce n'est plus son sommaire mais la page qui reprend.
    function summaryList() {
        var heads = document.querySelectorAll('.entry-content h1, .entry-content h2,'
            + ' .entry-content h3, .entry-content h4, .entry-content h5,'
            + ' .entry-content h6');

        for (var i = 0; i < heads.length; i++) {
            if (!/^sommaire$/i.test((heads[i].textContent || '').trim())) {
                continue;
            }

            var node = heads[i].nextElementSibling;

            for (var step = 0; node !== null && step < 4; step++) {
                if (node.tagName === 'UL' || node.tagName === 'OL') {
                    return node;
                }

                node = node.nextElementSibling;
            }

            return null;
        }

        return null;
    }

    // L'ancre que vise une entrée du sommaire.
    //
    // Le lien la donne, sauf quand le site se trompe : sur les quarante liens
    // relevés, trois pointent une ancre qui n'existe pas, « #salles » pour
    // « salle », « #succès » pour « stratégies ». Le texte du lien, lui, la
    // retrouve : on le compare aux identifiants de la page, sans accents ni
    // article. Trente-sept liens sur quarante trouvent ainsi leur cible ; les
    // trois autres n'en ont aucune sur la page, et leur entrée est passée.
    function anchor(link, text) {
        var href = link.getAttribute('href') || '';
        var cut = href.indexOf('#');
        var node = null;

        if (cut >= 0 && cut + 1 < href.length) {
            var hash = href.slice(cut + 1);

            try {
                node = document.getElementById(decodeURIComponent(hash));
            } catch (e) {
                node = document.getElementById(hash);
            }
        }

        if (node !== null) {
            return node;
        }

        var wanted = fold(text);

        if (wanted.length === 0) {
            return null;
        }

        var marked = document.querySelectorAll('.entry-content [id]');

        for (var i = 0; i < marked.length; i++) {
            if (fold(marked[i].id) === wanted) {
                return marked[i];
            }
        }

        return null;
    }

    // Un texte réduit à ce qui l'identifie : sans accents, sans article, sans
    // ponctuation. « Les stratégies » et « stratégies » s'y rejoignent.
    function fold(text) {
        return (text || '')
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '')
            .toLowerCase()
            .replace(/^(les|le|la|l)[\s'\u2019]+/, '')
            .replace(/[^a-z0-9]/g, '');
    }

    function steps() {
        var root = content();

        if (!root) {
            return [];
        }

        var found = [];

        // Une page qui porte des titres de sections se lit par eux : elle
        // n'ordonne rien, elle expose. Cela vaut pour les donjons, les tanières
        // et les chemins ; une page de quête n'en a aucun, relevé sur six
        // guides, et rien ne change donc pour elles.
        var titles = sections();

        if (titles.length > 0) {
            // Le départ garde son rang, ancré en haut de la page, quand la page
            // en a un. Son texte reste vide : la fenêtre le compose des
            // métadonnées du lieu, sa position et son gardien, qu'elle connaît
            // avant même la page. Un chemin n'en a pas : il commence où l'on est.
            if (dungeon()) {
                found.push({ node: root, text: '' });
            }

            return found.concat(titles);
        }

        // Le départ ouvre la marche, comme étape à part entière.
        //
        // Il était plaqué sur le premier paragraphe du guide, en supposant que
        // celui-ci disait où commencer. Mesuré sur seize guides, treize ouvrent
        // sur un préambule qui n'a rien à voir : « Cette quête est répétable »,
        // « La quête se lance à la suite de la précédente », « Divers : ». Le
        // bandeau annonçait donc « rendez-vous en [-64,-55] » au-dessus d'un
        // texte parlant d'autre chose.
        //
        // Son ancrage est le haut du guide : c'est là qu'on revient quand on
        // remonte à la première étape.
        var start = departure();

        if (start) {
            found.push({
                node: root,
                text: (start.textContent || '').replace(/\s+/g, ' ').trim()
            });
        }

        for (var i = 0; i < root.children.length; i++) {
            var node = root.children[i];

            if (node.tagName !== 'P' || !isObjective(node)) {
                continue;
            }

            var text = (node.textContent || '').replace(/\s+/g, ' ').trim();

            // Le gras ne suffit pas : le site en met sur ses commentaires de
            // combat, ses apartés et ses titres de liste. Mesuré sur
            // cinquante-cinq guides, cinq cent trente-deux paragraphes en gras
            // ne donnent que trois cent cinquante-sept consignes ; le reste
            // n'ordonne rien et n'avait donc rien à résumer.
            if (text.length > 0
                && !isNoise(text)
                && (COORDONNEES.test(text) || orders(text))) {
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

            // Dit à la fenêtre si la première étape est le départ : elle le
            // résume alors avec les métadonnées de la quête, plus sûres que la
            // prose du site. Sans cette marque, elle appliquerait ce traitement
            // au premier paragraphe des guides qui n'ont pas de bloc de départ.
            departure: departure() !== null || dungeon() !== null,
            steps: steps().map(function (s) { return s.text; })
        });
    }

    // Vrai quand la page n'a pas bougé. Le départ vaut tant qu'on n'a rien lu,
    // et il n'a pas d'autre ancrage : le haut du guide est aussi celui du
    // premier paragraphe, à quelques dizaines de pixels près.
    function atTop() {
        return (window.scrollY || document.documentElement.scrollTop || 0) <= 4;
    }

    // L'étape courante est le dernier paragraphe dont le haut est passé au-
    // dessus du tiers supérieur de la fenêtre : c'est celui qu'on est en train
    // de lire, pas celui qui vient d'apparaître en bas.
    //
    // Le départ échappe à cette règle. Il porte le rang zéro et son ancrage est
    // le haut du guide ; le premier paragraphe se trouvant à une cinquantaine
    // de pixels en dessous, il passait la marque avant qu'on ait rien fait
    // défiler, et le départ ne s'affichait jamais.
    function currentStep() {
        var found = steps();

        if (found.length === 0) {
            return -1;
        }

        if (departure() && atTop()) {
            return 0;
        }

        var mark = window.innerHeight / 3;
        var index = 0;

        for (var i = 0; i < found.length; i++) {
            if (found[i].node.getBoundingClientRect().top <= mark) {
                index = i;
            }
        }

        return index;
    }

    // Étape choisie au bouton, retenue jusqu'au prochain défilement de la main
    // de l'utilisateur. -1 quand rien n'est retenu.
    //
    // Deux raisons. Le défilement qu'on demande traverse les étapes
    // intermédiaires, et les rapporter une à une ferait défiler le bandeau. Et
    // sur une page trop courte pour défiler, l'étape visée n'atteint jamais la
    // marque : le bandeau revenait aussitôt à la précédente, et le clic passait
    // pour n'avoir rien fait.
    var held = -1;
    var release = null;

    // Le relâchement suit le dernier événement de défilement, et non le saut :
    // un défilement doux en émet pendant quelques centaines de millisecondes.
    // Armé dès le saut, pour le cas où la page ne bouge pas du tout.
    function holdStep(index) {
        held = index;

        if (release !== null) {
            window.clearTimeout(release);
        }

        release = window.setTimeout(function () {
            release = null;
            held = -1;
        }, 150);
    }

    function reportStep() {
        if (held >= 0) {
            return;
        }

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

            // Tant qu'une étape est retenue, chaque événement repousse le
            // relâchement : le défilement en cours est encore le nôtre.
            if (held >= 0) {
                holdStep(held);

                return;
            }

            reportStep();
        });
    }, { passive: true });

    window.__dtHubGoToStep = function (index) {
        var found = steps();

        if (index < 0 || index >= found.length) {
            return;
        }

        current = index;
        holdStep(index);

        var top = found[index].node.getBoundingClientRect().top + window.scrollY;

        // Une marge au-dessus : un paragraphe collé au bord haut se lit mal.
        // Le départ, lui, veut le vrai haut de page : c'est ce qui le rend
        // courant, et seize pixels plus bas suffiraient à l'en priver.
        window.scrollTo({
            top: index === 0 && departure() ? 0 : Math.max(0, top - 16),
            behavior: 'smooth'
        });
    };

    // Le bandeau d'intro n'a plus rien à montrer une fois retirés les blocs que
    // la fenêtre refait : sur la plupart des quêtes il ne reste que sa bordure,
    // une bande vide en tête de page. Mesuré sur quatre guides, seuls ceux dont
    // la quête est répétable gardent un bloc, celui de la récurrence. Le
    // bandeau n'est donc conservé que s'il lui reste quelque chose à dire.
    function trimIntro() {
        var intro = document.querySelector('section.pqa-quest-intro');

        if (!intro) {
            return;
        }

        for (var i = 0; i < intro.children.length; i++) {
            // Un élément masqué n'a pas de rectangle : la question ne demande
            // ni de savoir lequel, ni pourquoi il l'est.
            if (intro.children[i].getClientRects().length > 0) {
                return;
            }
        }

        intro.setAttribute(MARK, '');
    }

    function frame() {
        applyFraming();
        keepOnlyArticle();
        hideFloating();
        trimIntro();
    }

    function start() {
        frame();

        // Le suivi d'étapes n'a de sens que sur un guide de quête. Sur une page
        // de rubrique ou de carte, il n'y a rien à décrire ni à situer.
        if (!framingOnly) {
            describe();
            reportStep();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }

    // Le thème ajoute des blocs après coup : on recadre une fois la page
    // entièrement chargée, sinon le décor réapparaît.
    window.addEventListener('load', start);

    // Et ce qui arrive plus tard encore : le volet contextuel du site s'insère
    // quand il veut, et le bouton de retour en haut ne revient qu'au premier
    // défilement, longtemps après la dernière passe. Un observateur rattrape ce
    // qu'un nombre fini de passes laisserait passer.
    if (window.MutationObserver && document.body) {
        var pending = false;

        new window.MutationObserver(function () {
            // Le cadrage pose lui-même des attributs : réagir à chaque
            // mutation ferait boucler l'observateur sur son propre travail.
            if (pending) {
                return;
            }

            pending = true;

            window.setTimeout(function () {
                pending = false;
                frame();
            }, 120);
        }).observe(document.body, { childList: true, subtree: true });
    }
})();
