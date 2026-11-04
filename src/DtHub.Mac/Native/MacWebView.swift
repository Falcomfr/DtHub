// The web view of the guides, the Almanax and the web tools: a WKWebView,
// driven from .NET (Views/Web/MacWebView.cs) through plain C functions.
// It gives what the PC takes from WebView2: scripts injected before the
// page's own, messages from the page, the main frame's navigations to
// accept or refuse, their end, success or not, new windows, the address
// as the page changes it, scripts run on demand, cookies, and ad hosts
// refused before any request leaves. And what WebView2 does on its own:
// the page's alert, confirm and prompt, its file pickers, downloads, the
// history, and the mouse's back and forward buttons.
//
// Everything the view says goes through one C callback, with the context
// .NET gave at creation, on the main thread, which is Avalonia's.

import AppKit
import WebKit

/// (context, kind, text, number) -> 1 to refuse, for a navigation starting.
public typealias DthubWebEvent = @convention(c) (UnsafeMutableRawPointer?, Int32, UnsafePointer<CChar>?, Int64) -> Int32

private enum Kind: Int32 {
    case navigationStarting = 1
    case navigationCompleted = 2
    case message = 3
    case newWindow = 4
    case sourceChanged = 5
    case scriptResult = 6
    case scriptFailed = 7
    case cookie = 8
    case rulesReady = 9
    case redirectStarting = 10
    case download = 11
    case pressed = 12
}

/// The name the pages post to, behind the PC's window.chrome.webview.
private let messageName = "dthub"

/// The buttons of the page's dialogs, in DT Hub's language: given by .NET.
private var okLabel = "OK"
private var cancelLabel = "Cancel"

/// window.chrome.webview.postMessage, as WebView2 gives it to the top
/// document only, so the PC's scripts post unchanged.
private let chromeShim = """
    (function () {
      if (window !== window.top || !window.webkit || !window.webkit.messageHandlers.\(messageName)) return;
      window.chrome = window.chrome || {};
      if (window.chrome.webview) return;
      window.chrome.webview = {
        postMessage: function (m) { window.webkit.messageHandlers.\(messageName).postMessage(typeof m === 'string' ? m : JSON.stringify(m)); }
      };
    })();
    """

/// The WKWebView, with what WebView2 does by itself: the mouse's back and
/// forward buttons move in the history. A press is said first, for the
/// window's popups to close as on a click anywhere else in it.
private final class DthubWebView: WKWebView {
    var pressed: (() -> Void)?

    /// The keyboard asked for before the view was in a window: given once it is.
    var wantsFocus = false

    override func mouseDown(with event: NSEvent) {
        pressed?()
        super.mouseDown(with: event)
    }

    override func rightMouseDown(with event: NSEvent) {
        pressed?()
        super.rightMouseDown(with: event)
    }

    override func otherMouseUp(with event: NSEvent) {
        switch event.buttonNumber {
        case 3: goBack()
        case 4: goForward()
        default: super.otherMouseUp(with: event)
        }
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if wantsFocus, let window {
            wantsFocus = false
            window.makeFirstResponder(self)
        }
    }
}

private final class Host: NSObject, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler, WKDownloadDelegate {
    let callback: DthubWebEvent
    let context: UnsafeMutableRawPointer?
    let view: DthubWebView
    private var nextNavigation: Int64 = 0
    private var navigations: [ObjectIdentifier: Int64] = [:]
    private var allowed: Int64 = 0
    /// The main frame's navigation not committed yet: a server redirect
    /// keeps its number, as WebView2's NavigationId does.
    private var provisional: Int64?
    private var observation: NSKeyValueObservation?
    /// The downloads under way, with the file each one writes.
    private var downloads: [ObjectIdentifier: URL] = [:]
    /// The answers the page still waits for: given by default on close,
    /// WebKit requiring every completion handler to be called.
    private var pendingAnswers: [Int: () -> Void] = [:]
    private var nextAnswer = 0

