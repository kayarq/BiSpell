import Foundation
#if canImport(AppKit)
import AppKit
#endif

/// Fixed palette for tags & folders (stable, offline).
public enum TaxonomyPalette: Int, CaseIterable, Codable, Sendable, Identifiable {
    case rose = 0
    case amber
    case lime
    case mint
    case cyan
    case sky
    case violet
    case magenta
    case slate
    case peach

    public var id: Int { rawValue }

    public var displayName: String {
        switch self {
        case .rose: return "Rose"
        case .amber: return "Amber"
        case .lime: return "Lime"
        case .mint: return "Mint"
        case .cyan: return "Cyan"
        case .sky: return "Sky"
        case .violet: return "Violet"
        case .magenta: return "Magenta"
        case .slate: return "Slate"
        case .peach: return "Peach"
        }
    }

    /// Soft RGB for dark terminal UI.
    public var rgb: (r: CGFloat, g: CGFloat, b: CGFloat) {
        switch self {
        case .rose: return (0.93, 0.45, 0.55)
        case .amber: return (0.95, 0.72, 0.35)
        case .lime: return (0.72, 0.90, 0.40)
        case .mint: return (0.45, 0.88, 0.70)
        case .cyan: return (0.40, 0.85, 0.90)
        case .sky: return (0.45, 0.70, 0.98)
        case .violet: return (0.72, 0.58, 0.98)
        case .magenta: return (0.90, 0.50, 0.85)
        case .slate: return (0.62, 0.68, 0.75)
        case .peach: return (0.96, 0.62, 0.48)
        }
    }

#if canImport(AppKit)
    public var nsColor: NSColor {
        NSColor(calibratedRed: rgb.r, green: rgb.g, blue: rgb.b, alpha: 1)
    }

    public var dimNSColor: NSColor {
        NSColor(calibratedRed: rgb.r, green: rgb.g, blue: rgb.b, alpha: 0.22)
    }
#endif

    /// Deterministic default from name (stable across launches).
    public static func auto(for name: String) -> TaxonomyPalette {
        let key = name.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !key.isEmpty else { return .slate }
        var hash: UInt64 = 5381
        for u in key.utf8 {
            hash = ((hash << 5) &+ hash) &+ UInt64(u)
        }
        let all = TaxonomyPalette.allCases
        return all[Int(hash % UInt64(all.count))]
    }
}

public struct NotesTaxonomySettings: Codable, Equatable, Sendable {
    /// Lowercased name → palette rawValue
    public var tagColors: [String: Int]
    public var folderColors: [String: Int]

    public static let `default` = NotesTaxonomySettings(tagColors: [:], folderColors: [:])

    public init(tagColors: [String: Int] = [:], folderColors: [String: Int] = [:]) {
        self.tagColors = tagColors
        self.folderColors = folderColors
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        tagColors = try c.decodeIfPresent([String: Int].self, forKey: .tagColors) ?? [:]
        folderColors = try c.decodeIfPresent([String: Int].self, forKey: .folderColors) ?? [:]
    }
}

public final class NotesTaxonomyStore: @unchecked Sendable {
    private let defaults: UserDefaults
    private let key = "BiSpell.NotesTaxonomy"

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    public func load() -> NotesTaxonomySettings {
        guard let data = defaults.data(forKey: key),
              let value = try? JSONDecoder().decode(NotesTaxonomySettings.self, from: data) else {
            return .default
        }
        return value
    }

    public func save(_ settings: NotesTaxonomySettings) {
        if let data = try? JSONEncoder().encode(settings) {
            defaults.set(data, forKey: key)
        }
    }
}

public enum TaxonomyColorResolver {
    public static func palette(for name: String, map: [String: Int]) -> TaxonomyPalette {
        let key = name.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if let raw = map[key], let p = TaxonomyPalette(rawValue: raw) {
            return p
        }
        return TaxonomyPalette.auto(for: key)
    }

    public static func set(_ palette: TaxonomyPalette, for name: String, map: inout [String: Int]) {
        let key = name.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !key.isEmpty else { return }
        map[key] = palette.rawValue
    }
}
