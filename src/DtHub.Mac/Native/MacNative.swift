// What DT Hub asks of macOS to place the game windows, as plain C
// functions for .NET (Platform/MacNative.cs). A window is named by its
// CGWindowID, as the PC names one by its HWND. Coordinates are in points,
// origin at the top left of the main screen, as scrcpy and the
// Accessibility API use them.
//
// Moving another application's window goes through the Accessibility API:
// macOS allows it only once the player has ticked DT Hub in System
// Settings, Privacy & Security, Accessibility. dthub_ax_trusted says so.

import AppKit
import ApplicationServices

// Private but stable since macOS 10.x, and what every window manager uses:
// the only way from an Accessibility element to its CGWindowID.
@_silgen_name("_AXUIElementGetWindow")
private func axGetWindow(_ element: AXUIElement, _ id: UnsafeMutablePointer<CGWindowID>) -> AXError

private func attribute(_ element: AXUIElement, _ name: String) -> AnyObject? {
    var value: AnyObject?
    return AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success ? value : nil
}

private func windowInfo(_ id: CGWindowID) -> [String: Any]? {
    let list = CGWindowListCopyWindowInfo([.optionIncludingWindow], id) as? [[String: Any]]
    return list?.first
}

private func ownerPid(_ id: CGWindowID) -> pid_t? {
    (windowInfo(id)?[kCGWindowOwnerPID as String] as? Int).map { pid_t($0) }
}

/// One of our own windows. The Accessibility API cannot be used on
/// ourselves: the request is served by our own main thread, which is the
/// one waiting for the answer, so it times out. Our windows are moved
/// through AppKit instead.
private func ownWindow(_ id: CGWindowID) -> NSWindow? {
    // Looked up among our windows directly: the window server's list does
    // not hold a window shown a moment ago yet.
    let find = { NSApp?.windows.first { $0.windowNumber == Int(id) } }
    return Thread.isMainThread ? find() : DispatchQueue.main.sync(execute: find)
}

/// Runs on the main thread, as AppKit requires; from another thread the
/// gesture is queued rather than waited for.
private func onMain(_ body: @escaping () -> Void) {
    if Thread.isMainThread { body() } else { DispatchQueue.main.async(execute: body) }
}

/// Top-left origin, as the rest of DT Hub, to AppKit's bottom-left one.
private func appKitFrame(_ x: Double, _ y: Double, _ width: Double, _ height: Double) -> NSRect {
    let top = NSScreen.screens.first?.frame.height ?? 0
    return NSRect(x: x, y: top - y - height, width: width, height: height)
}

/// The Accessibility element of a window, found among its application's.
private func element(_ id: CGWindowID) -> AXUIElement? {
    guard let pid = ownerPid(id) else { return nil }
    let app = AXUIElementCreateApplication(pid)
    let windows = attribute(app, kAXWindowsAttribute as String) as? [AXUIElement] ?? []
    for window in windows {
        var found: CGWindowID = 0
        if axGetWindow(window, &found) == .success && found == id { return window }
    }
    return nil
}

private func copy(_ text: String, _ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) {
    guard let buffer, capacity > 0 else { return }
    let bytes = Array(text.utf8.prefix(Int(capacity) - 1))
    for (index, byte) in bytes.enumerated() { buffer[index] = CChar(bitPattern: byte) }
    buffer[bytes.count] = 0
}

@_cdecl("dthub_ax_trusted")
public func dthubAxTrusted(_ prompt: Bool) -> Bool {
    let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: prompt] as CFDictionary
    return AXIsProcessTrustedWithOptions(options)
}

/// The CGWindowID of one of our own windows, from its NSWindow.
@_cdecl("dthub_window_number")
public func dthubWindowNumber(_ nsWindow: UnsafeMutableRawPointer?) -> Int64 {
    guard let nsWindow else { return 0 }
    let window = Unmanaged<NSWindow>.fromOpaque(nsWindow).takeUnretainedValue()
    return Int64(window.windowNumber)
}

