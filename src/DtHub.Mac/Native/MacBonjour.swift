import Foundation
import dnssd

// The wireless debugging announcements, read from macOS's own Bonjour.
// adb's discovery finds nothing on a Mac, whatever its backend: without
// this, a phone whose port changed after a restart could not be found
// again, and the pairing window listed no phone nearby. Lines in the
// format of "adb mdns services": name, type, address.

private final class Found {
    var services: [(name: String, type: String, domain: String, interface: UInt32)] = []
    var host: String?
    var port: UInt16 = 0
    var address: String?
}

/// Hands the replies of one query to its callbacks until the deadline or
/// until done says enough.
private func pump(_ ref: DNSServiceRef, until deadline: Date, done: () -> Bool) {
    var poller = pollfd(fd: DNSServiceRefSockFD(ref), events: Int16(POLLIN), revents: 0)
    while !done() {
        let left = Int32(deadline.timeIntervalSinceNow * 1000)
        if left <= 0 || poll(&poller, 1, left) <= 0 { return }
        if DNSServiceProcessResult(ref) != kDNSServiceErr_NoError { return }
    }
}

private func context(_ found: Found) -> UnsafeMutableRawPointer { Unmanaged.passUnretained(found).toOpaque() }
private func found(_ context: UnsafeMutableRawPointer?) -> Found { Unmanaged<Found>.fromOpaque(context!).takeUnretainedValue() }

private func browse(_ type: String, into result: Found, for duration: TimeInterval) {
    var ref: DNSServiceRef?
    let status = DNSServiceBrowse(&ref, 0, 0, type, "local.", { _, flags, interface, error, name, regtype, domain, context in
        guard error == kDNSServiceErr_NoError, flags & DNSServiceFlags(kDNSServiceFlagsAdd) != 0,
              let name, let regtype, let domain else { return }
        found(context).services.append((String(cString: name), String(cString: regtype), String(cString: domain), interface))
    }, context(result))
    guard status == kDNSServiceErr_NoError, let ref else { return }
    defer { DNSServiceRefDeallocate(ref) }
    // Browsing never ends on its own: what is cached comes at once, what
    // a phone answers within a few hundred milliseconds.
    pump(ref, until: Date().addingTimeInterval(duration)) { false }
}

private func resolve(_ service: (name: String, type: String, domain: String, interface: UInt32)) -> String? {
    let result = Found()
    var ref: DNSServiceRef?
    var status = DNSServiceResolve(&ref, 0, service.interface, service.name, service.type, service.domain, { _, _, _, error, _, host, port, _, _, context in
        guard error == kDNSServiceErr_NoError, let host else { return }
        found(context).host = String(cString: host)
        found(context).port = UInt16(bigEndian: port)
    }, context(result))
    guard status == kDNSServiceErr_NoError, let resolver = ref else { return nil }
    pump(resolver, until: Date().addingTimeInterval(1)) { result.host != nil }
    DNSServiceRefDeallocate(resolver)
    guard let host = result.host else { return nil }

    // The phone's IPv4 address: adb connects to an address, not a name.
    ref = nil
    status = DNSServiceGetAddrInfo(&ref, 0, service.interface, DNSServiceProtocol(kDNSServiceProtocol_IPv4), host, { _, _, _, error, _, address, _, context in
        guard error == kDNSServiceErr_NoError, let address, address.pointee.sa_family == sa_family_t(AF_INET) else { return }
        var text = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
        address.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { inet in
            var raw = inet.pointee.sin_addr
            _ = inet_ntop(AF_INET, &raw, &text, socklen_t(INET_ADDRSTRLEN))
        }
        found(context).address = String(cString: text)
    }, context(result))
    guard status == kDNSServiceErr_NoError, let lookup = ref else { return nil }
    pump(lookup, until: Date().addingTimeInterval(1)) { result.address != nil }
    DNSServiceRefDeallocate(lookup)

    return result.address.map { "\($0):\(result.port)" }
}

@_cdecl("dthub_mdns_services")
public func dthubMdnsServices(_ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) -> Int32 {
    let result = Found()
    browse("_adb-tls-connect._tcp", into: result, for: 0.8)
    browse("_adb-tls-pairing._tcp", into: result, for: 0.8)

    var lines: [String] = []
    var seen = Set<String>()
    for service in result.services where seen.insert(service.name + service.type).inserted {
        guard let address = resolve(service) else { continue }
        let type = service.type.hasSuffix(".") ? String(service.type.dropLast()) : service.type
        lines.append("\(service.name)\t\(type)\t\(address)")
    }

    let text = Array(lines.joined(separator: "\n").utf8)
    guard let buffer, capacity > 0, text.count < Int(capacity) else { return -1 }
    for (index, byte) in text.enumerated() { buffer[index] = CChar(bitPattern: byte) }
    buffer[text.count] = 0
    return Int32(lines.count)
}
