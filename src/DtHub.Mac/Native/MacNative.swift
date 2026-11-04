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

/// Our own windows. The Accessibility API cannot be used on ourselves: the
/// request is served by our own main thread, which is the one waiting for
/// the answer, so it times out. Our windows go through AppKit instead, on
/// the main thread only.
///
/// Off the main thread nothing waits for it: the UI thread may itself be
/// blocked waiting on the caller (at quit, it waits for the sessions to
/// stop), and a synchronous hop would hang DT Hub for good. Gestures are
/// queued to it, and questions are answered from the window server.
private func findOwn(_ id: CGWindowID) -> NSWindow? {
    NSApp?.windows.first { $0.windowNumber == Int(id) }
}

private func isOwn(_ id: CGWindowID) -> Bool {
    // A window shown a moment ago is not in the window server's list yet,
    // hence AppKit's own list when that is allowed.
    Thread.isMainThread ? findOwn(id) != nil : ownerPid(id) == getpid()
}

/// Gives up after half a second rather than the default six: a scrcpy whose
/// main thread hangs (link lost, decoder stuck) must not freeze DT Hub's
/// interface on every placement.
private func bounded(_ element: AXUIElement) -> AXUIElement {
    _ = AXUIElementSetMessagingTimeout(element, 0.5)
    return element
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
    let app = bounded(AXUIElementCreateApplication(pid))
    let windows = attribute(app, kAXWindowsAttribute as String) as? [AXUIElement] ?? []
    for window in windows {
        var found: CGWindowID = 0
        if axGetWindow(window, &found) == .success && found == id { return window }
    }
    return nil
}

private func copy(_ text: String, _ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) {
    guard let buffer, capacity > 0 else { return }
    // Whole characters only, so a long title is never cut inside one.
    var cut = Substring(text)
    while cut.utf8.count > Int(capacity) - 1 { cut.removeLast() }
    let bytes = Array(cut.utf8)
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
    if Thread.isMainThread, let own = findOwn(CGWindowID(id)) {
        copy(own.title, buffer, capacity)
        return true
    }
    let title = isOwn(CGWindowID(id)) ? (windowInfo(CGWindowID(id))?[kCGWindowName as String] as? String) : (element(CGWindowID(id)).flatMap { attribute($0, kAXTitleAttribute as String) as? String })
        ?? (windowInfo(CGWindowID(id))?[kCGWindowName as String] as? String)
    guard let title else { return false }
    copy(title, buffer, capacity)
    return true
}

