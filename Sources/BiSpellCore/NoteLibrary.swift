import Foundation
import CryptoKit

// MARK: - Path normalization

/// Library path helpers: relative folders under a fixed parent root.
public enum LibraryPaths {
    public static let inbox = "inbox"
    public static let daily = "daily"
    public static let templates = "_templates"
    public static let archive = "archive"
    public static let bispellDir = ".bispell"
    public static let trashDir = ".bispell/trash"
    public static let maxDepth = 6
    public static let maxSegmentLength = 64

    public static func defaultLibraryRoot() -> URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Documents", isDirectory: true)
            .appendingPathComponent("BiSpell", isDirectory: true)
    }

    public static func expandUserPath(_ path: String) -> URL {
        let trimmed = path.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.hasPrefix("~/") {
            let rest = String(trimmed.dropFirst(2))
            return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(rest)
        }
        if trimmed == "~" {
            return FileManager.default.homeDirectoryForCurrentUser
        }
        return URL(fileURLWithPath: trimmed, isDirectory: true)
    }

    public static func displayPath(for url: URL) -> String {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        let path = url.path
        if path.hasPrefix(home) {
            return "~" + String(path.dropFirst(home.count))
        }
        return path
    }

    /// Normalize a folder path relative to library root.
    /// Empty / nil → `inbox`. Strips `..`, unsafe chars; max depth 6; segment ≤ 64.
    public static func normalizeFolder(_ raw: String?) -> String {
        guard let raw else { return inbox }
        var s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        s = s.replacingOccurrences(of: "\\", with: "/")
        while s.hasPrefix("./") { s = String(s.dropFirst(2)) }
        if s.hasPrefix("/") { s = String(s.dropFirst()) }
        if s.isEmpty { return inbox }

        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: " _-"))
        var segments: [String] = []
        for part in s.split(separator: "/") {
            var seg = String(part).trimmingCharacters(in: .whitespacesAndNewlines)
            if seg.isEmpty || seg == "." || seg == ".." { continue }
            if seg == bispellDir || seg.hasPrefix(".") { continue }
            let filtered = String(seg.unicodeScalars.filter { allowed.contains($0) })
                .trimmingCharacters(in: .whitespacesAndNewlines)
            if filtered.isEmpty { continue }
            segments.append(String(filtered.prefix(maxSegmentLength)))
            if segments.count >= maxDepth { break }
        }
        if segments.isEmpty { return inbox }
        return segments.joined(separator: "/")
    }

    /// Like normalizeFolder but returns nil for empty (callers that want optional).
    public static func normalizeFolderOptional(_ raw: String?) -> String? {
        let n = normalizeFolder(raw)
        return n
    }

    public static func sanitizeBasename(from title: String, id: UUID) -> String {
        let base = title.trimmingCharacters(in: .whitespacesAndNewlines)
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-_ "))
        let filtered = String(base.unicodeScalars.map { allowed.contains($0) ? Character($0) : Character("-") })
        let collapsed = filtered
            .split(separator: "-")
            .joined(separator: "-")
            .trimmingCharacters(in: CharacterSet(charactersIn: "- "))
            .replacingOccurrences(of: " ", with: "-")
        if collapsed.isEmpty {
            return "Untitled-\(String(id.uuidString.prefix(6)).lowercased())"
        }
        return String(collapsed.prefix(60))
    }

    public static func dailyFileName(for date: Date = Date(), calendar: Calendar = .current) -> String {
        let c = calendar.dateComponents([.year, .month, .day], from: date)
        let y = c.year ?? 1970
        let m = c.month ?? 1
        let d = c.day ?? 1
        return String(format: "%04d-%02d-%02d", y, m, d)
    }

    public static func relativePath(folder: String, basename: String) -> String {
        let f = normalizeFolder(folder)
        return "\(f)/\(basename).md"
    }
}

// MARK: - Sidecar

public struct NoteSidecar: Codable, Equatable, Sendable {
    public static let formatID = "bispell-note-meta"

    public var format: String
    public var version: Int
    public var id: UUID
    public var title: String?
    public var createdAt: Date
    public var updatedAt: Date
    public var isTemplate: Bool
    public var tags: [String]
    public var lockedSpans: [LockedSpan]
    public var editorMode: String?
    /// Cached folder path (source of truth is on-disk location).
    public var folder: String?
    public var bodySHA256: String?

    public init(
        id: UUID,
        title: String? = nil,
        createdAt: Date = Date(),
        updatedAt: Date = Date(),
        isTemplate: Bool = false,
        tags: [String] = [],
        lockedSpans: [LockedSpan] = [],
        editorMode: String? = nil,
        folder: String? = nil,
        bodySHA256: String? = nil
    ) {
        self.format = Self.formatID
        self.version = 1
        self.id = id
        self.title = title
        self.createdAt = createdAt
        self.updatedAt = updatedAt
        self.isTemplate = isTemplate
        self.tags = tags
        self.lockedSpans = lockedSpans
        self.editorMode = editorMode
        self.folder = folder
        self.bodySHA256 = bodySHA256
    }

