import Foundation

public struct TemplatePackItem: Codable, Equatable, Sendable {
    public var title: String
    public var body: String
    public var lockedSpans: [LockedSpan]
    public var folder: String?
    public var tags: [String]

    public init(
        title: String,
        body: String,
        lockedSpans: [LockedSpan] = [],
        folder: String? = nil,
        tags: [String] = []
    ) {
        self.title = title
        self.body = body
        self.lockedSpans = lockedSpans
        self.folder = folder
        self.tags = tags
    }

    public init(note: Note) {
        self.title = note.title
        self.body = note.body
        self.lockedSpans = note.lockedSpans
        self.folder = note.folder
        self.tags = note.tags
    }

    public func asNewTemplateNote() -> Note {
        Note(
            title: title,
            body: body,
            isTemplate: true,
            lockedSpans: lockedSpans,
            folder: folder,
            tags: tags
        )
    }
}

public struct TemplatePackFile: Codable, Equatable, Sendable {
    public var format: String
    public var version: Int
    public var exportedAt: Date
    public var templates: [TemplatePackItem]

    public static let formatID = "bispell-template-pack"

    public init(templates: [TemplatePackItem], exportedAt: Date = Date()) {
        self.format = Self.formatID
        self.version = 1
        self.exportedAt = exportedAt
        self.templates = templates
    }
}

public enum TemplatePack {
    public static func encodeJSON(_ pack: TemplatePackFile) throws -> Data {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        enc.dateEncodingStrategy = .iso8601
        return try enc.encode(pack)
    }

    public static func decodeJSON(_ data: Data) throws -> TemplatePackFile {
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        let pack = try dec.decode(TemplatePackFile.self, from: data)
        guard pack.format == TemplatePackFile.formatID || pack.version >= 1 else {
            throw TemplatePackError.invalidFormat
        }
        return pack
    }

    public static func pack(from notes: [Note]) -> TemplatePackFile {
        TemplatePackFile(templates: notes.filter(\.isTemplate).map(TemplatePackItem.init(note:)))
    }

    // MARK: - Markdown

    public static func exportMarkdown(_ item: TemplatePackItem) -> String {
        var lines: [String] = ["---"]
        lines.append("title: \(yamlEscape(item.title))")
        lines.append("bispell-template: true")
        if let folder = item.folder {
            lines.append("folder: \(yamlEscape(folder))")
        }
        if !item.tags.isEmpty {
            let list = item.tags.map { yamlEscape($0) }.joined(separator: ", ")
            lines.append("tags: [\(list)]")
        }
        if !item.lockedSpans.isEmpty {
            lines.append("locks:")
            for span in item.lockedSpans {
                let lab = span.label.map { yamlEscape($0) } ?? "\"\""
                lines.append("  - label: \(lab)")
                lines.append("    start: \(span.location)")
                lines.append("    length: \(span.length)")
            }
        }
        lines.append("---")
        lines.append("")
        lines.append(item.body)
        if !item.body.hasSuffix("\n") {
            lines.append("")
        }
        return lines.joined(separator: "\n")
    }

