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

        // Le résumé du bloc est masqué : le bloc est déjà déplié, et son titre
        // est celui de la fenêtre. Le site l'aligne à droite, où il termine une
        // ligne de métadonnées ; seul, il pendait hors du cadre.
        + '#papycha-report-error > summary{display:none !important}'

        // Rien du formulaire ne dépasse : les champs sont dimensionnés pour une
        // colonne d'article, et débordaient de la fenêtre par la droite.
        + '#papycha-report-error,#papycha-report-error *{max-width:100% !important;'
        + 'box-sizing:border-box !important}';

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

    return 'pret';
})();