    public init(from note: Note, bodySHA256: String? = nil, editorMode: String? = nil) {
        self.init(
            id: note.id,
            title: note.title,
            createdAt: note.createdAt,
            updatedAt: note.updatedAt,
            isTemplate: note.isTemplate,
            tags: note.tags,
            lockedSpans: note.lockedSpans,
            editorMode: editorMode,
            folder: note.folder,
            bodySHA256: bodySHA256
        )
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        format = try c.decodeIfPresent(String.self, forKey: .format) ?? Self.formatID
        version = try c.decodeIfPresent(Int.self, forKey: .version) ?? 1
        id = try c.decode(UUID.self, forKey: .id)
        title = try c.decodeIfPresent(String.self, forKey: .title)
        createdAt = try c.decodeIfPresent(Date.self, forKey: .createdAt) ?? Date()
        updatedAt = try c.decodeIfPresent(Date.self, forKey: .updatedAt) ?? Date()
        isTemplate = try c.decodeIfPresent(Bool.self, forKey: .isTemplate) ?? false
        tags = NoteTagging.normalizeTags(try c.decodeIfPresent([String].self, forKey: .tags) ?? [])
        lockedSpans = LockedSpanMath.normalize(try c.decodeIfPresent([LockedSpan].self, forKey: .lockedSpans) ?? [])
        editorMode = try c.decodeIfPresent(String.self, forKey: .editorMode)
        folder = try c.decodeIfPresent(String.self, forKey: .folder)
        bodySHA256 = try c.decodeIfPresent(String.self, forKey: .bodySHA256)
    }
}

// MARK: - Library manifest

public struct LibraryManifest: Codable, Equatable, Sendable {
    public var format: String
    public var version: Int
    public var createdAt: Date
    public var migratedFromAppSupport: Bool

    public static let formatID = "bispell-library"

    public init(
        version: Int = 1,
        createdAt: Date = Date(),
        migratedFromAppSupport: Bool = false
    ) {
        self.format = Self.formatID
        self.version = version
        self.createdAt = createdAt
        self.migratedFromAppSupport = migratedFromAppSupport
    }
}

// MARK: - File ref (in-memory location)

public struct NoteFileRef: Equatable, Sendable {
    public var folder: String
    public var basename: String

    public init(folder: String, basename: String) {
        self.folder = LibraryPaths.normalizeFolder(folder)
        self.basename = basename
    }

    public var relativeMarkdownPath: String {
        "\(folder)/\(basename).md"
    }
}

// MARK: - Body hash

public enum NoteBodyHash {
    public static func sha256(_ text: String) -> String {
        let data = Data(text.utf8)
        let digest = SHA256.hash(data: data)
        return digest.map { String(format: "%02x", $0) }.joined()
    }
}

// MARK: - Front matter interop (Phase C)

public enum NoteFrontMatter {
    /// Strip YAML front matter; return (bodyWithoutFM, title?, tags).
    public static func parse(_ text: String) -> (body: String, title: String?, tags: [String]) {
        let normalized = text.replacingOccurrences(of: "\r\n", with: "\n")
        guard normalized.hasPrefix("---\n") else {
            return (text, nil, [])
        }
        guard let endRange = normalized.range(
            of: "\n---\n",
            range: normalized.index(normalized.startIndex, offsetBy: 4)..<normalized.endIndex
        ) else {
            return (text, nil, [])
        }
        let fmStart = normalized.index(normalized.startIndex, offsetBy: 4)
        let front = String(normalized[fmStart..<endRange.lowerBound])
        let body = String(normalized[endRange.upperBound...])
        var title: String?
        var tags: [String] = []
        for line in front.split(separator: "\n", omittingEmptySubsequences: false) {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix("title:") {
                title = yamlScalar(String(trimmed.dropFirst(6)))
            } else if trimmed.hasPrefix("tags:") {
                tags = parseTags(String(trimmed.dropFirst(5)))
            }
        }
        return (body, title, NoteTagging.normalizeTags(tags))
    }

    /// Write simple front matter when tags non-empty (interop).
    public static func wrap(body: String, title: String, tags: [String]) -> String {
        guard !tags.isEmpty else { return body }
        var lines = ["---"]
        lines.append("title: \(yamlEscape(title))")
        let list = tags.map { yamlEscape($0) }.joined(separator: ", ")
        lines.append("tags: [\(list)]")
        lines.append("---")
        lines.append("")
        // Avoid double front matter
        let stripped = parse(body).body
        lines.append(stripped)
        if !stripped.hasSuffix("\n") {
            lines.append("")
        }
        return lines.joined(separator: "\n")
    }

    private static func yamlScalar(_ raw: String) -> String {
        var v = raw.trimmingCharacters(in: .whitespaces)
        if v.hasPrefix("\"") && v.hasSuffix("\"") && v.count >= 2 {
            v = String(v.dropFirst().dropLast())
                .replacingOccurrences(of: "\\\"", with: "\"")
        }
        return v
    }

    private static func parseTags(_ rest: String) -> [String] {
        var s = rest.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("[") && s.hasSuffix("]") {
            s = String(s.dropFirst().dropLast())
        }
        return NoteTagging.parseTagString(s)
    }

    private static func yamlEscape(_ s: String) -> String {
        if s.contains(":") || s.contains("#") || s.contains("\"") || s.contains("'") || s.contains("\n") {
            let escaped = s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
            return "\"\(escaped)\""
        }
        if s.isEmpty { return "\"\"" }
        return s
    }
}
