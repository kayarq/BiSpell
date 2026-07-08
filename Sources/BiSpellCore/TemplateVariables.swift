import Foundation

public struct TemplateVariableMatch: Equatable, Sendable {
    public let key: String
    public let range: NSRange

    public init(key: String, range: NSRange) {
        self.key = key
        self.range = range
    }
}

public struct TemplateFillResult: Equatable, Sendable {
    public var body: String
    public var lockedSpans: [LockedSpan]
    public var filledCount: Int
    public var skippedInLocks: Int

    public init(body: String, lockedSpans: [LockedSpan], filledCount: Int, skippedInLocks: Int) {
        self.body = body
        self.lockedSpans = lockedSpans
        self.filledCount = filledCount
        self.skippedInLocks = skippedInLocks
    }
}

public enum TemplateVariables {
    /// `{{key}}` with optional inner whitespace; key = [A-Za-z_][A-Za-z0-9_]*
    private static let pattern = #"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}"#

    public static func scan(_ body: String) -> [TemplateVariableMatch] {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return [] }
        let ns = body as NSString
        let full = NSRange(location: 0, length: ns.length)
        return regex.matches(in: body, range: full).compactMap { match in
            guard match.numberOfRanges >= 2 else { return nil }
            let keyRange = match.range(at: 1)
            guard keyRange.location != NSNotFound else { return nil }
            let key = ns.substring(with: keyRange)
            return TemplateVariableMatch(key: key, range: match.range(at: 0))
        }
    }

    /// Unique keys in first-seen order.
    public static func orderedKeys(in body: String) -> [String] {
        var seen = Set<String>()
        var keys: [String] = []
        for m in scan(body) {
            if seen.insert(m.key).inserted {
                keys.append(m.key)
            }
        }
        return keys
    }

    public static func orderedKeysUnlocked(in body: String, locks: [LockedSpan]) -> [String] {
        var seen = Set<String>()
        var keys: [String] = []
        for m in scan(body) {
            if LockedSpanMath.anyBlocks(locks, edit: m.range) { continue }
            if seen.insert(m.key).inserted {
                keys.append(m.key)
            }
        }
        return keys
    }

    /// Fill `{{key}}` only in unlocked ranges; back-to-front; adjust locks.
    public static func fill(
        body: String,
        locks: [LockedSpan],
        values: [String: String]
    ) -> TemplateFillResult {
        let matches = scan(body)
        var unlocked: [TemplateVariableMatch] = []
        var skipped = 0
        for m in matches {
            if LockedSpanMath.anyBlocks(locks, edit: m.range) {
                skipped += 1
            } else {
                unlocked.append(m)
            }
        }

        var text = body
        var spans = locks
        var filled = 0
        for m in unlocked.sorted(by: { $0.range.location > $1.range.location }) {
            let replacement = values[m.key] ?? ""
            let ns = text as NSString
            guard m.range.location + m.range.length <= ns.length else { continue }
            text = ns.replacingCharacters(in: m.range, with: replacement)
            spans = LockedSpanMath.adjusting(
                spans,
                edited: m.range,
                replacementLength: (replacement as NSString).length
            )
            filled += 1
        }
        spans = LockedSpanMath.clamp(spans, toTextLength: (text as NSString).length)
        return TemplateFillResult(
            body: text,
            lockedSpans: spans,
            filledCount: filled,
            skippedInLocks: skipped
        )
    }
}