    init(callback: @escaping DthubWebEvent, context: UnsafeMutableRawPointer?) {
        self.callback = callback
        self.context = context

        let configuration = WKWebViewConfiguration()
        // One store for every view, as the PC's one profile: the tracker
        // cookie set in one window is read in the others.
        configuration.websiteDataStore = .default()
        configuration.userContentController.addUserScript(
            WKUserScript(source: chromeShim, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        view = DthubWebView(frame: NSRect(x: 0, y: 0, width: 400, height: 300), configuration: configuration)
        super.init()

        configuration.userContentController.add(WeakHandler(self), name: messageName)
        view.navigationDelegate = self
        view.uiDelegate = self
        view.allowsMagnification = true
        view.allowsBackForwardNavigationGestures = false
        view.pressed = { [weak self] in self?.send(.pressed, nil, 0) }
        // The window's colour shows until the page paints, not white.
        view.setValue(false, forKey: "drawsBackground")
        observation = view.observe(\.url, options: [.new]) { [weak self] web, _ in
            self?.send(.sourceChanged, web.url?.absoluteString, 0)
        }
    }

    @discardableResult
    func send(_ kind: Kind, _ text: String?, _ number: Int64) -> Int32 {
        guard let text else { return callback(context, kind.rawValue, nil, number) }
        return text.withCString { callback(context, kind.rawValue, $0, number) }
    }

    func close() {
        observation = nil
        view.pressed = nil

        // A dialog still open is answered as dismissed, and its sheet ended.
        let answers = Array(pendingAnswers.values)
        pendingAnswers.removeAll()
        answers.forEach { $0() }
        if let window = view.window {
            window.sheets.forEach { window.endSheet($0) }
        }
        downloads.removeAll()

        view.stopLoading()
        view.navigationDelegate = nil
        view.uiDelegate = nil
        view.configuration.userContentController.removeScriptMessageHandler(forName: messageName)
        view.configuration.userContentController.removeAllUserScripts()
        view.removeFromSuperview()
    }

    // MARK: Navigation, the main frame's only: a frame inside the page is
    // the page's business, as WebView2's NavigationStarting leaves it.

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        // A link marked "download", a file a tool exports: saved, never
        // shown, nor handed to the browser as a navigation would be.
        if action.shouldPerformDownload {
            decisionHandler(.download)
            return
        }

        // No target: a new window, said by createWebViewWith.
        guard let frame = action.targetFrame, frame.isMainFrame else {
            decisionHandler(.allow)
            return
        }

        // A server redirect is asked about too, before
        // didReceiveServerRedirect (measured): the same navigation, under
        // the same number, so that its end is the one awaited, and the
        // rules on links apply to where it leads.
        if let id = provisional, Host.isRedirect(action) {
            let refused = send(.redirectStarting, action.request.url?.absoluteString ?? "", id) != 0
            decisionHandler(refused ? .cancel : .allow)
            return
        }

        nextNavigation += 1
        let id = nextNavigation
        let refused = send(.navigationStarting, action.request.url?.absoluteString ?? "", id) != 0

        if !refused {
            allowed = id
        }

        decisionHandler(refused ? .cancel : .allow)
    }

    /// WebKit's own word (_isRedirect, measured on macOS 26), else a
    /// navigation of no kind while another one is still pending.
    private static func isRedirect(_ action: WKNavigationAction) -> Bool {
        if action.responds(to: Selector(("_isRedirect"))), let flag = action.value(forKey: "_isRedirect") as? Bool {
            return flag
        }
        return action.navigationType == .other
    }

    /// What WebKit cannot show, or is told to save, becomes a download.
    func webView(_ webView: WKWebView, decidePolicyFor response: WKNavigationResponse,
                 decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        let disposition = (response.response as? HTTPURLResponse)?
            .value(forHTTPHeaderField: "Content-Disposition")?.lowercased() ?? ""
        decisionHandler(!response.canShowMIMEType || disposition.hasPrefix("attachment") ? .download : .allow)
    }

    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        if let navigation {
            navigations[ObjectIdentifier(navigation)] = allowed
        }
        provisional = allowed
    }

    func webView(_ webView: WKWebView, didCommit navigation: WKNavigation!) {
        provisional = nil
    }

    private func finished(_ navigation: WKNavigation?, _ error: Error?) {
        var id = allowed
        if let navigation, let known = navigations.removeValue(forKey: ObjectIdentifier(navigation)) {
            id = known
        }
        if provisional == id {
            provisional = nil
        }

        // "1" on success, else "0 domain code description".
        if let error = error as NSError? {
            send(.navigationCompleted, "0 \(error.domain) \(error.code) \(error.localizedDescription)", id)
        } else {
            send(.navigationCompleted, "1", id)
        }
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        finished(navigation, nil)
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        finished(navigation, error)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        finished(navigation, error)
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        provisional = nil
        send(.navigationCompleted, "0 WebKit -1 web content process ended", allowed)
    }