/// The ordinary windows of a process, front to back.
@_cdecl("dthub_windows_of")
public func dthubWindowsOf(_ pid: Int32, _ ids: UnsafeMutablePointer<Int64>?, _ capacity: Int32) -> Int32 {
    let list = CGWindowListCopyWindowInfo([.excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
    var count: Int32 = 0
    for info in list where (info[kCGWindowOwnerPID as String] as? Int) == Int(pid)
        && (info[kCGWindowLayer as String] as? Int) == 0 {
        if let ids, count < capacity, let id = info[kCGWindowNumber as String] as? Int {
            ids[Int(count)] = Int64(id)
        }
        count += 1
    }
    return count
}

@_cdecl("dthub_window_exists")
public func dthubWindowExists(_ id: Int64) -> Bool { windowInfo(CGWindowID(id)) != nil }

@_cdecl("dthub_window_pid")
public func dthubWindowPid(_ id: Int64) -> Int32 { ownerPid(CGWindowID(id)) ?? 0 }

/// Frame of a window, title bar included.
@_cdecl("dthub_window_frame")
public func dthubWindowFrame(_ id: Int64, _ frame: UnsafeMutablePointer<Double>?) -> Bool {
    guard let frame, let bounds = windowInfo(CGWindowID(id))?[kCGWindowBounds as String] as? [String: Any],
          let rect = CGRect(dictionaryRepresentation: bounds as CFDictionary) else { return false }
    frame[0] = rect.origin.x; frame[1] = rect.origin.y; frame[2] = rect.width; frame[3] = rect.height
    return true
}

@_cdecl("dthub_window_title")
public func dthubWindowTitle(_ id: Int64, _ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) -> Bool {
    if let own = ownWindow(CGWindowID(id)) {
        copy(own.title, buffer, capacity)
        return true
    }
    let title = (element(CGWindowID(id)).flatMap { attribute($0, kAXTitleAttribute as String) as? String })
        ?? (windowInfo(CGWindowID(id))?[kCGWindowName as String] as? String)
    guard let title else { return false }
    copy(title, buffer, capacity)
    return true
}

/// Moves and sizes a window's frame. The size is asked twice, around the
/// move: macOS clamps a size that would not fit where the window still is.
@_cdecl("dthub_move_window")
public func dthubMoveWindow(_ id: Int64, _ x: Double, _ y: Double, _ width: Double, _ height: Double) -> Bool {
    if let own = ownWindow(CGWindowID(id)) {
        onMain { own.setFrame(appKitFrame(x, y, width, height), display: true) }
        return true
    }
    guard let window = element(CGWindowID(id)) else { return false }
    var size = CGSize(width: width, height: height)
    var origin = CGPoint(x: x, y: y)
    guard let sizeValue = AXValueCreate(.cgSize, &size), let originValue = AXValueCreate(.cgPoint, &origin) else {
        return false
    }
    _ = AXUIElementSetAttributeValue(window, kAXSizeAttribute as CFString, sizeValue)
    let moved = AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, originValue) == .success
    _ = AXUIElementSetAttributeValue(window, kAXSizeAttribute as CFString, sizeValue)
    return moved
}

/// Brings the window and its application to the front.
@_cdecl("dthub_raise_window")
public func dthubRaiseWindow(_ id: Int64, _ activate: Bool) -> Bool {
    if let own = ownWindow(CGWindowID(id)) {
        onMain {
            if activate {
                NSApp.activate()
                own.makeKeyAndOrderFront(nil)
            } else {
                own.orderFront(nil)
            }
        }
        return true
    }
    guard let window = element(CGWindowID(id)) else { return false }
    let raised = AXUIElementPerformAction(window, kAXRaiseAction as CFString) == .success
    if activate, let pid = ownerPid(CGWindowID(id)) {
        _ = AXUIElementSetAttributeValue(window, kAXMainAttribute as CFString, kCFBooleanTrue)
        NSRunningApplication(processIdentifier: pid)?.activate()
    }
    return raised
}

/// Presses the window's close button, as the player would.
@_cdecl("dthub_close_window")
public func dthubCloseWindow(_ id: Int64) -> Bool {
    if let own = ownWindow(CGWindowID(id)) {
        onMain { own.performClose(nil) }
        return true
    }
    guard let window = element(CGWindowID(id)),
          let button = attribute(window, kAXCloseButtonAttribute as String) else { return false }
    return AXUIElementPerformAction(button as! AXUIElement, kAXPressAction as CFString) == .success
}

@_cdecl("dthub_set_minimized")
public func dthubSetMinimized(_ id: Int64, _ minimized: Bool) -> Bool {
    if let own = ownWindow(CGWindowID(id)) {
        onMain { if minimized { own.miniaturize(nil) } else { own.deminiaturize(nil) } }
        return true
    }
    guard let window = element(CGWindowID(id)) else { return false }
    let value: CFBoolean = minimized ? kCFBooleanTrue : kCFBooleanFalse
    return AXUIElementSetAttributeValue(window, kAXMinimizedAttribute as CFString, value) == .success
}

@_cdecl("dthub_is_minimized")
public func dthubIsMinimized(_ id: Int64) -> Bool {
    if let own = ownWindow(CGWindowID(id)) { return own.isMiniaturized }
    guard let window = element(CGWindowID(id)) else { return false }
    return (attribute(window, kAXMinimizedAttribute as String) as? Bool) ?? false
}

/// The focused window of the frontmost application, or 0.
@_cdecl("dthub_front_window")
public func dthubFrontWindow() -> Int64 {
    guard let app = NSWorkspace.shared.frontmostApplication else { return 0 }
    let element = AXUIElementCreateApplication(app.processIdentifier)
    guard let focused = attribute(element, kAXFocusedWindowAttribute as String) else { return 0 }
    var id: CGWindowID = 0
    return axGetWindow(focused as! AXUIElement, &id) == .success ? Int64(id) : 0
}

/// Command, Option, Control or Shift held down right now.
@_cdecl("dthub_modifier_down")
public func dthubModifierDown() -> Bool {
    let flags = CGEventSource.flagsState(.combinedSessionState)
    return !flags.intersection([.maskCommand, .maskAlternate, .maskControl, .maskShift]).isEmpty
}

/// The screens: for each, frame then visible frame (x, y, width, height),
/// top-left origin, eight doubles per screen. The first is the main one.
@_cdecl("dthub_screens")
public func dthubScreens(_ values: UnsafeMutablePointer<Double>?, _ capacity: Int32) -> Int32 {
    let screens = NSScreen.screens
    guard let main = screens.first else { return 0 }
    let height = main.frame.height
    for (index, screen) in screens.enumerated() where values != nil && index < Int(capacity) {
        for (offset, rect) in [screen.frame, screen.visibleFrame].enumerated() {
            let base = index * 8 + offset * 4
            values![base] = rect.origin.x
            values![base + 1] = height - rect.origin.y - rect.height
            values![base + 2] = rect.width
            values![base + 3] = rect.height
        }
    }
    return Int32(screens.count)
}
