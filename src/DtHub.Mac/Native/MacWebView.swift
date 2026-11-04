// The web view of the guides, the Almanax and the web tools: a WKWebView,
// driven from .NET (Views/Web/MacWebView.cs) through plain C functions.
// It gives what the PC takes from WebView2: scripts injected before the
// page's own, messages from the page, the main frame's navigations to
// accept or refuse, their end, success or not, new windows, the address
// as the page changes it, scripts run on demand, cookies, and ad hosts
// refused before any request leaves.
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
}

/// The name the pages post to, behind the PC's window.chrome.webview.
private let messageName = "dthub"

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

private final class Host: NSObject, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler {
    let callback: DthubWebEvent
    let context: UnsafeMutableRawPointer?
    let view: WKWebView
    private var nextNavigation: Int64 = 0
    private var navigations: [ObjectIdentifier: Int64] = [:]
    private var allowed: Int64 = 0
    private var observation: NSKeyValueObservation?

    init(callback: @escaping DthubWebEvent, context: UnsafeMutableRawPointer?) {
        self.callback = callback
        self.context = context

        let configuration = WKWebViewConfiguration()
        // One store for every view, as the PC's one profile: the tracker
        // cookie set in one window is read in the others.
        configuration.websiteDataStore = .default()
        configuration.userContentController.addUserScript(
            WKUserScript(source: chromeShim, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        view = WKWebView(frame: NSRect(x: 0, y: 0, width: 400, height: 300), configuration: configuration)
        super.init()

        configuration.userContentController.add(WeakHandler(self), name: messageName)
        view.navigationDelegate = self
        view.uiDelegate = self
        view.allowsMagnification = true
        view.allowsBackForwardNavigationGestures = false
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
        // No target: a new window, said by createWebViewWith.
        guard let frame = action.targetFrame, frame.isMainFrame else {
            decisionHandler(.allow)
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

    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        if let navigation {
            navigations[ObjectIdentifier(navigation)] = allowed
        }
    }

    private func finished(_ navigation: WKNavigation?, _ error: Error?) {
        var id = allowed
        if let navigation, let known = navigations.removeValue(forKey: ObjectIdentifier(navigation)) {
            id = known
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

/// The address shown, copied into the buffer; false when there is none.
@_cdecl("dthub_webview_source")
public func dthubWebViewSource(_ pointer: UnsafeMutableRawPointer?, _ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) -> Bool {
    guard let url = host(pointer)?.view.url?.absoluteString, let buffer, capacity > 0 else { return false }
    let bytes = Array(url.utf8)
    guard bytes.count < Int(capacity) else { return false }
    for (index, byte) in bytes.enumerated() { buffer[index] = CChar(bitPattern: byte) }
    buffer[bytes.count] = 0
    return true
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

/// The language the pages hear (navigator.language, Accept-Language): DT
/// Hub's, as the PC's --accept-lang, for this process only and before the
/// first view. Read by WebKit from the process's preferred languages.
@_cdecl("dthub_webview_set_language")
public func dthubWebViewSetLanguage(_ language: UnsafePointer<CChar>?) {
    UserDefaults.standard.setVolatileDomain(["AppleLanguages": [string(language)]], forName: UserDefaults.argumentDomain)
}