    public static func parseMarkdown(_ text: String) throws -> TemplatePackItem {
        let normalized = text.replacingOccurrences(of: "\r\n", with: "\n")
        guard normalized.hasPrefix("---\n") || normalized.hasPrefix("---\r") else {
            // Whole file as body
            return TemplatePackItem(title: "Imported", body: text, lockedSpans: [], folder: nil, tags: ["imported"])
        }
        guard let endRange = normalized.range(of: "\n---\n", range: normalized.index(normalized.startIndex, offsetBy: 4)..<normalized.endIndex) else {
            throw TemplatePackError.invalidMarkdown
        }
        let fmStart = normalized.index(normalized.startIndex, offsetBy: 4)
        let front = String(normalized[fmStart..<endRange.lowerBound])
        let body = String(normalized[endRange.upperBound...])

        var title = "Imported"
        var folder: String?
        var tags: [String] = []
        var locks: [LockedSpan] = []
        var inLocks = false
        var pendingLabel: String?
        var pendingStart: Int?
        var pendingLength: Int?

        func flushLock() {
            if let s = pendingStart, let len = pendingLength, len > 0 {
                locks.append(LockedSpan(location: s, length: len, label: pendingLabel))
            }
            pendingLabel = nil
            pendingStart = nil
            pendingLength = nil
        }

        for line in front.split(separator: "\n", omittingEmptySubsequences: false) {
            let raw = String(line)
            let trimmed = raw.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix("locks:") {
                inLocks = true
                continue
            }
            if inLocks {
                if trimmed.hasPrefix("- ") {
                    flushLock()
                    // - label: x  OR just -
                    if let val = yamlValue(after: "label:", in: trimmed) {
                        pendingLabel = val.isEmpty ? nil : val
                    }
                    continue
                }
                if trimmed.hasPrefix("label:") {
                    pendingLabel = yamlValue(after: "label:", in: trimmed)
                    continue
                }
                if trimmed.hasPrefix("start:") {
                    pendingStart = Int(yamlValue(after: "start:", in: trimmed) ?? "")
                    continue
                }
                if trimmed.hasPrefix("length:") {
                    pendingLength = Int(yamlValue(after: "length:", in: trimmed) ?? "")
                    continue
                }
                // left locks block
                if !trimmed.isEmpty && !raw.hasPrefix(" ") && !raw.hasPrefix("\t") {
                    flushLock()
                    inLocks = false
                } else {
                    continue
                }
            }
            if trimmed.hasPrefix("title:") {
                title = yamlValue(after: "title:", in: trimmed) ?? title
            } else if trimmed.hasPrefix("folder:") {
                folder = NoteTagging.normalizeFolder(yamlValue(after: "folder:", in: trimmed))
            } else if trimmed.hasPrefix("tags:") {
                tags = parseTagsLine(String(trimmed.dropFirst(5)))
            }
        }
        flushLock()

        return TemplatePackItem(
            title: title,
            body: body,
            lockedSpans: LockedSpanMath.normalize(locks),
            folder: folder,
            tags: NoteTagging.normalizeTags(tags)
        )
    }

    public static func safeFileName(for title: String) -> String {
        let base = title.trimmingCharacters(in: .whitespacesAndNewlines)
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-_ "))
        let filtered = String(base.unicodeScalars.map { allowed.contains($0) ? Character($0) : "-" })
        let collapsed = filtered
            .split(separator: "-")
            .joined(separator: "-")
            .trimmingCharacters(in: CharacterSet(charactersIn: "- "))
        let name = collapsed.isEmpty ? "template" : String(collapsed.prefix(60))
        return name + ".md"
    }

    // MARK: - YAML helpers (minimal)

    private static func yamlEscape(_ s: String) -> String {
        if s.contains(":") || s.contains("#") || s.contains("\"") || s.contains("'") || s.contains("\n") {
            let escaped = s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
            return "\"\(escaped)\""
        }
        if s.isEmpty { return "\"\"" }
        return s
    }

    private static func yamlValue(after key: String, in line: String) -> String? {
        guard let r = line.range(of: key) else { return nil }
        var v = String(line[r.upperBound...]).trimmingCharacters(in: .whitespaces)
        if v.hasPrefix("\"") && v.hasSuffix("\"") && v.count >= 2 {
            v = String(v.dropFirst().dropLast())
                .replacingOccurrences(of: "\\\"", with: "\"")
                .replacingOccurrences(of: "\\\\", with: "\\")
        }
        return v
    }

    private static func parseTagsLine(_ rest: String) -> [String] {
        var s = rest.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("[") && s.hasSuffix("]") {
            s = String(s.dropFirst().dropLast())
        }
        return NoteTagging.parseTagString(s)
    }
}

public enum TemplatePackError: Error, LocalizedError {
    case invalidFormat
    case invalidMarkdown

    public var errorDescription: String? {
        switch self {
        case .invalidFormat: return "Not a BiSpell template pack"
        case .invalidMarkdown: return "Invalid template markdown front matter"
        }
    }
}
