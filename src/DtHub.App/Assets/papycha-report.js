// Prepares the site's error-report form inside our window.
//
// The block lives at the bottom of the article, collapsed behind a
// "details" element. Three actions, and no more: open it, put in where we
// were, and show only it.
//
// Nothing is sent, nothing is filled in on the reader's behalf except the
// step marker, which the reader can clear. The site's anti-robot field is
// not touched.
(function () {
    'use strict';

    var bloc = document.getElementById('papycha-report-error');

    if (!bloc) {
        return 'absent';
    }

    bloc.open = true;

    // The step marker, only if the field is empty: returning to the
    // window must not overwrite what the reader has just written.
    var lieu = bloc.querySelector('[name="papycha_report_location"]');

    if (lieu && !lieu.value) {
        lieu.value = __DTHUB_LOCATION__;
    }

    // Show only the block: we climb up to the body, hiding at each level
    // everything not on the path. Hide rather than remove, so as not to
    // break anything the site expects around its form.
    var style = document.createElement('style');

    style.textContent =
        '[data-dthub-report-hidden]{display:none !important}'

        // The levels passed through lose everything that made them wide,
        // decorated or scrollable: the site's layout assumes a full page,
        // and our window is only four hundred pixels wide.
        + '[data-dthub-report-path]{display:block !important;width:auto !important;'
        + 'max-width:none !important;min-width:0 !important;margin:0 !important;'
        + 'padding:0 !important;border:0 !important;background:none !important;'
        + 'box-shadow:none !important;overflow:visible !important;float:none !important;'
        + 'position:static !important;transform:none !important}'

        // The tint is that of our panels; the form keeps its own colors,
        // which the site gives as light on dark.
        // The article's background is a full-page photo, and only one
        // element should scroll: two scrollbars used to appear side by
        // side, the document's and that of a level passed through.
        + 'html{background:#12141a !important;height:100% !important;'
        + 'overflow:hidden !important;margin:0 !important;padding:0 !important}'
        + 'body{background:#12141a !important;height:100% !important;'
        + 'overflow-x:hidden !important;overflow-y:auto !important;'
        + 'margin:0 !important;padding:0 !important;width:auto !important;'
        + 'max-width:none !important}'

        + '#papycha-report-error{margin:14px !important;overflow:visible !important}'

        // The summary stays: it is the only way to collapse and reopen
        // the block, and hiding it had removed that. It is brought back
        // to the left, the site aligning it to the right to end a
        // metadata line; alone, it hung outside the frame. It keeps its
        // underline, which signals that it can be clicked.
        + '#papycha-report-error > summary{margin:0 0 12px 0 !important;'
        + 'width:auto !important;text-align:left !important;font-size:1rem !important}'

        // The site's theme underlines a label that has focus, and the
        // decoration propagates to everything it contains: clicking in a
        // field used to underline its label and its value. We cut it off
        // where it originates, on the label itself, otherwise it passes
        // through its descendants without them being able to shed it.
        + '#papycha-report-error label,#papycha-report-error label:focus,'
        + '#papycha-report-error label.focus,#papycha-report-error fieldset,'
        + '#papycha-report-error legend{text-decoration:none !important}'

        // Nothing in the form overflows: the fields are sized for an
        // article column, and used to overflow the window on the right. A
        // maximum width keeps the fields readable on a wide screen, where
        // they used to stretch across the whole window.
        + '#papycha-report-error,#papycha-report-error *{max-width:100% !important;'
        + 'box-sizing:border-box !important}'
        + '#papycha-report-error{max-width:560px !important;padding-bottom:20px !important}'

        // The text area can no longer be resized by dragging: the form
        // scrolls, it does not resize itself. Its height follows the
        // window's, with two bounds, so it fits as well on a laptop as on
        // a large screen. As tall as the site gives it, it used to push
        // the submit button below the bottom edge, where it could no
        // longer be found.
        + '#papycha-report-error textarea{resize:none !important;'
        + 'height:20vh !important;min-height:100px !important;max-height:240px !important}'

        // The site's submit button is invisible: its stylesheet gives it
        // "background: currentColor" and "color: Canvas", so that the
        // background takes the button's own text color, that is itself.
        // We give it back the two colors it was aiming for, in the same
        // system-color vocabulary, so it follows the light or dark theme.
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

    // The height the window would need to show the whole form, margins
    // included. The window sizes itself to it, bounded by the screen: it
    // has only this form to show, and nothing justifies cutting it off
    // when the room exists.
    return 'pret ' + Math.ceil(bloc.getBoundingClientRect().height + 28);
})();
