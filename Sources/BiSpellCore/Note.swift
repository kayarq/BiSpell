import Foundation

public struct Note: Identifiable, Equatable, Codable, Sendable {
    public var id: UUID
    public var title: String
    public var body: String
    public var createdAt: Date
    public var updatedAt: Date
    public var isTemplate: Bool
    public var lockedSpans: [LockedSpan]
    /// Relative path under library root (e.g. `inbox`, `work/projects`, `daily`).
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
            // Skip pure front-matter markers
            let s = String(line)
            if s != "---" {
                return String(s.prefix(80))
            }
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
    /// Normalize relative folder path. Empty → nil (callers map to inbox when persisting).
    /// Nested paths allowed: `work/ideas`. Max depth 6, segment ≤ 64.
    public static func normalizeFolder(_ folder: String?) -> String? {
        guard let folder else { return nil }
        let t = folder.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else { return nil }
        // Use library path rules; empty/unsafe → treat as nil so UI "clear" works,
        // while store maps nil → inbox on save.
        let normalized = LibraryPaths.normalizeFolder(t)
        // If user cleared intentionally, normalizeFolder returns inbox — distinguish:
        // only when input was non-empty after trim we return the path.
        return normalized
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
