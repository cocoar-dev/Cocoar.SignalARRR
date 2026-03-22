import Foundation

// MARK: - Hub Protocol Kind

/// Selects the wire protocol used by the SignalR connection.
public enum HubProtocolKind: String, Sendable {
    case json = "json"
    case messagepack = "messagepack"
}

// MARK: - Hub Protocol Abstraction

/// Internal abstraction over JSON and MessagePack hub protocols.
protocol SignalRHubProtocol {
    func writeHandshakeRequest() -> Data
    func parseHandshake(_ data: Data) throws -> (error: String?, remaining: Data?)
    func writeInvocation(target: String, arguments: [Any], invocationId: String?) throws -> Data
    func writeStreamInvocation(target: String, arguments: [Any], invocationId: String) throws -> Data
    func writeCompletion(invocationId: String, result: Any?, error: String?) throws -> Data
    func writeCancelInvocation(invocationId: String) throws -> Data
    func writePing() -> Data
    func parseMessages(_ data: Data) throws -> [HubMessage]
}

extension JsonHubProtocol: SignalRHubProtocol {}

// MARK: - MessagePack Hub Protocol

/// SignalR MessagePack Hub Protocol.
///
/// Wire format: each message is a VarInt length prefix + MessagePack array.
/// The handshake uses the same JSON/text format as the JSON protocol.
struct MessagePackHubProtocol: SignalRHubProtocol {

    // MARK: - Handshake

