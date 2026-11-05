// Offers the download that fits the visitor's computer. Windows is what the
// pages say without this script: on a Mac or a Linux PC, the elements marked
// data-pour show or hide (style.css), and the download links that carry
// data-mac-href or data-linux-href, with their data-*-texte, point to that
// system's install instead. A phone or a tablet keeps Windows: DT Hub runs
// on the computer the phone is plugged into. Nothing is stored or sent.
(function () {
  var platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
  var agent = platform + ' ' + navigator.userAgent;
  var touch = navigator.maxTouchPoints > 1;
  var os = '';

  if (/Android|iPhone|iPad|CrOS/i.test(agent)) {
    os = '';
  } else if (/Mac/i.test(agent)) {
    // An iPad asking for the desktop site says "Macintosh"; only it has touch.
    os = touch ? '' : 'mac';
  } else if (/Linux|X11/i.test(agent)) {
    os = 'linux';
  }

  if (os) {
    document.documentElement.setAttribute('data-os', os);
  }

  document.addEventListener('DOMContentLoaded', function () {
    if (os) {
      var liens = document.querySelectorAll('[data-' + os + '-href]');
      for (var i = 0; i < liens.length; i++) {
        var lien = liens[i];
        lien.setAttribute('href', lien.getAttribute('data-' + os + '-href'));
        var texte = lien.getAttribute('data-' + os + '-texte');
        // The icon stays: only the text after it changes.
        if (texte && lien.lastChild && lien.lastChild.nodeType === 3) {
          lien.lastChild.nodeValue = texte;
        }
      }
    }

    // A "Copy" button on each install command, labelled by the page.
    var commandes = document.querySelectorAll('pre[data-copier]');
    for (var j = 0; j < commandes.length; j++) {
      (function (pre) {
        var bouton = document.createElement('button');
        bouton.type = 'button';
        bouton.className = 'copier';
        bouton.textContent = pre.getAttribute('data-copier');
        bouton.addEventListener('click', function () {
          navigator.clipboard.writeText(pre.textContent.trim()).then(function () {
            bouton.textContent = pre.getAttribute('data-copie');
          });
        });
        pre.parentNode.insertBefore(bouton, pre.nextSibling);
      })(commandes[j]);
    }
  });
})();
