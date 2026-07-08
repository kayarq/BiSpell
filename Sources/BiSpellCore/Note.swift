import Foundation

public struct Note: Identifiable, Equatable, Codable, Sendable {
    public var id: UUID
    public var title: String
    public var body: String
    public var createdAt: Date
    public var updatedAt: Date
    public var isTemplate: Bool
    public var lockedSpans: [LockedSpan]
    /// Optional flat folder name (no nesting in v1).
    public var folder: String?
    /// Multi-tags; stored trimmed, unique by case-insensitive key.
    public var tags: [String]

    public init(
        id: UUID = UUID(),
        title: String = "Untitled",
        body: String = "",
        createdAt: Date = Date(),
        updatedAt: Date = Date(),
        isTemplate: Bool = false,
        lockedSpans: [LockedSpan] = [],
        folder: String? = nil,
        tags: [String] = []
    ) {
        self.id = id
        self.title = title
        self.body = body
        self.createdAt = createdAt
        self.updatedAt = updatedAt
        self.isTemplate = isTemplate
        self.lockedSpans = LockedSpanMath.normalize(lockedSpans)
        self.folder = NoteTagging.normalizeFolder(folder)
        self.tags = NoteTagging.normalizeTags(tags)
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        title = try c.decodeIfPresent(String.self, forKey: .title) ?? "Untitled"
        body = try c.decodeIfPresent(String.self, forKey: .body) ?? ""
        createdAt = try c.decodeIfPresent(Date.self, forKey: .createdAt) ?? Date()
        updatedAt = try c.decodeIfPresent(Date.self, forKey: .updatedAt) ?? Date()
        isTemplate = try c.decodeIfPresent(Bool.self, forKey: .isTemplate) ?? false
        lockedSpans = LockedSpanMath.normalize(try c.decodeIfPresent([LockedSpan].self, forKey: .lockedSpans) ?? [])
        folder = NoteTagging.normalizeFolder(try c.decodeIfPresent(String.self, forKey: .folder))
        tags = NoteTagging.normalizeTags(try c.decodeIfPresent([String].self, forKey: .tags) ?? [])
    }

    public var displayTitle: String {
        let trimmedTitle = title.trimmingCharacters(in: .whitespacesAndNewlines)
        if !trimmedTitle.isEmpty, trimmedTitle != "Untitled", trimmedTitle != "Untitled template" {
            return trimmedTitle
        }
        if let line = body.split(whereSeparator: \.isNewline)
            .map({ $0.trimmingCharacters(in: .whitespaces) })
            .first(where: { !$0.isEmpty }) {
            return String(line.prefix(80))
        }
        return trimmedTitle.isEmpty ? "Untitled" : trimmedTitle
    }

    public var preview: String {
        let flat = body
            .replacingOccurrences(of: "\n", with: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if flat.isEmpty { return "Empty note" }
        return String(flat.prefix(120))
    }
}

public enum NoteTagging {
    public static func normalizeFolder(_ folder: String?) -> String? {
        guard let folder else { return nil }
        let t = folder.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else { return nil }
        // Flat names only
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: " _-"))
        let filtered = String(t.unicodeScalars.filter { allowed.contains($0) })
        let collapsed = filtered
            .split(whereSeparator: { $0 == "/" || $0 == "\\" })
            .joined(separator: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return collapsed.isEmpty ? nil : String(collapsed.prefix(64))
    }

    public static func normalizeTags(_ tags: [String]) -> [String] {
        var seen = Set<String>()
        var out: [String] = []
        for raw in tags {
            let t = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !t.isEmpty else { continue }
            let key = t.lowercased()
            if seen.insert(key).inserted {
                out.append(String(t.prefix(40)))
            }
        }
        return out
    }

    public static func parseTagString(_ text: String) -> [String] {
        let parts = text.split(whereSeparator: { $0 == "," || $0 == ";" })
        return normalizeTags(parts.map(String.init))
    }

    public static func tagsDisplayString(_ tags: [String]) -> String {
        tags.joined(separator: ", ")
    }
}