/// Moves and sizes a window's frame. The size is asked twice, around the
/// move: macOS clamps a size that would not fit where the window still is.
@_cdecl("dthub_move_window")
public func dthubMoveWindow(_ id: Int64, _ x: Double, _ y: Double, _ width: Double, _ height: Double) -> Bool {
    if isOwn(CGWindowID(id)) {
        onMain { findOwn(CGWindowID(id))?.setFrame(appKitFrame(x, y, width, height), display: true) }
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
    if isOwn(CGWindowID(id)) {
        onMain {
            guard let own = findOwn(CGWindowID(id)) else { return }
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
    if activate, let pid = ownerPid(CGWindowID(id)) {
        // Through the Accessibility API, not NSRunningApplication.activate:
        // since macOS 14 an app that is not in front may not activate
        // another, and DT Hub is rarely in front while the player plays.
        // Setting the application frontmost is what a trusted process may
        // still do.
        let app = bounded(AXUIElementCreateApplication(pid))
        _ = AXUIElementSetAttributeValue(app, kAXFrontmostAttribute as CFString, kCFBooleanTrue)
        _ = AXUIElementSetAttributeValue(window, kAXMainAttribute as CFString, kCFBooleanTrue)
    }
    return AXUIElementPerformAction(window, kAXRaiseAction as CFString) == .success
}

/// Presses the window's close button, as the player would.
@_cdecl("dthub_close_window")
public func dthubCloseWindow(_ id: Int64) -> Bool {
    if isOwn(CGWindowID(id)) {
        onMain { findOwn(CGWindowID(id))?.performClose(nil) }
        return true
    }
    guard let window = element(CGWindowID(id)),
          let button = attribute(window, kAXCloseButtonAttribute as String) else { return false }
    return AXUIElementPerformAction(button as! AXUIElement, kAXPressAction as CFString) == .success
}

@_cdecl("dthub_set_minimized")
public func dthubSetMinimized(_ id: Int64, _ minimized: Bool) -> Bool {
    if isOwn(CGWindowID(id)) {
        onMain {
            guard let own = findOwn(CGWindowID(id)) else { return }
            if minimized { own.miniaturize(nil) } else { own.deminiaturize(nil) }
        }
        return true
    }
    guard let window = element(CGWindowID(id)) else { return false }
    let value: CFBoolean = minimized ? kCFBooleanTrue : kCFBooleanFalse
    return AXUIElementSetAttributeValue(window, kAXMinimizedAttribute as CFString, value) == .success
}

@_cdecl("dthub_is_minimized")
public func dthubIsMinimized(_ id: Int64) -> Bool {
    if isOwn(CGWindowID(id)) {
        if Thread.isMainThread, let own = findOwn(CGWindowID(id)) { return own.isMiniaturized }
        // A minimized window is off screen for the window server.
        return (windowInfo(CGWindowID(id))?[kCGWindowIsOnscreen as String] as? Bool) == false
    }
    guard let window = element(CGWindowID(id)) else { return false }
    return (attribute(window, kAXMinimizedAttribute as String) as? Bool) ?? false
}

/// The focused window of the frontmost application, or 0.
@_cdecl("dthub_front_window")
public func dthubFrontWindow() -> Int64 {
    guard let app = NSWorkspace.shared.frontmostApplication else { return 0 }
    let element = bounded(AXUIElementCreateApplication(app.processIdentifier))
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

/// Shift held down right now, as WPF's Keyboard.Modifiers says it.
@_cdecl("dthub_shift_down")
public func dthubShiftDown() -> Bool {
    NSEvent.modifierFlags.contains(.shift)
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

/// Of the given windows, the one nearest the front on screen, or 0. Lets
/// the tab strip name the game actually shown when macOS refused to bring
/// the selected one forward (an app not in front may not activate another).
@_cdecl("dthub_front_most_of")
public func dthubFrontMostOf(_ ids: UnsafePointer<Int64>?, _ count: Int32) -> Int64 {
    guard let ids, count > 0 else { return 0 }
    let wanted = Set((0..<Int(count)).map { ids[$0] })
    let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
        as? [[String: Any]] ?? []
    for info in list {
        if let id = info[kCGWindowNumber as String] as? Int, wanted.contains(Int64(id)) { return Int64(id) }
    }
    return 0
}

/// The processor's time since startup, all cores, in ticks: idle, and the
/// total. What the PC reads with GetSystemTimes, for its busy-machine
/// warning.
@_cdecl("dthub_cpu_ticks")
public func dthubCpuTicks(_ idle: UnsafeMutablePointer<Int64>?, _ total: UnsafeMutablePointer<Int64>?) -> Bool {
    guard let idle, let total else { return false }
    var load = host_cpu_load_info()
    var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info_data_t>.size / MemoryLayout<integer_t>.size)
    let result = withUnsafeMutablePointer(to: &load) {
        $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
            host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO, $0, &count)
        }
    }
    guard result == KERN_SUCCESS else { return false }
    let user = Int64(load.cpu_ticks.0), system = Int64(load.cpu_ticks.1)
    let idleTicks = Int64(load.cpu_ticks.2), nice = Int64(load.cpu_ticks.3)
    idle.pointee = idleTicks
    total.pointee = user + system + idleTicks + nice
    return true
}
