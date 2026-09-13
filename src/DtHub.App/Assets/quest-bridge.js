// Bridge between the guide page and the quest window.
//
// It does three things, and nothing else: it frames the page around the
// guide, it locates the steps, and it tells the application where we are. It
// never modifies the guide's own text.
//
// The site's decor is hidden because the window reproduces it on its own
// terms: the quest chain is replayed in our window footer, and the title in
// our banner. Showing it twice would steal room from the only content that
// matters, on a window deliberately narrow and placed next to the game.
(function () {
    'use strict';

    if (window.__dtHubQuestBridge) {
        return;
    }

    window.__dtHubQuestBridge = true;

    // What is hidden in all our windows, because none of them has any use
    // for it: the two banner blocks that our own banner already reproduces,
    // and the article footer credits.
    var HIDDEN = [
        // From the green-bordered banner, the two blocks that our own
        // banner already reproduces: the success with its step, and the
        // departure sentence.
        '.pqa-quest-intro__facts',
        '.pqa-quest-intro__start',

        'footer.papycha-article-footer',
        'footer.entry-footer'
    ];

    // The article title, reproduced by the quest window's banner. Kept in
    // the linked-pages window, which shows none of it again and where it
    // is the only landmark.
    var TITLE = '.entry-header';

    // What only the quest window hides, because it redoes it its own way:
    // the title, and the previous quests, which its footer already gives as
    // buttons. Showing them twice did not help and cost room at the top of
    // the guide.
    //
    // The intro banner is passed in whole, not child by child. All of them
    // already were except the classification, "Type : Principale", which a
    // list of children had let through. Since the departure step anchors at
    // the top of the content, that was what we saw as the first step,
    // instead of the guide's prose. A list of children would let through
    // whatever the site adds next; the section, on the other hand, covers
    // all of them.
    //
    // Hiding removes nothing from the document: the bridge still reads the
    // departure block and still posts the intro banner to the window.
    //
    // **The site's progress block is no longer here, and that is a
    // deliberate step back.** It was hidden as long as the window's footer
    // replaced it, then given back to the linked-pages window, which had
    // none. The footer can only announce a single follow-up, and stays
    // silent as soon as the site names several: almost four hundred guides
    // therefore said nothing about what they unlock.
    //
    // Showing it as the site draws it is better than redrawing it. It
    // already sorts its follow-ups by objective, it follows the page
    // instead of taking height from it, and its links go through the
    // window's ordinary routing: a quest opens in place, a success in a
    // separate window.
    //
    // No risk to step tracking, verified: "steps" only reads the direct "p"
    // children of the content, and "headings" only the "h2". The block is a
    // "nav" and its subheadings are "h3".
    var HIDDEN_IN_QUEST = [
        TITLE,
        '.pqa-quest-intro'
    ];

    // Mark placed on whatever does not belong to the guide.
    //
    // An attribute rather than an inline style: the site re-shows some of
    // them afterwards, and jQuery's fadeIn resets style.display, which
    // loses the !important. An !important declaration from a stylesheet,
    // on the other hand, wins over an ordinary inline style; the reverse
    // is not true.
    var MARK = 'data-dthub-hidden';

    // True when only the layout matters to us: the linked-pages window has
    // neither steps nor a chain to follow.
    var framingOnly = window.__dtHubFramingOnly === true;

    // The rest of the page is hidden by the reverse rule: we climb from the
    // article up to the body and, at each level, hide its siblings.
    //
    // Naming the theme's blocks one by one was bound to fail: the banner,
    // the menu, the breadcrumb, the context panel, each with its own class,
    // and a button that reappears elsewhere at the slightest site update.
    // Keeping the article and discarding the rest depends on no name.
    // The page's own content: everything around it is decor.
    //
    // A guide and a category page have an ".entry-content", and it is the
    // best anchor: it excludes even the breadcrumb and the side panel. The
    // map has none, because it is not an article; we then fall back on the
    // theme's content landmark, present on every page measured. Without
    // this fallback, the map kept the site's header and its banner, that
    // is the top quarter of the window.
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

            // ".wrap" was missing, and it is what let the site's green
            // background show through: the theme gives it fifty pixels of
            // margin at the top and bottom, and only cancels the first one
            // below five hundred forty pixels of width, a threshold the
            // window crosses as soon as it is widened. The body's borders
            // added two green pixels.
            'body,#container,.wrap,main#content,#main{margin-top:0 !important;' +
            'margin-bottom:0 !important;padding-top:0 !important;' +
            'border-top-width:0 !important;border-bottom-width:0 !important}' +

            // The guide takes up the full width: the window is narrow, and
            // the margins of a site designed for a large screen cost dearly
            // here.
            '.entry-content{max-width:none !important;margin:0 !important;padding:12px !important}' +

            // The intro banner now opens the page, its first two blocks
            // being hidden: its top margin, meant to follow a title, would
            // otherwise leave only empty space at the top of the window.
            '.pqa-quest-intro{margin-top:0 !important;padding-top:0 !important}' +

            // A dungeon's card moves up ninety-four pixels to tuck itself
            // next to the header. It used to cover it, and with it the
            // level and the soul stone.
            //
            // This is not our own layout: the site switches to two columns
            // beyond eight hundred eighty pixels of content, and the
            // negative margin there is ninety-four instead of eighteen. On
            // its own site the guide column is six hundred fifty pixels
            // wide, with a side panel taking the rest: it therefore never
            // reaches this branch. We discard that panel, the column takes
            // all the room, and we fall into a layout the site never
            // exercises.
            //
            // Measured on two dungeons, from nine hundred twenty-two to one
            // thousand nine hundred pixels: overlap everywhere before,
            // nowhere after.
            '.pcd-info__map{margin-top:0 !important}';
    }

    function keepOnlyArticle() {
        var node = content();

        if (!node) {
            return;
        }

        // The title is a sibling of the content, and hiding siblings runs
        // before the stylesheet: without this exception, the linked-pages
        // window would lose it anyway, and a category page would open
        // without anything naming it.
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

    // The theme adds floating elements after the fact: a menu button, a
    // context panel. Naming them one by one would have to be redone at
    // every site change; we handle the category instead. Anything that
    // floats above the page without belonging to the guide is decor, and
    // the window is too narrow to bear it.
    function hideFloating() {
        var guide = content();

        // Without a guide, we do not know what is decor: the map page has
        // no article, and hiding everything would strip it of its own
        // controls.
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

    // A step is an instruction paragraph, and what distinguishes it from
    // narration is grammatical: an order given to the reader, or
    // coordinates.
    //
    // Bold text long served as a first filter, even before reading the
    // text. It no longer does: measured across the 782 guides, this
    // requirement left one hundred sixty guides without a single step, not
    // because they have no instructions, but because their authors never
    // use bold. This is not a site convention, it is an author's habit,
    // and recognizing that yields one thousand two hundred fifty-five
    // instructions.
    //
    // What really filters is what follows: noise first, then order.
    // Measured on randomly sampled additions read back one by one,
    // twenty-one out of twenty-two are genuine instructions.
    // What the site writes without it being an instruction: its own
    // callout boxes, and asides. Measured on 335 steps from 36 quests.
    var ETIQUETTES = /^\s*(pr[ée].?requis|source|plage habituelle|dur[ée]e|note|notes|attention|astuce|remarque|rappel|important|info|informations?)\s*:/i;

    // The departure announcement, which the banner already gives as the
    // first step and which the guide repeats in prose: "La quête se lance
    // en [2,-16] en parlant à Kerubim Crépin." ("The quest starts at
    // [2,-16] by talking to Kerubim Crépin.") Measured on the site's 782
    // guides, thirty paragraphs, all restatements of the departure block
    // and none an instruction.
    var DEPART = /^(la|cette)\s+qu[eê]te\b[\s\S]{0,90}?\b(se\s+(lance|d[ée]clenche|d[ée]bloque)|est\s+(disponible|r[ée]p[ée]table|accessible))/i;

    function isNoise(text) {
        if (ETIQUETTES.test(text)) {
            return true;
        }

        // Except when the announcement also carries its own instruction:
        // "La quête se lance en [-68,-76] auprès du Barron d'Ouillard,
        // renseignez-vous sur son rôle." ("The quest starts at [-68,-76]
        // with the Barron d'Ouillard, ask about his role.") Four guides out
        // of the 782 are in this case.
        if (DEPART.test(text) && !orders(text)) {
            return true;
        }

        // A list heading: short and ending with a colon. The site puts it
        // in bold like the rest, and they used to become steps saying
        // "Conseils." (Tips.), "Korbax.", "En résumé." (Summary.).
        if (text.length <= 45 && /:\s*$/.test(text)) {
            return true;
        }

        // A whole aside in parentheses comments, it does not give orders.
        return text.charAt(0) === '(' && text.charAt(text.length - 1) === ')';
    }

    // Imperative verbs whose ending does not give them away.
    var IRREGULIERS = ['faites', 'dites', 'soyez', 'ayez', 'sachez', 'veuillez'];

    // Words after which a "-ez" ending is not an order but a present tense:
    // "vous validez" ("you validate"), "qui rapportez" ("who report"), "et
    // faites l'acquisition" ("and acquire").
    //
    // Negation no longer appears here. It used to, for "vous ne partez
    // pas" ("you do not leave"), but it carried away with it every
    // negative order: "N'oubliez pas de lui reparler une seconde fois !"
    // ("Do not forget to talk to him again a second time!"), "Ne partez
    // pas sans lui parler." ("Do not leave without talking to him.") It is
    // not negation that distinguishes the present tense from the order,
    // it is the subject; negation is therefore crossed like a pronoun, and
    // the subject is sought behind it.
    var SUJETS = ['vous', 'qui', 'que', 'qu', 'et'];

    // Object pronouns that slip in between the subject and its verb.
    //
    // The guard only looked one word back, and a pronoun was therefore
    // enough to fool it: "et vous lui faites part du mal être de Tira"
    // ("and you tell him about Tira's distress"), "que vous lui infligez"
    // ("that you inflict on him"), "Vous y découvrez un message" ("You
    // discover a message there") passed for orders when they are
    // narration. Measured on the site's 782 guides, ninety-four
    // paragraphs, all narration, none an instruction.
    //
    // "vous" appears in both lists, and that is intentional: we skip over
    // pronouns but stop on it, since it is the subject we are looking for.
    var PRONOMS = [
        'le', 'la', 'l', 'les', 'lui', 'leur', 'me', 'm', 'te', 't',
        'se', 's', 'nous', 'vous', 'y', 'en', 'ne', 'n'];

    // Words in "-ez" that do not give an order: common nouns, and the
    // future tense of "avoir" (to have) and "être" (to be), whose
    // imperatives "ayez" and "soyez" already appear above. "Lorsque vous
    // l'aurez vaincu, il se met automatiquement à vous suivre" ("Once you
    // have defeated him, he automatically starts following you") is
    // narration, and the subject guard misses it because the elided
    // pronoun slips in between "vous" and "aurez". Eighteen paragraphs out
    // of the 782 guides, all narration.
    //
    // Added to these are the imperfect forms of verbs whose imperative is
    // written quite differently: "vous saviez", "vous aviez", "vous
    // étiez". A "-iez" ending gives nothing away by itself, since
    // "remerciez", "oubliez" and "privilégiez" are genuine ones, and
    // nothing in the form separates them. But for these particular verbs,
    // the order is said as "sachez", "ayez", "soyez", "faites", "allez":
    // neither the imperfect nor the conditional of these verbs can
    // therefore be an order.
    var FAUX_AMIS = [
        'chez', 'assez', 'nez', 'rez', 'aurez', 'serez',
        'aviez', 'saviez', 'étiez', 'deviez', 'pouviez', 'vouliez',
        'faisiez', 'alliez', 'veniez', 'preniez', 'disiez', 'voyiez',
        'auriez', 'seriez', 'pourriez', 'devriez', 'voudriez', 'sauriez',
        'feriez', 'iriez', 'viendriez', 'prendriez', 'diriez', 'verriez'];

    // True if the text gives an order to the reader.
    //
    // The marker is grammatical, not lexical: the second-person plural
    // imperative ends in "-ez", except for six irregulars. A list of verbs
    // had been tried first; it discarded "Faites votre lit." ("Make your
    // bed."), "Consultez la lettre de Mériana." ("Read Mériana's
    // letter."), "Protégez Juzie et Mériana !" ("Protect Juzie and
    // Mériana!"), and more would have turned up with every guide.
    function orders(text) {
        // The splitting must be unicode-aware: "\W" only knows ASCII, and
        // "Protégez" becomes "Prot" and "gez" under it. Twenty-three
        // accented imperatives from the corpus were lost this way,
        // including "Récupérez" and "Défendez".
        var words = text.match(/[\p{L}\p{M}]+/gu) || [];

        for (var i = 0; i < words.length; i++) {
            var word = words[i].toLowerCase();

            // The subject does not always touch its verb: we climb back
            // over object pronouns before judging.
            var j = i - 1;

            while (j >= 0
                && PRONOMS.indexOf(words[j].toLowerCase()) >= 0
                && SUJETS.indexOf(words[j].toLowerCase()) < 0) {
                j--;
            }

            var before = j >= 0 ? words[j].toLowerCase() : '';

            // The subject guard applies to irregulars just as much as to
            // the others, and did not used to: "vous faites" ("you do"),
            // "vous vous faites agresser" ("you get attacked"), "et faites
            // l'acquisition" ("and acquire") passed for orders. Seven
            // paragraphs out of the 782 guides, all narration.
            if (SUJETS.indexOf(before) >= 0) {
                continue;
            }

            if (IRREGULIERS.indexOf(word) >= 0) {
                return true;
            }

            // Four letters and not five: "Tuez" ("Kill") is only four.
            if (word.length >= 4
                && word.slice(-2) === 'ez'
                && FAUX_AMIS.indexOf(word) < 0) {
                return true;
            }
        }

        return false;
    }

    var COORDONNEES = /\[\s*-?\d+\s*,\s*-?\d+\s*\]/;

    // The quest's departure, as the site gives it in its intro banner.
    // This is not a guide paragraph: it is what must be done before
    // reading it.
    function departure() {
        return document.querySelector('.pqa-quest-intro__start .pqa-quest-intro__lead');
    }

    // A dungeon page is recognized by its header block, which is the
    // site's own markup and not prose.
    function dungeon() {
        return document.querySelector('section.pcd-info');
    }

    // A page's sections, in their order.
    //
    // Not every page on the site is read like a quest guide. A dungeon, a
    // raid, a lair, a path are dossiers: the monsters, the rooms, the boss
    // for one; the steps of the route for the other. It is these sections
    // that are followed, not paragraphs to summarize.
    //
    // Two sources, in this order. Headings first: eighty-three dungeons
    // out of eighty-three write them at the second level, and eight paths
    // out of twenty-one. Failing that, the summary the page gives itself:
    // the two raids have no heading tag other than "Sommaire" ("Summary"),
    // and seven lairs out of eight put theirs at the fourth level, out of
    // reach of a rule that only reads the second. None of the seven
    // hundred eighty-two quest guides carries a summary: the second source
    // cannot reach them, and the first did not reach them either.
    function sections() {
        var found = headings();

        return found.length > 0 ? found : summary();
    }

    // The second-level headings, in their order.
    //
    // Two are discarded: "Position du PNJ sur la carte" ("NPC position on
    // the map") duplicates the map the header block already carries, and
    // "Papycha remercie" ("Papycha thanks") is the page footer.
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
            found.push({ node: titles[i], text: text, title: true });
        }

        return found;
    }

    // The outline the page gives itself: the list of links that follows
    // its "Sommaire" ("Summary"), each link pointing to an anchor on the
    // page.
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
            found.push({ node: node, text: text, title: true });
        }

        return found;
    }

    // The list that follows the "Sommaire" heading. It follows closely:
    // beyond a few items, it is no longer its summary but the page
    // resuming.
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

    // The anchor a summary entry targets.
    //
    // The link gives it, except when the site gets it wrong: of the forty
    // links surveyed, three point to an anchor that does not exist,
    // "#salles" for "salle" ("room"), "#succès" for "stratégies"
    // ("strategies"). The link's text, however, finds it: it is compared
    // against the page's ids, without accents or article. Thirty-seven
    // links out of forty find their target this way; the other three have
    // none on the page, and their entry is skipped.
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

    // A text reduced to what identifies it: without accents, without
    // article, without punctuation. "Les stratégies" and "stratégies"
    // ("The strategies" and "strategies") meet there.
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

        // A page that carries section headings is read by them: it does
        // not give orders, it presents. This holds for dungeons, lairs and
        // paths; a quest page has none, measured on six guides, and
        // nothing therefore changes for them.
        var titles = sections();

        if (titles.length > 0) {
            // The departure keeps its rank, anchored at the top of the
            // page, when the page has one. Its text stays empty: the
            // window composes it from the location's metadata, its
            // position and its guardian, which it knows even before the
            // page. A path has none: it starts wherever we are.
            if (dungeon()) {
                found.push({ node: root, text: '', title: false });
            }

            return found.concat(titles);
        }

        // The departure opens the walk, as a step in its own right.
        //
        // It used to be pinned to the guide's first paragraph, assuming
        // that paragraph said where to start. Measured on sixteen guides,
        // thirteen open on a preamble that has nothing to do with it:
        // "Cette quête est répétable" ("This quest is repeatable"), "La
        // quête se lance à la suite de la précédente" ("The quest starts
        // right after the previous one"), "Divers : " ("Miscellaneous: ").
        // The banner would therefore announce "rendez-vous en [-64,-55]"
        // ("meet at [-64,-55]") above a text talking about something else.
        //
        // Its anchor is the top of the guide: that is where we return to
        // when we scroll back up to the first step.
        var start = departure();

        if (start) {
            found.push({
                node: root,
                text: (start.textContent || '').replace(/\s+/g, ' ').trim(),
                title: false
            });
        }

        for (var i = 0; i < root.children.length; i++) {
            var node = root.children[i];

            if (node.tagName !== 'P') {
                continue;
            }

            var text = (node.textContent || '').replace(/\s+/g, ' ').trim();

            // Not every paragraph is an instruction: the site narrates as
            // much as it orders. Measured on fifty-five guides, five
            // hundred thirty-two bold paragraphs yielded only three
            // hundred fifty-seven instructions; the rest gives no orders
            // and therefore had nothing to summarize. It is this test that
            // does the sorting now, not formatting anymore.
            if (text.length > 0
                && !isNoise(text)
                && (COORDONNEES.test(text) || orders(text))) {
                found.push({ node: node, text: text, title: false });
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

            // Tells the window whether the first step is the departure:
            // it then summarizes it with the quest's metadata, more
            // reliable than the site's prose. Without this marker, it
            // would apply this treatment to the first paragraph of guides
            // that have no departure block.
            departure: departure() !== null || dungeon() !== null,

            // Each step comes with its nature. A section heading presents,
            // an instruction gives orders, and the window does not show
            // them the same way: it shows the heading as is and shows
            // nothing of an instruction. This nature was known here and
            // discarded on the next line, which forced the window to
            // guess it.
            steps: steps().map(function (s) {
                return { text: s.text, title: s.title === true };
            })
        });
    }

    // True when the page has not moved. The departure holds as long as
    // nothing has been read, and it has no other anchor: the top of the
    // guide is also that of the first paragraph, give or take a few dozen
    // pixels.
    function atTop() {
        return (window.scrollY || document.documentElement.scrollTop || 0) <= 4;
    }

    // The current step is the last paragraph whose top has passed above
    // the window's upper third: it is the one we are in the process of
    // reading, not the one that has just appeared at the bottom.
    //
    // The departure escapes this rule. It holds rank zero and its anchor
    // is the top of the guide; since the first paragraph sits some fifty
    // pixels below, it used to cross the mark before anything had been
    // scrolled, and the departure would never display.
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

    // Step chosen via the button, held until the user's next manual
    // scroll. -1 when nothing is held.
    //
    // Two reasons. The scroll we request passes through the intermediate
    // steps, and reporting them one by one would make the banner scroll
    // too. And on a page too short to scroll, the targeted step never
    // reaches the mark: the banner would immediately fall back to the
    // previous one, and the click would look like it had done nothing.
    var held = -1;
    var release = null;

    // The release follows the last scroll event, not the jump: a smooth
    // scroll emits them for a few hundred milliseconds. Armed as soon as
    // the jump happens, in case the page does not move at all.
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

        // One report per frame rather than per pixel scrolled: scrolling
        // emits hundreds of them per second.
        window.requestAnimationFrame(function () {
            pending = false;

            // While a step is held, each event pushes back the release:
            // the ongoing scroll is still ours.
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

        // A margin above: a paragraph pinned to the top edge reads
        // poorly. The departure, however, wants the true top of the page:
        // that is what makes it current, and sixteen pixels lower would
        // be enough to deprive it of that.
        window.scrollTo({
            top: index === 0 && departure() ? 0 : Math.max(0, top - 16),
            behavior: 'smooth'
        });
    };

    // The intro banner has nothing left to show once the blocks the
    // window redoes are removed: on most quests all that is left is its
    // border, an empty strip at the top of the page. Measured on four
    // guides, only those whose quest is repeatable keep a block, the
    // recurrence one. The banner is therefore kept only if it still has
    // something to say.
    function trimIntro() {
        var intro = document.querySelector('section.pqa-quest-intro');

        if (!intro) {
            return;
        }

        for (var i = 0; i < intro.children.length; i++) {
            // A hidden element has no rectangle: the question does not
            // ask which one, nor why it is hidden.
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

        // Step tracking only makes sense on a quest guide. On a category
        // or map page, there is nothing to describe or locate.
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

    // The theme adds blocks after the fact: we reframe once the page has
    // fully loaded, otherwise the decor reappears.
    window.addEventListener('load', start);

    // And what arrives even later still: the site's context panel slots
    // itself in whenever it wants, and the back-to-top button only
    // returns on the first scroll, long after the last pass. An observer
    // catches what a finite number of passes would let through.
    if (window.MutationObserver && document.body) {
        var pending = false;

        new window.MutationObserver(function () {
            // The framing itself sets attributes: reacting to every
            // mutation would make the observer loop on its own work.
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
