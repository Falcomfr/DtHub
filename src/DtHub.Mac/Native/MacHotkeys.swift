// The global shortcuts and the watch on the frontmost window, for
// Platform/MacHotkeyRegistrar.cs. Every function here runs on the main
// thread: the registrar calls them through Avalonia's UI thread, which is
// the main thread on macOS, and the events come back on it.
//
// The shortcuts are Carbon's RegisterEventHotKey, the macOS twin of the
// PC's RegisterHotKey: only the declared combinations are delivered, no
// other keystroke is seen, and it needs no Accessibility permission.
//
// The frontmost window is followed by notifications, not by polling: the
// active application changing (NSWorkspace), and one of our own windows
// becoming key, which macOS does not report as an application change.
// Every scrcpy window is its own process, so going from one game window
// to another is an application change too.
//
// A mouse press in another application's window is seen by a global
// monitor, which macOS allows without permission for the mouse (unlike
// the keyboard); only the window under the pointer is passed on.

import AppKit
import Carbon.HIToolbox

/// kind 1: shortcut pressed (value: its id); 2: frontmost window changed
/// (value: its CGWindowID, 0 if unknown); 3: mouse pressed in another
/// application's window (value: its CGWindowID).
public typealias DthubHotkeyCallback = @convention(c) (Int32, Int64) -> Void

private var callback: DthubHotkeyCallback?
private var handler: EventHandlerRef?
private var hotkeys: [Int32: EventHotKeyRef] = [:]
private var observers: [(NotificationCenter, NSObjectProtocol)] = []
private var mouseMonitor: Any?

/// The signature Carbon files our shortcuts under: "DTHb".
private let signature: OSType = 0x4454_4862

/// The frontmost window of an application: its focused one when the
/// Accessibility permission is granted, else its first normal window
/// from the front, which needs no permission.
private func frontWindow(of pid: pid_t) -> Int64 {
    if AXIsProcessTrusted() {
        let app = AXUIElementCreateApplication(pid)
        var focused: AnyObject?
        if AXUIElementCopyAttributeValue(app, kAXFocusedWindowAttribute as CFString, &focused) == .success,
           let focused {
            var id: CGWindowID = 0
            if hotkeyAxGetWindow(focused as! AXUIElement, &id) == .success && id != 0 { return Int64(id) }
        }
    }
    let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
        as? [[String: Any]] ?? []
    for info in list {
        guard (info[kCGWindowOwnerPID as String] as? Int).map({ pid_t($0) }) == pid,
              (info[kCGWindowLayer as String] as? Int) == 0,
              let id = info[kCGWindowNumber as String] as? Int else { continue }
        return Int64(id)
    }
    return 0
}

@_silgen_name("_AXUIElementGetWindow")
private func hotkeyAxGetWindow(_ element: AXUIElement, _ id: UnsafeMutablePointer<CGWindowID>) -> AXError

/// Installs the shortcut handler and the watches. Called once.
@_cdecl("dthub_hotkeys_start")
public func dthubHotkeysStart(_ onEvent: DthubHotkeyCallback?) -> Bool {
    guard handler == nil else { return true }
    callback = onEvent

    var type = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
    let status = InstallEventHandler(GetEventDispatcherTarget(), { _, event, _ in
        var id = EventHotKeyID()
        let read = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                     nil, MemoryLayout<EventHotKeyID>.size, nil, &id)
        if read == noErr && id.signature == signature { callback?(1, Int64(id.id)) }
        return noErr
    }, 1, &type, nil, &handler)
    guard status == noErr else { return false }

    let workspace = NSWorkspace.shared.notificationCenter
    observers.append((workspace, workspace.addObserver(
        forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { note in
        let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
        callback?(2, app.map { frontWindow(of: $0.processIdentifier) } ?? 0)
    }))

    // Our own windows: the hotkey editor taking the keyboard from the
    // panel must be seen, as Windows reports it on the PC.
    let local = NotificationCenter.default
    observers.append((local, local.addObserver(
        forName: NSWindow.didBecomeKeyNotification, object: nil, queue: .main) { note in
        if let window = note.object as? NSWindow { callback?(2, Int64(window.windowNumber)) }
    }))

    mouseMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { _ in
        let number = NSWindow.windowNumber(at: NSEvent.mouseLocation, belowWindowWithWindowNumber: 0)
        if number > 0 { callback?(3, Int64(number)) }
    }
    return true
}

/// Registers one shortcut: a macOS key code and Carbon modifiers
/// (cmdKey, shiftKey, optionKey, controlKey). Returns Carbon's status, 0
/// when accepted.
@_cdecl("dthub_hotkey_register")
public func dthubHotkeyRegister(_ id: Int32, _ keyCode: UInt32, _ modifiers: UInt32) -> Int32 {
    var ref: EventHotKeyRef?
    let status = RegisterEventHotKey(keyCode, modifiers, EventHotKeyID(signature: signature, id: UInt32(id)),
                                     GetEventDispatcherTarget(), 0, &ref)
    if status == noErr, let ref { hotkeys[id] = ref }
    return status
}

/// Unregisters every shortcut.
@_cdecl("dthub_hotkeys_clear")
public func dthubHotkeysClear() {
    for ref in hotkeys.values { UnregisterEventHotKey(ref) }
    hotkeys.removeAll()
}

/// Unregisters everything and removes the watches.
@_cdecl("dthub_hotkeys_stop")
public func dthubHotkeysStop() {
    dthubHotkeysClear()
    for (center, observer) in observers { center.removeObserver(observer) }
    observers.removeAll()
    if let mouseMonitor { NSEvent.removeMonitor(mouseMonitor) }
    mouseMonitor = nil
    if let handler { RemoveEventHandler(handler) }
    handler = nil
    callback = nil
}

/// The character a key types with no modifier on the current keyboard
/// layout, as UTF-16, or 0. "a" for the key left of Z on an AZERTY Mac.
@_cdecl("dthub_key_character")
public func dthubKeyCharacter(_ keyCode: UInt16) -> UInt16 {
    guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
          let pointer = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return 0 }
    let data = Unmanaged<CFData>.fromOpaque(pointer).takeUnretainedValue() as Data
    return data.withUnsafeBytes { raw -> UInt16 in
        guard let layout = raw.baseAddress?.assumingMemoryBound(to: UCKeyboardLayout.self) else { return 0 }
        var dead: UInt32 = 0
        var length = 0
        var chars = [UniChar](repeating: 0, count: 4)
        let status = UCKeyTranslate(layout, keyCode, UInt16(kUCKeyActionDisplay), 0, UInt32(LMGetKbdType()),
                                    OptionBits(kUCKeyTranslateNoDeadKeysBit), &dead, chars.count, &length, &chars)
        return status == noErr && length == 1 ? chars[0] : 0
    }
}