    // MARK: New windows: never opened here; .NET decides where they go.

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for action: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let url = action.request.url?.absoluteString, !url.isEmpty {
            send(.newWindow, url, 0)
        }
        return nil
    }

    // MARK: Downloads: where to is asked, the Downloads folder offered.

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) {
        download.delegate = self
    }

    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) {
        download.delegate = self
    }

    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse,
                  suggestedFilename: String, completionHandler: @escaping (URL?) -> Void) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = suggestedFilename
        panel.directoryURL = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
        panel.canCreateDirectories = true

        let answer = once(completionHandler, nil)
        present(panel) { [weak self] chosen in
            guard chosen, let url = panel.url else {
                answer(nil)
                return
            }
            // The panel asked before replacing; WebKit refuses a file
            // that is already there.
            try? FileManager.default.removeItem(at: url)
            self?.downloads[ObjectIdentifier(download)] = url
            answer(url)
        }
    }

    func downloadDidFinish(_ download: WKDownload) {
        let url = downloads.removeValue(forKey: ObjectIdentifier(download))
        send(.download, url?.path ?? "", 1)
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        downloads.removeValue(forKey: ObjectIdentifier(download))
        send(.download, error.localizedDescription, 0)
    }

    // MARK: The page's dialogs and file pickers, as sheets on its window.

    /// The handler, called once, whoever comes first: the answer, or the
    /// default given when the view closes.
    private func once<T>(_ handler: @escaping (T) -> Void, _ fallback: T) -> (T) -> Void {
        nextAnswer += 1
        let key = nextAnswer
        var done = false
        let answer: (T) -> Void = { [weak self] value in
            guard !done else { return }
            done = true
            self?.pendingAnswers.removeValue(forKey: key)
            handler(value)
        }
        pendingAnswers[key] = { answer(fallback) }
        return answer
    }

    /// A sheet on the view's window, or a modal panel when it has none.
    private func present(_ panel: NSSavePanel, _ done: @escaping (Bool) -> Void) {
        if let window = view.window {
            panel.beginSheetModal(for: window) { done($0 == .OK) }
        } else {
            done(panel.runModal() == .OK)
        }
    }

    private func present(_ alert: NSAlert, _ done: @escaping (NSApplication.ModalResponse) -> Void) {
        if let window = view.window {
            alert.beginSheetModal(for: window, completionHandler: done)
        } else {
            done(alert.runModal())
        }
    }

    /// Titled by the site that asks, as browsers do.
    private func alert(_ message: String, _ frame: WKFrameInfo, _ buttons: [String]) -> NSAlert {
        let alert = NSAlert()
        alert.messageText = frame.securityOrigin.host
        alert.informativeText = message
        buttons.forEach { alert.addButton(withTitle: $0) }
        // Escape cancels, whatever the language calls the button.
        if alert.buttons.count > 1 {
            alert.buttons[1].keyEquivalent = "\u{1b}"
        }
        return alert
    }

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        let answer = once({ (_: Bool) in completionHandler() }, false)
        present(alert(message, frame, [okLabel])) { _ in answer(true) }
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) {
        let answer = once(completionHandler, false)
        present(alert(message, frame, [okLabel, cancelLabel])) { answer($0 == .alertFirstButtonReturn) }
    }

    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String,
                 defaultText: String?, initiatedByFrame frame: WKFrameInfo,
                 completionHandler: @escaping (String?) -> Void) {
        let answer = once(completionHandler, nil)
        let dialog = alert(prompt, frame, [okLabel, cancelLabel])
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 280, height: 24))
        field.stringValue = defaultText ?? ""
        dialog.accessoryView = field
        dialog.window.initialFirstResponder = field
        present(dialog) { answer($0 == .alertFirstButtonReturn ? field.stringValue : nil) }
    }

    func webView(_ webView: WKWebView, runOpenPanelWith parameters: WKOpenPanelParameters,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping ([URL]?) -> Void) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = parameters.allowsDirectories
        panel.allowsMultipleSelection = parameters.allowsMultipleSelection
        let answer = once(completionHandler, nil)
        present(panel) { chosen in answer(chosen ? panel.urls : nil) }
    }

    // MARK: Messages

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        // Text only, as the PC's TryGetWebMessageAsString: anything else
        // a page posts is not ours.
        if message.frameInfo.isMainFrame, let text = message.body as? String {
            send(.message, text, 0)
        }
    }
}

/// The controller keeps its handlers: through this, not the host itself,
/// or the host would never be freed.
private final class WeakHandler: NSObject, WKScriptMessageHandler {
    weak var target: Host?

    init(_ target: Host) { self.target = target }

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        target?.userContentController(controller, didReceive: message)
    }
}

