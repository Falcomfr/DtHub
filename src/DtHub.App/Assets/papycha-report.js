// Prépare le formulaire de signalement du site dans notre fenêtre.
//
// Le bloc vit en pied d'article, replié derrière un « details ». Trois gestes,
// et aucun de plus : l'ouvrir, y poser où l'on en était, et ne montrer que lui.
//
// Rien n'est envoyé, rien n'est rempli à la place du lecteur sauf le repère
// d'étape, qu'il peut effacer. Le champ anti-robot du site n'est pas touché.
(function () {
    'use strict';

    var bloc = document.getElementById('papycha-report-error');

    if (!bloc) {
        return 'absent';
    }

    bloc.open = true;

    // Le repère d'étape, seulement si le champ est vide : revenir sur la
    // fenêtre ne doit pas écraser ce que le lecteur vient d'écrire.
    var lieu = bloc.querySelector('[name="papycha_report_location"]');

    if (lieu && !lieu.value) {
        lieu.value = __DTHUB_LOCATION__;
    }

    // Ne montrer que le bloc : on remonte jusqu'au corps en masquant, à chaque
    // étage, tout ce qui n'est pas sur le chemin. Masquer plutôt que retirer,
    // pour ne rien casser de ce que le site attend autour de son formulaire.
    var style = document.createElement('style');

    style.textContent =
        '[data-dthub-report-hidden]{display:none !important}'

        // Les étages traversés perdent tout ce qui les faisait larges, décorés
        // ou défilants : la mise en page du site suppose une pleine page, et
        // notre fenêtre en fait quatre cents pixels.
        + '[data-dthub-report-path]{display:block !important;width:auto !important;'
        + 'max-width:none !important;min-width:0 !important;margin:0 !important;'
        + 'padding:0 !important;border:0 !important;background:none !important;'
        + 'box-shadow:none !important;overflow:visible !important;float:none !important;'
        + 'position:static !important;transform:none !important}'

        // La teinte est celle de nos panneaux ; le formulaire garde ses propres
        // couleurs, que le site donne clair sur sombre.
        // Le fond de l'article est une photo pleine page, et un seul élément
        // doit défiler : deux barres paraissaient côte à côte, celle du
        // document et celle d'un étage traversé.
        + 'html{background:#12141a !important;height:100% !important;'
        + 'overflow:hidden !important;margin:0 !important;padding:0 !important}'
        + 'body{background:#12141a !important;height:100% !important;'
        + 'overflow-x:hidden !important;overflow-y:auto !important;'
        + 'margin:0 !important;padding:0 !important;width:auto !important;'
        + 'max-width:none !important}'

        + '#papycha-report-error{margin:14px !important;overflow:visible !important}'

        // Le résumé reste : c'est le seul moyen de replier et de rouvrir le
        // bloc, et le masquer l'avait supprimé. Il est ramené à gauche, le site
        // l'alignant à droite pour terminer une ligne de métadonnées ; seul, il
        // pendait hors du cadre. Il garde son soulignement, qui dit qu'on peut
        // le toucher.
        + '#papycha-report-error > summary{margin:0 0 12px 0 !important;'
        + 'width:auto !important;text-align:left !important;font-size:1rem !important}'

        // Le thème du site souligne un libellé qui a le focus, et la décoration
        // se propage à tout ce qu'il contient : cliquer dans un champ soulignait
        // son intitulé et sa valeur. On la coupe là où elle naît, sur le libellé
        // lui-même, faute de quoi elle traverse ses descendants sans qu'ils
        // puissent s'en défaire.
        + '#papycha-report-error label,#papycha-report-error label:focus,'
        + '#papycha-report-error label.focus,#papycha-report-error fieldset,'
        + '#papycha-report-error legend{text-decoration:none !important}'

        // Rien du formulaire ne dépasse : les champs sont dimensionnés pour une
        // colonne d'article, et débordaient de la fenêtre par la droite. Une
        // largeur maximale garde les champs lisibles sur un écran large, où ils
        // s'étiraient sur toute la fenêtre.
        + '#papycha-report-error,#papycha-report-error *{max-width:100% !important;'
        + 'box-sizing:border-box !important}'
        + '#papycha-report-error{max-width:560px !important;padding-bottom:20px !important}'

        // La zone de texte ne s'étire plus à la poignée : le formulaire défile,
        // il ne se redimensionne pas. Sa hauteur suit celle de la fenêtre, avec
        // deux bornes, pour tenir aussi bien sur un portable que sur un grand
        // écran. Haute comme le site la donne, elle poussait le bouton d'envoi
        // sous le bord inférieur, où on ne le trouvait plus.
        + '#papycha-report-error textarea{resize:none !important;'
        + 'height:20vh !important;min-height:100px !important;max-height:240px !important}'

        // Le bouton d'envoi du site est invisible : sa feuille de style lui
        // donne « background: currentColor » et « color: Canvas », si bien que
        // le fond prend la couleur du texte du bouton, c'est-à-dire la sienne.
        // On lui rend les deux couleurs qu'il visait, dans le même vocabulaire
        // de couleurs système, pour qu'il suive le thème clair ou sombre.
        + '#papycha-report-error .papycha-report__submit{background:CanvasText !important;'
        + 'border-color:CanvasText !important;color:Canvas !important;'
        + 'margin-top:8px !important}';

    document.head.appendChild(style);

    var noeud = bloc;

    while (noeud && noeud.parentElement && noeud !== document.body) {
        var parent = noeud.parentElement;
        var freres = parent.children;

        for (var i = 0; i < freres.length; i++) {
            if (freres[i] !== noeud) {
                freres[i].setAttribute('data-dthub-report-hidden', '');
            }
        }

        if (parent !== document.body) {
            parent.setAttribute('data-dthub-report-path', '');
        }

        noeud = parent;
    }

    var quoi = bloc.querySelector('[name="papycha_report_description"]');

    if (quoi) {
        quoi.focus();
    }

    // La hauteur qu'il faudrait à la fenêtre pour montrer le formulaire en
    // entier, marges comprises. La fenêtre s'y pose, bornée à l'écran : elle
    // n'a que ce formulaire à montrer, et rien ne justifie qu'elle le coupe
    // quand la place existe.
    return 'pret ' + Math.ceil(bloc.getBoundingClientRect().height + 28);
})();