    func writeHandshakeRequest() -> Data {
        Data(#"{"protocol":"messagepack","version":1}"#.utf8) + [recordSeparator]
    }

    func parseHandshake(_ data: Data) throws -> (error: String?, remaining: Data?) {
        // Handshake response is always JSON text + \x1e
        guard let sepIndex = data.firstIndex(of: recordSeparator) else {
            throw SignalRError.handshakeFailed("Incomplete handshake response")
        }
        let chunk = data[data.startIndex..<sepIndex]
        let json: [String: Any]
        if chunk.isEmpty {
            json = [:]
        } else {
            json = (try? JSONSerialization.jsonObject(with: Data(chunk))) as? [String: Any] ?? [:]
        }
        let error = json["error"] as? String
        let remainingStart = data.index(after: sepIndex)
        let remaining: Data? = remainingStart < data.endIndex ? Data(data[remainingStart...]) : nil
        return (error, remaining)
    }

    // MARK: - Writing

    func writeInvocation(target: String, arguments: [Any], invocationId: String? = nil) throws -> Data {
        let array: [Any] = [
            HubMessageType.invocation.rawValue,
            [String: Any](),
            invocationId ?? NSNull(),
            target,
            arguments,
            [Any]()
        ]
        return try packMessage(array)
    }

    func writeStreamInvocation(target: String, arguments: [Any], invocationId: String) throws -> Data {
        let array: [Any] = [
            HubMessageType.streamInvocation.rawValue,
            [String: Any](),
            invocationId,
            target,
            arguments,
            [Any]()
        ]
        return try packMessage(array)
    }

    func writeCompletion(invocationId: String, result: Any? = nil, error: String? = nil) throws -> Data {
        if let error = error {
            return try packMessage([
                HubMessageType.completion.rawValue,
                [String: Any](),
                invocationId,
                1,      // resultKind: error
                error
            ] as [Any])
        } else if let result = result {
            return try packMessage([
                HubMessageType.completion.rawValue,
                [String: Any](),
                invocationId,
                3,      // resultKind: result
                result
            ] as [Any])
        } else {
            return try packMessage([
                HubMessageType.completion.rawValue,
                [String: Any](),
                invocationId,
                2       // resultKind: void
            ] as [Any])
        }
    }

    func writeCancelInvocation(invocationId: String) throws -> Data {
        try packMessage([
            HubMessageType.cancelInvocation.rawValue,
            [String: Any](),
            invocationId
        ] as [Any])
    }

    func writePing() -> Data {
        (try? packMessage([HubMessageType.ping.rawValue as Any])) ?? Data()
    }

    // MARK: - Parsing

    func parseMessages(_ data: Data) throws -> [HubMessage] {
        var messages: [HubMessage] = []
        var offset = 0
        while offset < data.count {
            guard let (msgLen, varIntSize) = try? msgpackReadVarInt(data, at: offset),
                  msgLen > 0 else { break }
            offset += varIntSize
            guard offset + msgLen <= data.count else { break }
            let msgSlice = Data(data[offset..<(offset + msgLen)])
            offset += msgLen
            if let msg = try? parseOneMessage(msgSlice) {
                messages.append(msg)
            }
        }
        return messages
    }

    // MARK: - Private: Framing

    private func packMessage(_ array: [Any]) throws -> Data {
        let payload = try msgpackEncode(array)
        return Data(msgpackWriteVarInt(payload.count)) + payload
    }

    private func parseOneMessage(_ data: Data) throws -> HubMessage? {
        let (value, _) = try msgpackDecode(data, at: 0)
        guard let array = value as? [Any],
              let typeRaw = array.first as? Int,
              let type = HubMessageType(rawValue: typeRaw) else { return nil }

        switch type {
        case .invocation:
            guard array.count >= 5 else { return nil }
            let invocationId: String? = (array[2] is NSNull) ? nil : (array[2] as? String)
            let target = array[3] as? String ?? ""
            let args = array[4] as? [Any] ?? []
            return .invocation(target: target, arguments: args, invocationId: invocationId)

        case .streamItem:
            guard array.count >= 4 else { return nil }
            let invocationId = array[2] as? String ?? ""
            let rawData = try makeJsonEnvelope(key: "item", value: array[3])
            return .streamItem(invocationId: invocationId, rawData: rawData)

        case .completion:
            guard array.count >= 4 else { return nil }
            let invocationId = array[2] as? String ?? ""
            let resultKind = array[3] as? Int ?? 0
            switch resultKind {
            case 1:
                let msg = array.count > 4 ? (array[4] as? String ?? "Server error") : "Server error"
                return .completion(invocationId: invocationId, error: msg, rawData: nil)
            case 2:
                return .completion(invocationId: invocationId, error: nil, rawData: nil)
            case 3:
                let result = array.count > 4 ? array[4] : NSNull()
                let rawData = try makeJsonEnvelope(key: "result", value: result)
                return .completion(invocationId: invocationId, error: nil, rawData: rawData)
            default:
                return .completion(invocationId: invocationId, error: nil, rawData: nil)
            }

        case .ping:
            return .ping

        case .close:
            let error = array.count > 1 ? (array[1] as? String) : nil
            return .close(error: error)

        case .streamInvocation, .cancelInvocation:
            return nil
        }
    }

    /// Wrap a MessagePack-decoded value in a JSON envelope `{"key": value}`.
    /// Binary values are base64-encoded; ext types (Guid etc.) are converted to their string form.
    private func makeJsonEnvelope(key: String, value: Any) throws -> Data {
        let jsonValue: Any
        if value is NSNull {
            jsonValue = NSNull()
        } else if let d = value as? Data {
            // Binary blob — try to interpret as UTF-8 string, else base64
            jsonValue = String(data: d, encoding: .utf8) ?? d.base64EncodedString()
        } else if JSONSerialization.isValidJSONObject([key: value] as [String: Any]) {
            jsonValue = value
        } else {
            jsonValue = "\(value)"
        }
        return try JSONSerialization.data(withJSONObject: [key: jsonValue] as [String: Any])
    }
}

// MARK: - VarInt (length-prefix framing)

private func msgpackWriteVarInt(_ value: Int) -> [UInt8] {
    var v = value
    var bytes: [UInt8] = []
    repeat {
        var byte = UInt8(v & 0x7f)
        v >>= 7
        if v != 0 { byte |= 0x80 }
        bytes.append(byte)
    } while v != 0
    return bytes
}

private func msgpackReadVarInt(_ data: Data, at offset: Int) throws -> (Int, Int) {
    var result = 0
    var shift = 0
    var i = offset
    while i < data.count {
        let byte = Int(data[i])
        i += 1
        result |= (byte & 0x7f) << shift
        shift += 7
        if byte & 0x80 == 0 { return (result, i - offset) }
        guard shift < 35 else { break }
    }
    throw SignalRError.serializationFailed("Invalid VarInt in MessagePack frame")
}

// MARK: - MessagePack Encoder

private func msgpackEncode(_ value: Any) throws -> Data {
    var out = Data()
    try msgpackWrite(value, into: &out)
    return out
}

private func msgpackWrite(_ value: Any, into out: inout Data) throws {
    if value is NSNull {
        out.append(0xc0)
        return
    }

    // Unwrap Optional<T> stored as Any
    let mirror = Mirror(reflecting: value)
    if mirror.displayStyle == .optional {
        if let child = mirror.children.first {
            try msgpackWrite(child.value, into: &out)
        } else {
            out.append(0xc0)
        }
        return
    }

    switch value {
    // Bool must precede NSNumber — Swift bools and ObjC __NSCFBoolean both match Bool first
    case let b as Bool:
        out.append(b ? 0xc3 : 0xc2)

    case let n as Int:    msgpackWriteInt(Int64(n), into: &out)
    case let n as Int8:   msgpackWriteInt(Int64(n), into: &out)
    case let n as Int16:  msgpackWriteInt(Int64(n), into: &out)
    case let n as Int32:  msgpackWriteInt(Int64(n), into: &out)
    case let n as Int64:  msgpackWriteInt(n, into: &out)
    case let n as UInt:   msgpackWriteUInt(UInt64(n), into: &out)
    case let n as UInt8:  msgpackWriteUInt(UInt64(n), into: &out)
    case let n as UInt16: msgpackWriteUInt(UInt64(n), into: &out)
    case let n as UInt32: msgpackWriteUInt(UInt64(n), into: &out)
    case let n as UInt64: msgpackWriteUInt(n, into: &out)
    case let n as Double: msgpackWriteDouble(n, into: &out)
    case let n as Float:  msgpackWriteDouble(Double(n), into: &out)

    // NSNumber fallback — handles numbers from JSONSerialization
    case let n as NSNumber:
        let enc = String(cString: n.objCType)
        if enc == "d" || enc == "f" {
            msgpackWriteDouble(n.doubleValue, into: &out)
        } else {
            msgpackWriteInt(n.int64Value, into: &out)
        }

    case let s as String:
        let bytes = Array(s.utf8)
        msgpackWriteStr(bytes, into: &out)

    case let d as Data:
        msgpackWriteBin(Array(d), into: &out)

    case let arr as [Any]:
        let count = arr.count
        if count <= 15 {
            out.append(UInt8(0x90 | count))
        } else if count <= 0xffff {
            out.append(0xdc)
            msgpackAppendBE(UInt16(count), into: &out)
        } else {
            out.append(0xdd)
            msgpackAppendBE(UInt32(count), into: &out)
        }
        for item in arr { try msgpackWrite(item, into: &out) }

    case let dict as [String: Any]:
        let count = dict.count
        if count <= 15 {
            out.append(UInt8(0x80 | count))
        } else if count <= 0xffff {
            out.append(0xde)
            msgpackAppendBE(UInt16(count), into: &out)
        } else {
            out.append(0xdf)
            msgpackAppendBE(UInt32(count), into: &out)
        }
        for (k, v) in dict {
            try msgpackWrite(k, into: &out)
            try msgpackWrite(v, into: &out)
        }

    default:
        // Encodable fallback: encode to JSON, parse back to Foundation types, then to MessagePack
        if let encodable = value as? any Encodable {
            let jsonData = try JSONEncoder().encode(AnyEncodableBox(encodable))
            let obj = try JSONSerialization.jsonObject(with: jsonData, options: .fragmentsAllowed)
            try msgpackWrite(obj, into: &out)
            return
        }
        throw SignalRError.serializationFailed("Cannot serialize \(type(of: value)) as MessagePack")
    }
}

private func msgpackWriteInt(_ n: Int64, into out: inout Data) {
    if n >= 0 {
        msgpackWriteUInt(UInt64(n), into: &out)
    } else if n >= -32 {
        out.append(UInt8(bitPattern: Int8(n)))      // negative fixint
    } else if n >= -128 {
        out.append(contentsOf: [0xd0, UInt8(bitPattern: Int8(n))])
    } else if n >= -32768 {
        out.append(0xd1); msgpackAppendBE(UInt16(bitPattern: Int16(n)), into: &out)
    } else if n >= -2_147_483_648 {
        out.append(0xd2); msgpackAppendBE(UInt32(bitPattern: Int32(n)), into: &out)
    } else {
        out.append(0xd3); msgpackAppendBE(UInt64(bitPattern: n), into: &out)
    }
}

private func msgpackWriteUInt(_ n: UInt64, into out: inout Data) {
    if n <= 0x7f {
        out.append(UInt8(n))                        // positive fixint
    } else if n <= 0xff {
        out.append(contentsOf: [0xcc, UInt8(n)])
    } else if n <= 0xffff {
        out.append(0xcd); msgpackAppendBE(UInt16(n), into: &out)
    } else if n <= 0xffff_ffff {
        out.append(0xce); msgpackAppendBE(UInt32(n), into: &out)
    } else {
        out.append(0xcf); msgpackAppendBE(n, into: &out)
    }
}

private func msgpackWriteDouble(_ n: Double, into out: inout Data) {
    out.append(0xcb); msgpackAppendBE(n.bitPattern, into: &out)
}

private func msgpackWriteStr(_ bytes: [UInt8], into out: inout Data) {
    let len = bytes.count
    if len <= 31 {
        out.append(UInt8(0xa0 | len))
    } else if len <= 0xff {
        out.append(contentsOf: [0xd9, UInt8(len)])
    } else if len <= 0xffff {
        out.append(0xda); msgpackAppendBE(UInt16(len), into: &out)
    } else {
        out.append(0xdb); msgpackAppendBE(UInt32(len), into: &out)
    }
    out.append(contentsOf: bytes)
}

private func msgpackWriteBin(_ bytes: [UInt8], into out: inout Data) {
    let len = bytes.count
    if len <= 0xff {
        out.append(contentsOf: [0xc4, UInt8(len)])
    } else if len <= 0xffff {
        out.append(0xc5); msgpackAppendBE(UInt16(len), into: &out)
    } else {
        out.append(0xc6); msgpackAppendBE(UInt32(len), into: &out)
    }
    out.append(contentsOf: bytes)
}

private func msgpackAppendBE<T: FixedWidthInteger>(_ value: T, into out: inout Data) {
    var v = value.bigEndian
    withUnsafeBytes(of: &v) { out.append(contentsOf: $0) }
}

// MARK: - MessagePack Decoder

private func msgpackDecode(_ data: Data, at offset: Int) throws -> (Any, Int) {
    guard offset < data.count else {
        throw SignalRError.serializationFailed("MessagePack: unexpected end of data at \(offset)")
    }
    let byte = data[offset]

    // positive fixint
    if byte & 0x80 == 0 { return (Int(byte), offset + 1) }
    // fixmap
    if byte & 0xf0 == 0x80 { return try msgpackDecodeMap(data, at: offset + 1, count: Int(byte & 0x0f)) }
    // fixarray
    if byte & 0xf0 == 0x90 { return try msgpackDecodeArray(data, at: offset + 1, count: Int(byte & 0x0f)) }
    // fixstr
    if byte & 0xe0 == 0xa0 {
        let len = Int(byte & 0x1f)
        let end = offset + 1 + len
        guard end <= data.count else { throw SignalRError.serializationFailed("MessagePack: fixstr overflow") }
        return (String(data: data[(offset + 1)..<end], encoding: .utf8) ?? "", end)
    }
    // negative fixint
    if byte & 0xe0 == 0xe0 { return (Int(Int8(bitPattern: byte)), offset + 1) }

    switch byte {
    case 0xc0: return (NSNull(), offset + 1)
    case 0xc2: return (false, offset + 1)
    case 0xc3: return (true, offset + 1)

    case 0xc4:  // bin8
        let len = Int(data[offset + 1])
        let end = offset + 2 + len
        return (Data(data[(offset + 2)..<end]), end)
    case 0xc5:  // bin16
        let len = Int(msgpackRU16(data, at: offset + 1))
        let end = offset + 3 + len
        return (Data(data[(offset + 3)..<end]), end)
    case 0xc6:  // bin32
        let len = Int(msgpackRU32(data, at: offset + 1))
        let end = offset + 5 + len
        return (Data(data[(offset + 5)..<end]), end)

    case 0xc7:  // ext8
        let len = Int(data[offset + 1])
        let end = offset + 3 + len
        return (Data(data[(offset + 3)..<end]), end)
    case 0xc8:  // ext16
        let len = Int(msgpackRU16(data, at: offset + 1))
        let end = offset + 4 + len
        return (Data(data[(offset + 4)..<end]), end)
    case 0xc9:  // ext32
        let len = Int(msgpackRU32(data, at: offset + 1))
        let end = offset + 6 + len
        return (Data(data[(offset + 6)..<end]), end)

    case 0xca:  // float32
        let bits = msgpackRU32(data, at: offset + 1)
        return (Double(Float(bitPattern: bits)), offset + 5)
    case 0xcb:  // float64
        return (Double(bitPattern: msgpackRU64(data, at: offset + 1)), offset + 9)

    case 0xcc: return (Int(data[offset + 1]), offset + 2)                                          // uint8
    case 0xcd: return (Int(msgpackRU16(data, at: offset + 1)), offset + 3)                        // uint16
    case 0xce: return (Int(msgpackRU32(data, at: offset + 1)), offset + 5)                        // uint32
    case 0xcf:                                                                                      // uint64
        let v = msgpackRU64(data, at: offset + 1)
        return (v <= UInt64(Int.max) ? Int(v) : v, offset + 9)

    case 0xd0: return (Int(Int8(bitPattern: data[offset + 1])), offset + 2)                        // int8
    case 0xd1: return (Int(Int16(bitPattern: msgpackRU16(data, at: offset + 1))), offset + 3)     // int16
    case 0xd2: return (Int(Int32(bitPattern: msgpackRU32(data, at: offset + 1))), offset + 5)     // int32
    case 0xd3: return (Int(Int64(bitPattern: msgpackRU64(data, at: offset + 1))), offset + 9)     // int64

    // fixext types — return raw bytes
    case 0xd4: return (Data(data[(offset + 2)..<(offset + 3)]), offset + 3)
    case 0xd5: return (Data(data[(offset + 2)..<(offset + 4)]), offset + 4)
    case 0xd6: return (Data(data[(offset + 2)..<(offset + 6)]), offset + 6)
    case 0xd7: return (Data(data[(offset + 2)..<(offset + 10)]), offset + 10)
    case 0xd8: return (Data(data[(offset + 2)..<(offset + 18)]), offset + 18)

    case 0xd9:  // str8
        let len = Int(data[offset + 1])
        let end = offset + 2 + len
        return (String(data: data[(offset + 2)..<end], encoding: .utf8) ?? "", end)
    case 0xda:  // str16
        let len = Int(msgpackRU16(data, at: offset + 1))
        let end = offset + 3 + len
        return (String(data: data[(offset + 3)..<end], encoding: .utf8) ?? "", end)
    case 0xdb:  // str32
        let len = Int(msgpackRU32(data, at: offset + 1))
        let end = offset + 5 + len
        return (String(data: data[(offset + 5)..<end], encoding: .utf8) ?? "", end)

    case 0xdc:  // array16
        return try msgpackDecodeArray(data, at: offset + 3, count: Int(msgpackRU16(data, at: offset + 1)))
    case 0xdd:  // array32
        return try msgpackDecodeArray(data, at: offset + 5, count: Int(msgpackRU32(data, at: offset + 1)))

    case 0xde:  // map16
        return try msgpackDecodeMap(data, at: offset + 3, count: Int(msgpackRU16(data, at: offset + 1)))
    case 0xdf:  // map32
        return try msgpackDecodeMap(data, at: offset + 5, count: Int(msgpackRU32(data, at: offset + 1)))

    default:
        throw SignalRError.serializationFailed("MessagePack: unknown format byte 0x\(String(byte, radix: 16))")
    }
}

private func msgpackDecodeArray(_ data: Data, at offset: Int, count: Int) throws -> (Any, Int) {
    var arr: [Any] = []
    arr.reserveCapacity(count)
    var pos = offset
    for _ in 0..<count {
        let (val, next) = try msgpackDecode(data, at: pos)
        arr.append(val)
        pos = next
    }
    return (arr, pos)
}

private func msgpackDecodeMap(_ data: Data, at offset: Int, count: Int) throws -> (Any, Int) {
    var dict: [String: Any] = [:]
    var pos = offset
    for _ in 0..<count {
        let (keyVal, next1) = try msgpackDecode(data, at: pos)
        let (val, next2) = try msgpackDecode(data, at: next1)
        if let key = keyVal as? String { dict[key] = val }
        pos = next2
    }
    return (dict, pos)
}

@inline(__always)
private func msgpackRU16(_ data: Data, at i: Int) -> UInt16 {
    UInt16(data[i]) << 8 | UInt16(data[i + 1])
}

@inline(__always)
private func msgpackRU32(_ data: Data, at i: Int) -> UInt32 {
    UInt32(data[i]) << 24 | UInt32(data[i + 1]) << 16 | UInt32(data[i + 2]) << 8 | UInt32(data[i + 3])
}

@inline(__always)
private func msgpackRU64(_ data: Data, at i: Int) -> UInt64 {
    let hi: UInt64 = UInt64(data[i]) << 56 | UInt64(data[i + 1]) << 48
                   | UInt64(data[i + 2]) << 40 | UInt64(data[i + 3]) << 32
    let lo: UInt64 = UInt64(data[i + 4]) << 24 | UInt64(data[i + 5]) << 16
                   | UInt64(data[i + 6]) << 8  | UInt64(data[i + 7])
    return hi | lo
}