private func host(_ pointer: UnsafeMutableRawPointer?) -> Host? {
    pointer.map { Unmanaged<Host>.fromOpaque($0).takeUnretainedValue() }
}

private func string(_ text: UnsafePointer<CChar>?) -> String {
    text.map { String(cString: $0) } ?? ""
}

/// A result as WebView2 gives it: JSON, "null" when there is none.
private func json(_ value: Any?) -> String {
    guard let value, !(value is NSNull) else { return "null" }
    if let data = try? JSONSerialization.data(withJSONObject: value, options: [.fragmentsAllowed]),
       let text = String(data: data, encoding: .utf8) {
        return text
    }
    return "null"
}

/// A new view and its host, retained until dthub_webview_destroy.
@_cdecl("dthub_webview_create")
public func dthubWebViewCreate(_ callback: DthubWebEvent, _ context: UnsafeMutableRawPointer?) -> UnsafeMutableRawPointer? {
    Unmanaged.passRetained(Host(callback: callback, context: context)).toOpaque()
}

/// The NSView to put in the window.
@_cdecl("dthub_webview_view")
public func dthubWebViewView(_ pointer: UnsafeMutableRawPointer?) -> UnsafeMutableRawPointer? {
    host(pointer).map { Unmanaged.passUnretained($0.view).toOpaque() }
}

@_cdecl("dthub_webview_destroy")
public func dthubWebViewDestroy(_ pointer: UnsafeMutableRawPointer?) {
    guard let pointer else { return }
    let unmanaged = Unmanaged<Host>.fromOpaque(pointer)
    unmanaged.takeUnretainedValue().close()
    unmanaged.release()
}

/// A script run before the page's own, on every document from now on.
@_cdecl("dthub_webview_add_script")
public func dthubWebViewAddScript(_ pointer: UnsafeMutableRawPointer?, _ source: UnsafePointer<CChar>?, _ mainFrameOnly: Bool) {
    host(pointer)?.view.configuration.userContentController.addUserScript(
        WKUserScript(source: string(source), injectionTime: .atDocumentStart, forMainFrameOnly: mainFrameOnly))
}

@_cdecl("dthub_webview_navigate")
public func dthubWebViewNavigate(_ pointer: UnsafeMutableRawPointer?, _ address: UnsafePointer<CChar>?) -> Bool {
    guard let host = host(pointer), let url = URL(string: string(address)) else { return false }
    host.view.load(URLRequest(url: url))
    return true
}

/// The address shown: its length in UTF-8 bytes, -1 when there is none.
/// Copied into the buffer, ended by a zero, only when it fits: a longer
/// one is asked again with the room it needs.
@_cdecl("dthub_webview_source")
public func dthubWebViewSource(_ pointer: UnsafeMutableRawPointer?, _ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) -> Int32 {
    guard let url = host(pointer)?.view.url?.absoluteString else { return -1 }
    let bytes = Array(url.utf8)
    if let buffer, bytes.count < Int(capacity) {
        for (index, byte) in bytes.enumerated() { buffer[index] = CChar(bitPattern: byte) }
        buffer[bytes.count] = 0
    }
    return Int32(clamping: bytes.count)
}

/// Runs a script; its JSON result, or its error, comes back as an event
/// carrying the request number.
@_cdecl("dthub_webview_eval")
public func dthubWebViewEval(_ pointer: UnsafeMutableRawPointer?, _ script: UnsafePointer<CChar>?, _ request: Int64) {
    guard let host = host(pointer) else { return }
    host.view.evaluateJavaScript(string(script)) { [weak host] value, error in
        guard let host else { return }
        if let error {
            host.send(.scriptFailed, error.localizedDescription, request)
        } else {
            host.send(.scriptResult, json(value), request)
        }
    }
}

/// The value of a cookie for an address's host (its parent domains
/// included), as an event carrying the request number; no text when none.
@_cdecl("dthub_webview_cookie")
public func dthubWebViewCookie(_ pointer: UnsafeMutableRawPointer?, _ name: UnsafePointer<CChar>?,
                               _ address: UnsafePointer<CChar>?, _ request: Int64) {
    guard let host = host(pointer) else { return }
    let wanted = string(name)
    let site = URL(string: string(address))?.host?.lowercased() ?? ""
    host.view.configuration.websiteDataStore.httpCookieStore.getAllCookies { [weak host] cookies in
        let found = cookies.first { cookie in
            let domain = cookie.domain.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
            return cookie.name == wanted && (site == domain || site.hasSuffix("." + domain))
        }
        host?.send(.cookie, found?.value, request)
    }
}

