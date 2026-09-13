// Development probe: what the bridge keeps as steps, across the site's 782
// guides.
//
// It is not a port. It cuts out of quest-bridge.js the block that carries
// the rule and runs it as is: what it measures is therefore exactly what
// the window will display, and not a restatement that could diverge. This
// is what the D76 audit lacked, whose numbers could never be replayed.
//
// Node is used for development only. The application does not depend on
// it, and nothing in this file is embedded.
//
// The corpus is cached on the first run, and read back afterwards: the
// rule is tuned through successive trials, and redoing eight requests to
// the site on every trial would mean paying the network for nothing.
// "--relire" forces a fresh fetch online.
//
// The detail goes into etapes.json, one line per retained step with its
// guide, its full text and what retained it. D76 regrets not having done
// this: its 3 293 steps could never be found again.
//
//   node build/sonde-papycha/audit-etapes.mjs
//   node build/sonde-papycha/audit-etapes.mjs "découverte d'un destin"
//   node build/sonde-papycha/audit-etapes.mjs --relire
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const ici = dirname(fileURLToPath(import.meta.url));
const pont = join(ici, '..', '..', 'src', 'DtHub.App', 'Assets', 'quest-bridge.js');

// The rule, taken from the bridge between its two stable boundaries.
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

// The content's direct children, with their text.
//
// An explicit stack, not a depth counter: the site leaves stray orphan
// closing tags lying around, and a simple counter used to fall to zero
// halfway through the document, which made everything after that pass for
// top level. The flaw is silent and skews every count.
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
            while (pile.length && pile.pop() !== nom) { /* unwind */ }
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

// The site renders its titles with entities: "d&rsquo;un" and not "d'un".
// Without this, searching by title would never find anything.
function lisible(titre) {
    return titre
        .replace(/&(?:rsquo|#8217|#x2019);/g, '\u2019')
        .replace(/&(?:lsquo|#8216);/g, '\u2018')
        .replace(/&(?:nbsp|#160);/g, ' ')
        .replace(/&(?:amp|#38);/g, '&')
        .replace(/&(?:hellip|#8230);/g, '\u2026')
        .replace(/&(?:quot|#34);/g, '"');
}

// Typographic and straight apostrophes must match each other, otherwise
// searching for "d'un destin" would not find "d\u2019un destin".
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

// The old marker: bold or a color. It no longer controls anything, and is
// kept here only to state in one voice what the rule used to produce
// before and what it produces now. See the D76 rerun.
function marquee(balisage) {
    return /<(strong|b)[\s>]/i.test(balisage)
        || /class="[^"]*has-text-color/.test(balisage)
        || /style="[^"]*color:/.test(balisage);
}

// The verb that triggered the order, for the report only.
//
// The loop restates the rule's own loop, but it borrows its three lists
// rather than copying their contents: if a false friend is added to them
// tomorrow, the report knows it the same day. This name decides nothing,
// it explains.
function verbeDeclencheur(texte) {
    const mots = texte.match(/[\p{L}\p{M}]+/gu) || [];

    for (let i = 0; i < mots.length; i++) {
        const mot = mots[i].toLowerCase();

        // The same pronoun crossing as the rule. It was missing here once
        // before, and the report then named a verb the rule had
        // discarded: an instrument that is wrong is worse than no
        // instrument at all, since it is believed.
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


// What the rule must say, on paragraphs taken from the site as is.
//
// The rule had no test: it is written in JavaScript, and the repository
// has nothing to run it in its test suite. This table is the safety net
// that can be replayed with one command. Every case comes from the
// corpus, never from invention: a test written from memory only protects
// against what was already in mind.
const CAS = [
    // Narration, which the subject guard must cross over to recognize.
    [false, 'Jerael prépare le repas avec le bouftou que vous lui avez ramené.'],
    [false, 'Ce dernier vous confie qu\u2019il est dans le donjon, et vous lui faites part '
        + 'du mal être de Tira.'],
    [false, 'Le Baron semble content de vous, il espère tirer quelque chose de la trouvaille. '
        + 'Vous avez su lui montrer que vous n\u2019aviez pas les yeux dans la poche.'],

    // Genuine orders, which the narrowing must not sweep away.
    [true, 'Rendez-vous en [13,-28] auprès de Capitaine Igloute. Demandez-lui où est passé Tira.'],
    [true, 'Faites le lit dans la première chambre.'],
    [true, 'Remerciez Marc Azin.'],
    [true, 'N\u2019oubliez pas de lui reparler une seconde fois!'],
    [true, 'Venez en à bout pour le ramener au Captain Amakna.'],

    // A coordinate alone is enough: saying where to pick up an object is
    // indeed a step.
    [true, 'La Page de journal trempée de bave se trouve en [21,8] dans le coffre à côté '
        + 'du bateau.'],

    // The noise the site writes without it being an instruction.
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
