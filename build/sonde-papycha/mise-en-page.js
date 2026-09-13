// Layout probe. Development tool, never used by the application.
//
// It looks for what overlaps in a site page once framed the way the
// guides window frames it. This is how the dungeon map defect was found:
// a top margin of minus ninety-four pixels, in a two-column layout the
// site never reaches on its own, its guide column being six hundred fifty
// pixels wide when the threshold is eight hundred eighty. We discard its
// side panel, the column takes all the room, and we fall into a branch
// it never exercises.
//
// Usage: open a site page in a browser, set the window to the width of
// the guides window, paste this into the console.
//
// What it reports:
//   - negative margins, the mechanism of the only known defect so far;
//   - what overflows the column;
//   - top-level siblings that overlap vertically.
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