/// Deletes a cookie for an address's host, wherever it was set for it.
@_cdecl("dthub_webview_delete_cookie")
public func dthubWebViewDeleteCookie(_ pointer: UnsafeMutableRawPointer?, _ name: UnsafePointer<CChar>?, _ address: UnsafePointer<CChar>?) {
    guard let host = host(pointer) else { return }
    let wanted = string(name)
    let site = URL(string: string(address))?.host?.lowercased() ?? ""
    let store = host.view.configuration.websiteDataStore.httpCookieStore
    store.getAllCookies { cookies in
        for cookie in cookies where cookie.name == wanted {
            let domain = cookie.domain.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: "."))
            if site == domain || site.hasSuffix("." + domain) {
                store.delete(cookie)
            }
        }
    }
}

/// The content rules compiled once per process, from the WebKit rule list
/// JSON .NET gives: requests to the ad hosts never leave.
private var compiledRules: [String: WKContentRuleList] = [:]

/// Applies a content rule list, compiling it the first time; the view's
/// host hears "rulesReady" (number 1, or 0 when it did not compile) and
/// only then navigates.
@_cdecl("dthub_webview_block")
public func dthubWebViewBlock(_ pointer: UnsafeMutableRawPointer?, _ identifier: UnsafePointer<CChar>?, _ rules: UnsafePointer<CChar>?) {
    guard let host = host(pointer) else { return }
    let name = string(identifier)

    if let list = compiledRules[name] {
        host.view.configuration.userContentController.add(list)
        host.send(.rulesReady, nil, 1)
        return
    }

    WKContentRuleListStore.default().compileContentRuleList(
        forIdentifier: name, encodedContentRuleList: string(rules)
    ) { [weak host] list, _ in
        if let list {
            compiledRules[name] = list
            host?.view.configuration.userContentController.add(list)
        }
        host?.send(.rulesReady, nil, list == nil ? 0 : 1)
    }
}

/// The page's zoom, 1 being the site's own size.
@_cdecl("dthub_webview_set_zoom")
public func dthubWebViewSetZoom(_ pointer: UnsafeMutableRawPointer?, _ zoom: Double) {
    host(pointer)?.view.pageZoom = CGFloat(zoom)
}

/// The history: -1 back, 1 forward, 0 reloads.
@_cdecl("dthub_webview_go")
public func dthubWebViewGo(_ pointer: UnsafeMutableRawPointer?, _ step: Int32) {
    guard let view = host(pointer)?.view else { return }
    switch step {
    case ..<0: view.goBack()
    case 1...: view.goForward()
    default: view.reload()
    }
}

/// The keyboard to the page: its view becomes the window's first
/// responder, now or once it is in a window.
@_cdecl("dthub_webview_focus")
public func dthubWebViewFocus(_ pointer: UnsafeMutableRawPointer?) {
    guard let view = host(pointer)?.view else { return }
    if let window = view.window {
        window.makeFirstResponder(view)
    } else {
        view.wantsFocus = true
    }
}

/// The buttons of the page's dialogs, in DT Hub's language.
@_cdecl("dthub_webview_set_labels")
public func dthubWebViewSetLabels(_ ok: UnsafePointer<CChar>?, _ cancel: UnsafePointer<CChar>?) {
    okLabel = string(ok)
    cancelLabel = string(cancel)
}

/// An Edit menu action (copy:, paste:, undo:...) for the page that has
/// the keyboard, sent down the responder chain as AppKit's own Edit menu
/// does. False, and nothing sent, when the key window's first responder is
/// not a web view: the caller gives it to its own fields.
@_cdecl("dthub_webview_edit_action")
public func dthubWebViewEditAction(_ selector: UnsafePointer<CChar>?) -> Bool {
    var responder = NSApp.keyWindow?.firstResponder as? NSView
    while let view = responder, !(view is WKWebView) {
        responder = view.superview
    }
    guard responder != nil else { return false }
    _ = NSApp.sendAction(Selector(string(selector)), to: nil, from: nil)
    return true
}

/// The language the pages hear (navigator.language, Accept-Language): DT
/// Hub's, as the PC's --accept-lang, for this process only and before the
/// first view. Read by WebKit from the process's preferred languages.
@_cdecl("dthub_webview_set_language")
public func dthubWebViewSetLanguage(_ language: UnsafePointer<CChar>?) {
    UserDefaults.standard.setVolatileDomain(["AppleLanguages": [string(language)]], forName: UserDefaults.argumentDomain)
}
