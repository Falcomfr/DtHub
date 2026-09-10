// Sonde de développement : ce que le pont retient comme étapes, sur les 782
// guides du site.
//
// Elle n'est pas un portage. Elle découpe dans quest-bridge.js le bloc qui
// porte la règle et l'exécute tel quel : ce qu'elle mesure est donc exactement
// ce que la fenêtre affichera, et non une redite qui pourrait diverger. C'est
// ce qui manquait à l'audit de D76, dont les nombres n'ont jamais pu être
// rejoués.
//
// Node sert au développement seulement. L'application n'en dépend pas, et rien
// de ce fichier n'est embarqué.
//
// Le corpus est mis en cache au premier passage, et relu ensuite : la règle se
// règle par essais successifs, et refaire huit requêtes au site à chaque essai
// serait payer le réseau pour rien. « --relire » force la reprise en ligne.
//
// Le détail part dans etapes.json, une ligne par étape retenue avec son guide,
// son texte entier et ce qui l'a retenue. D76 regrette de n'avoir pas fait
// cela : ses 3 293 étapes n'ont jamais pu être retrouvées.
//
//   node build/sonde-papycha/audit-etapes.mjs
//   node build/sonde-papycha/audit-etapes.mjs "découverte d'un destin"
//   node build/sonde-papycha/audit-etapes.mjs --relire
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const ici = dirname(fileURLToPath(import.meta.url));
const pont = join(ici, '..', '..', 'src', 'DtHub.App', 'Assets', 'quest-bridge.js');

// La règle, prise dans le pont entre ses deux bornes stables.
function regleDuPont() {
    const source = readFileSync(pont, 'utf8');
    const debut = source.indexOf('    var ETIQUETTES');
    const apres = source.indexOf('    var COORDONNEES');

    if (debut < 0 || apres < 0) {
        throw new Error(
            'Bornes introuvables dans quest-bridge.js : la règle a été déplacée, '
            + 'et cette sonde doit être resituée avant de croire ses chiffres.');
    }

    const bloc = source.slice(debut, source.indexOf('\n', apres));

    return new Function(
        bloc + '\nreturn { isNoise, orders, COORDONNEES, IRREGULIERS, SUJETS, FAUX_AMIS, PRONOMS };')();
}

// Les enfants directs du contenu, avec leur texte.
//
// Une pile explicite, et non un compteur de profondeur : le site laisse
// traîner des balises fermantes orphelines, et un simple compteur tombait à
// zéro au milieu du document, ce qui faisait passer tout le reste pour du
// premier niveau. Le défaut est silencieux et fausse tous les nombres.
const VIDES = new Set(
    ['img', 'br', 'hr', 'input', 'meta', 'link', 'source', 'col', 'area', 'wbr', 'embed']);

function premierNiveau(html) {
    const blocs = [];
    const pile = [];
    let courant = null;

    const jeton = /<\/?([a-zA-Z][\w-]*)[^>]*>|([^<]+)/g;
    let m;

    while ((m = jeton.exec(html)) !== null) {
        if (m[2] !== undefined) {
            if (courant) { courant.texte += m[2]; }
            continue;
        }

        const nom = m[1].toLowerCase();

        if (VIDES.has(nom)) { continue; }

        if (m[0].startsWith('</')) {
            if (!pile.includes(nom)) { continue; }
            while (pile.length && pile.pop() !== nom) { /* remonter */ }
            if (pile.length === 0 && courant) {
                courant.balisage = html.slice(courant.debut, jeton.lastIndex);
                blocs.push(courant);
                courant = null;
            }
            continue;
        }

        if (pile.length === 0) { courant = { nom, texte: '', debut: m.index, balisage: '' }; }
        pile.push(nom);
    }

    for (const b of blocs) { b.texte = b.texte.replace(/\s+/g, ' ').trim(); }

    return blocs;
}

