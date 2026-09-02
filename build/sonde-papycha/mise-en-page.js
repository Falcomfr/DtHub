// Sonde de mise en page. Outil de développement, jamais employé par
// l'application.
//
// Elle cherche ce qui se recouvre dans une page du site une fois cadrée comme
// la fenêtre des guides la cadre. C'est ainsi qu'a été trouvé le défaut de la
// carte des donjons : une marge haute de moins quatre-vingt-quatorze pixels,
// dans une mise en page à deux colonnes que le site n'atteint jamais chez lui,
// sa colonne de guide faisant six cent cinquante pixels quand le seuil est à
// huit cent quatre-vingts. Nous écartons son volet latéral, la colonne prend
// toute la place, et nous tombons dans une branche qu'il n'éprouve pas.
//
// Emploi : ouvrir une page du site dans un navigateur, régler la fenêtre à la
// largeur de la fenêtre des guides, coller ceci dans la console.
//
// Ce qu'elle relève :
//   - les marges négatives, mécanisme du seul défaut connu à ce jour ;
//   - ce qui déborde de la colonne ;
//   - les frères de premier rang qui se chevauchent verticalement.
(function () {
    function cadrer() {
        var noeud = document.querySelector('.entry-content')
            || document.querySelector('main#content');

        while (noeud && noeud !== document.body) {
            var parent = noeud.parentElement;

            if (!parent) {
                break;
            }

            for (var i = 0; i < parent.children.length; i++) {
                if (parent.children[i] !== noeud) {
                    parent.children[i].style.display = 'none';
                }
            }

            noeud = parent;
        }

        var style = document.createElement('style');

        style.textContent =
            '.entry-content{max-width:none !important;margin:0 !important;padding:12px !important}';
        document.head.appendChild(style);
    }

    function sonder() {
        var racine = document.querySelector('.entry-content')
            || document.querySelector('main#content');
        var large = racine.getBoundingClientRect().width;
        var negatives = [];
        var debords = [];
        var chevauchements = [];
        var cotes = ['marginTop', 'marginLeft', 'marginRight', 'marginBottom'];
        var tous = racine.querySelectorAll('*');

        for (var i = 0; i < tous.length; i++) {
            var e = tous[i];
            var boite = e.getBoundingClientRect();

            if (boite.width === 0 || boite.height === 0) {
                continue;
            }

            var style = getComputedStyle(e);

            for (var c = 0; c < cotes.length; c++) {
                var valeur = parseFloat(style[cotes[c]]);

                if (valeur < -4) {
                    negatives.push((e.className || e.tagName) + ' ' + cotes[c] + ' ' + Math.round(valeur));
                }
            }

            if (boite.width > large + 2) {
                debords.push((e.className || e.tagName) + ' ' + Math.round(boite.width));
            }
        }

        var enfants = [];

        for (var k = 0; k < racine.children.length; k++) {
            var b = racine.children[k].getBoundingClientRect();

            if (b.height > 0) {
                enfants.push({ nom: racine.children[k].className, b: b });
            }
        }

        for (var a = 0; a < enfants.length; a++) {
            for (var d = a + 1; d < enfants.length; d++) {
                var x = enfants[a].b;
                var y = enfants[d].b;

                if (!(x.y + x.height <= y.y + 1 || y.y + y.height <= x.y + 1)) {
                    chevauchements.push(enfants[a].nom + ' et ' + enfants[d].nom);
                }
            }
        }

        return {
            largeur: Math.round(large),
            margesNegatives: negatives,
            debordements: debords,
            chevauchements: chevauchements,
        };
    }

    cadrer();

    return sonder();
})();
