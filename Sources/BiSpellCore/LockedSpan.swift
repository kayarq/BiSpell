import Foundation

/// UTF-16 span that cannot be edited (copy/select still allowed).
public struct LockedSpan: Codable, Equatable, Sendable, Hashable {
    public var location: Int
    public var length: Int
    /// Optional region name (e.g. "Question", "Footer").
    public var label: String?

    public init(location: Int, length: Int, label: String? = nil) {
        self.location = max(0, location)
        self.length = max(0, length)
        self.label = Self.normalizeLabel(label)
    }

    public init(range: NSRange, label: String? = nil) {
        self.location = max(0, range.location)
        self.length = max(0, range.length)
        self.label = Self.normalizeLabel(label)
    }

    public var utf16Range: NSRange {
        NSRange(location: location, length: length)
    }

    public var isEmpty: Bool { length == 0 }

    public var displayLabel: String {
        if let label, !label.isEmpty { return label }
        return "Locked"
    }

    public static func normalizeLabel(_ label: String?) -> String? {
        guard let label else { return nil }
        let t = label.trimmingCharacters(in: .whitespacesAndNewlines)
        return t.isEmpty ? nil : t
    }

    /// True if an attempted edit of `edit` would modify locked characters.
    public func blocksEdit(in edit: NSRange) -> Bool {
        if edit.length == 0 {
            return edit.location > location && edit.location < location + length
        }
        return NSIntersectionRange(utf16Range, edit).length > 0
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        location = try c.decode(Int.self, forKey: .location)
        length = try c.decode(Int.self, forKey: .length)
        label = Self.normalizeLabel(try c.decodeIfPresent(String.self, forKey: .label))
    }

    enum CodingKeys: String, CodingKey {
        case location, length, label
    }
}

public enum LockedSpanMath {
    public static func normalize(_ spans: [LockedSpan]) -> [LockedSpan] {
        let sorted = spans.filter { $0.length > 0 }.sorted { $0.location < $1.location }
        guard var current = sorted.first else { return [] }
        var out: [LockedSpan] = []
        for span in sorted.dropFirst() {
            let currentEnd = current.location + current.length
            // Merge only true overlaps — keep adjacent locks separate.
            if span.location < currentEnd {
                let end = max(currentEnd, span.location + span.length)
                let label = current.label ?? span.label
                current = LockedSpan(location: current.location, length: end - current.location, label: label)
            } else {
                out.append(current)
                current = span
            }
        }
        out.append(current)
        return out
    }

    public static func add(_ spans: [LockedSpan], range: NSRange, label: String? = nil) -> [LockedSpan] {
        guard range.length > 0 else { return normalize(spans) }
        return normalize(spans + [LockedSpan(range: range, label: label)])
    }

    public static func remove(_ spans: [LockedSpan], intersecting range: NSRange) -> [LockedSpan] {
        if range.length == 0 {
            return spans.filter { span in
                !(range.location >= span.location && range.location < span.location + span.length)
            }
        }
        return spans.filter { NSIntersectionRange($0.utf16Range, range).length == 0 }
    }

    public static func rename(_ spans: [LockedSpan], at index: Int, label: String?) -> [LockedSpan] {
        guard spans.indices.contains(index) else { return spans }
        var copy = spans
        copy[index] = LockedSpan(
            location: copy[index].location,
            length: copy[index].length,
            label: label
        )
        return copy
    }

    public static func anyBlocks(_ spans: [LockedSpan], edit: NSRange) -> Bool {
        spans.contains { $0.blocksEdit(in: edit) }
    }

    public static func fullyLocked(_ range: NSRange, spans: [LockedSpan]) -> Bool {
        guard range.length > 0 else {
            return anyBlocks(spans, edit: range)
        }
        return unlockedSegments(of: range, spans: spans).isEmpty
    }

    public static func isMixedSelection(_ range: NSRange, spans: [LockedSpan]) -> Bool {
        guard range.length > 0 else { return false }
        let overlapsLock = spans.contains { NSIntersectionRange($0.utf16Range, range).length > 0 }
        let unlocked = unlockedSegments(of: range, spans: spans)
        return overlapsLock && !unlocked.isEmpty
    }

    public static func unlockedSegments(of range: NSRange, spans: [LockedSpan]) -> [NSRange] {
        guard range.length > 0 else { return [] }
        let rangeEnd = range.location + range.length
        let norm = normalize(spans)
        var lockedInRange: [NSRange] = []
        for span in norm {
            let inter = NSIntersectionRange(span.utf16Range, range)
            if inter.length > 0 { lockedInRange.append(inter) }
        }
        lockedInRange.sort { $0.location < $1.location }

        var result: [NSRange] = []
        var cursor = range.location
        for lock in lockedInRange {
            if lock.location > cursor {
                result.append(NSRange(location: cursor, length: lock.location - cursor))
            }
            cursor = max(cursor, lock.location + lock.length)
        }
        if cursor < rangeEnd {
            result.append(NSRange(location: cursor, length: rangeEnd - cursor))
        }
        return result.filter { $0.length > 0 }
    }

    public static func adjusting(
        _ spans: [LockedSpan],
        edited: NSRange,
        replacementLength: Int
    ) -> [LockedSpan] {
        let delta = replacementLength - edited.length
        let editEnd = edited.location + edited.length
        var result: [LockedSpan] = []
        for span in spans {
            let spanEnd = span.location + span.length
            if edited.location >= spanEnd {
                result.append(span)
            } else if editEnd <= span.location {
                result.append(LockedSpan(
                    location: span.location + delta,
                    length: span.length,
                    label: span.label
                ))
            }
        }
        return normalize(result)
    }

    public static func applyingDeletions(_ spans: [LockedSpan], segments: [NSRange]) -> [LockedSpan] {
        var current = spans
        for seg in segments.sorted(by: { $0.location > $1.location }) {
            current = adjusting(current, edited: seg, replacementLength: 0)
        }
        return current
    }

    public static func clamp(_ spans: [LockedSpan], toTextLength len: Int) -> [LockedSpan] {
        normalize(spans.compactMap { span in
            guard span.location < len else { return nil }
            let length = min(span.length, len - span.location)
            guard length > 0 else { return nil }
            return LockedSpan(location: span.location, length: length, label: span.label)
        })
    }
}