// Le site rend ses titres avec des entités : « d&rsquo;un » et non « d'un ».
// Sans quoi la recherche par titre ne trouve jamais rien.
function lisible(titre) {
    return titre
        .replace(/&(?:rsquo|#8217|#x2019);/g, '\u2019')
        .replace(/&(?:lsquo|#8216);/g, '\u2018')
        .replace(/&(?:nbsp|#160);/g, ' ')
        .replace(/&(?:amp|#38);/g, '&')
        .replace(/&(?:hellip|#8230);/g, '\u2026')
        .replace(/&(?:quot|#34);/g, '"');
}

// Les apostrophes typographiques et droites doivent se répondre, sinon
// chercher « d'un destin » ne trouve pas « d\u2019un destin ».
function pliee(texte) {
    return lisible(texte).toLowerCase().replace(/[\u2018\u2019]/g, "'");
}

const corpus = join(ici, 'corpus.json');
const rapport = join(ici, 'etapes.json');

async function guides() {
    if (existsSync(corpus) && !process.argv.includes('--relire')) {
        const garde = JSON.parse(readFileSync(corpus, 'utf8'));
        console.log(`corpus relu sur disque : ${garde.length} guides`);
        return new Map(garde.map((g) => [g.lien, { titre: g.titre, html: g.html }]));
    }

    const pages = new Map();

    for (let page = 1; page <= 12; page++) {
        const url = 'https://papycha.fr/wp-json/wp/v2/posts?categories=7&per_page=100'
            + `&page=${page}&_fields=link,title,content`;
        const reponse = await fetch(url);

        if (!reponse.ok) { break; }

        const lot = await reponse.json();

        for (const p of lot) {
            pages.set(p.link, { titre: lisible(p.title.rendered), html: p.content.rendered });
        }

        if (lot.length < 100) { break; }
    }

    writeFileSync(corpus, JSON.stringify(
        [...pages].map(([lien, g]) => ({ lien, titre: g.titre, html: g.html }))));

    console.log(`corpus tiré du site et gardé : ${pages.size} guides`);

    return pages;
}

const regle = regleDuPont();

// La marque d'autrefois : du gras ou une couleur. Elle ne commande plus rien,
// et n'est gardée ici que pour dire d'une seule voix ce que la règle rendait
// avant et ce qu'elle rend maintenant. Voir la reprise de D76.
function marquee(balisage) {
    return /<(strong|b)[\s>]/i.test(balisage)
        || /class="[^"]*has-text-color/.test(balisage)
        || /style="[^"]*color:/.test(balisage);
}

// Le verbe qui a déclenché l'ordre, pour le rapport seulement.
//
// La boucle redit celle de la règle, mais elle emprunte ses trois listes plutôt
// que d'en recopier le contenu : si un faux ami y est ajouté demain, le rapport
// le sait le jour même. Ce nom ne décide de rien, il explique.
function verbeDeclencheur(texte) {
    const mots = texte.match(/[\p{L}\p{M}]+/gu) || [];

    for (let i = 0; i < mots.length; i++) {
        const mot = mots[i].toLowerCase();

        // Le même franchissement des pronoms que la règle. Il a manqué ici une
        // première fois, et le rapport nommait alors un verbe que la règle
        // avait écarté : un instrument qui se trompe est pire que pas
        // d'instrument, puisqu'on le croit.
        let j = i - 1;

        while (j >= 0
            && regle.PRONOMS.indexOf(mots[j].toLowerCase()) >= 0
            && regle.SUJETS.indexOf(mots[j].toLowerCase()) < 0) {
            j--;
        }

        if (regle.SUJETS.indexOf(j >= 0 ? mots[j].toLowerCase() : '') >= 0) { continue; }
        if (regle.IRREGULIERS.indexOf(mot) >= 0) { return mot; }

        if (mot.length >= 4 && mot.slice(-2) === 'ez' && regle.FAUX_AMIS.indexOf(mot) < 0) {
            return mot;
        }
    }

    return '';
}

function etapes(html, exigerLaMarque) {
    const gardees = [];

    for (const bloc of premierNiveau(html)) {
        if (bloc.nom !== 'p') { continue; }
        if (exigerLaMarque && !marquee(bloc.balisage)) { continue; }

        const t = bloc.texte;

        if (t.length === 0 || regle.isNoise(t)) { continue; }

        const coordonnee = regle.COORDONNEES.test(t);
        const ordre = regle.orders(t);

        if (coordonnee || ordre) {
            gardees.push({ texte: t, coordonnee, ordre, verbe: ordre ? verbeDeclencheur(t) : '' });
        }
    }

    return gardees;
}


// Ce que la règle doit dire, sur des paragraphes tirés du site tels quels.
//
// La règle n'avait aucune épreuve : elle est en JavaScript, et le dépôt n'a pas
// de quoi en exécuter dans sa suite. Cette table est le garde-fou qu'on peut
// rejouer d'une commande. Chaque cas vient du corpus, jamais d'une invention :
// une épreuve écrite de mémoire ne protège que de ce qu'on avait en tête.
const CAS = [
    // Du récit, que le garde des sujets doit franchir pour reconnaître.
    [false, 'Jerael prépare le repas avec le bouftou que vous lui avez ramené.'],
    [false, 'Ce dernier vous confie qu\u2019il est dans le donjon, et vous lui faites part '
        + 'du mal être de Tira.'],
    [false, 'Le Baron semble content de vous, il espère tirer quelque chose de la trouvaille. '
        + 'Vous avez su lui montrer que vous n\u2019aviez pas les yeux dans la poche.'],

    // De vrais ordres, que le resserrement ne doit pas emporter.
    [true, 'Rendez-vous en [13,-28] auprès de Capitaine Igloute. Demandez-lui où est passé Tira.'],
    [true, 'Faites le lit dans la première chambre.'],
    [true, 'Remerciez Marc Azin.'],
    [true, 'N\u2019oubliez pas de lui reparler une seconde fois!'],
    [true, 'Venez en à bout pour le ramener au Captain Amakna.'],

    // Une coordonnée suffit : dire où ramasser un objet est bien une étape.
    [true, 'La Page de journal trempée de bave se trouve en [21,8] dans le coffre à côté '
        + 'du bateau.'],

    // Le bruit que le site écrit sans que ce soit une consigne.
    [false, 'Attention : le combat se lance immédiatement, préparez vos sorts.'],
    [false, '(Vous pouvez aussi y aller plus tard, cela ne change rien.)'],
];

function eprouve() {
    const fautes = [];

    for (const [attendu, texte] of CAS) {
        const retenu = texte.length > 0
            && !regle.isNoise(texte)
            && (regle.COORDONNEES.test(texte) || Boolean(regle.orders(texte)));

        if (retenu !== attendu) {
            fautes.push(`${attendu ? 'devrait être' : 'ne devrait pas être'} une étape : `
                + `« ${texte.slice(0, 90)} »`);
        }
    }

    return fautes;
}

const cherche = process.argv[2];
const pages = await guides();

const detail = [];
let total = 0;
let muets = 0;
let totalMarque = 0;
let muetsMarque = 0;
const tailles = [];

for (const { titre, html } of pages.values()) {
    const trouvees = etapes(html, false);
    const avant = etapes(html, true);
    total += trouvees.length;
    totalMarque += avant.length;
    tailles.push(trouvees.length);

    if (trouvees.length === 0) { muets++; }
    if (avant.length === 0) { muetsMarque++; }

    detail.push({ titre, etapes: trouvees });

    if (cherche && pliee(titre).includes(pliee(cherche))) {
        console.log(`\n« ${titre} » : ${trouvees.length} étapes`);
        trouvees.forEach((e, i) => console.log(`   ${i + 1}. ${e.texte.slice(0, 100)}`));
    }
}

writeFileSync(rapport, JSON.stringify(detail, null, 1));

tailles.sort((a, b) => a - b);

console.log(`\nguides            : ${pages.size}`);
console.log(`consignes         : ${total}`);
console.log(`guides sans étape : ${muets}`);
console.log(`  pour mémoire, du temps où le gras était exigé : `
    + `${totalMarque} consignes, ${muetsMarque} guides sans étape`);
console.log(`détail écrit      : ${rapport}`);
console.log(`par guide         : médiane ${tailles[tailles.length >> 1]}, `
    + `90e ${tailles[Math.floor(tailles.length * 0.9)]}, max ${tailles[tailles.length - 1]}`);

const fautes = eprouve();

if (fautes.length > 0) {
    console.error(`\n${fautes.length} cas de la table ne passent plus :`);
    fautes.forEach((f) => console.error(`  ${f}`));
    process.exitCode = 1;
} else {
    console.log(`table de cas      : ${CAS.length} sur ${CAS.length}`);
}
