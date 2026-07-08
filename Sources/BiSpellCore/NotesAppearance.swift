import Foundation
#if canImport(AppKit)
import AppKit
#endif

// MARK: - Themes

/// Terminal-like curated themes (dark-first).
public enum NotesTheme: String, Codable, CaseIterable, Sendable, Identifiable {
    case phosphor
    case amber
    case cyan
    case paperMono

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .phosphor: return "Phosphor"
        case .amber: return "Amber"
        case .cyan: return "Cyan"
        case .paperMono: return "Paper Mono"
        }
    }

    /// Migrate legacy theme IDs from earlier builds.
    public static func migrated(fromRaw raw: String?) -> NotesTheme {
        guard let raw else { return .phosphor }
        if let direct = NotesTheme(rawValue: raw) { return direct }
        switch raw {
        case "system", "nightInk", "sakuraDusk": return .phosphor
        case "paper", "parchmentLuxe": return .paperMono
        case "roseQuartz": return .amber
        default: return .phosphor
        }
    }
}

// MARK: - Fonts

/// Writing fonts — mono-first with proportional escape hatch.
public enum NotesFontOption: String, Codable, CaseIterable, Sendable, Identifiable {
    case sfMono
    case menlo
    case avenirNext

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .sfMono: return "SF Mono"
        case .menlo: return "Menlo"
        case .avenirNext: return "Avenir Next"
        }
    }

    public var fontNames: [String] {
        switch self {
        case .sfMono: return ["SFMono-Regular", "SF Mono", "Menlo-Regular", "Menlo"]
        case .menlo: return ["Menlo-Regular", "Menlo", "SFMono-Regular"]
        case .avenirNext: return ["Avenir Next", "AvenirNext-Regular"]
        }
    }

    public static func migrated(fromRaw raw: String?) -> NotesFontOption {
        guard let raw else { return .sfMono }
        if let direct = NotesFontOption(rawValue: raw) { return direct }
        switch raw {
        case "system", "palatino": return .sfMono
        case "avenirNext": return .avenirNext
        default: return .sfMono
        }
    }
}


// MARK: - Text color

/// Soft, readable text colors the user can pick (or theme default).
public enum NotesTextColorOption: String, Codable, CaseIterable, Sendable, Identifiable {
    case themeDefault
    case softWhite
    case softGreen
    case softAmber
    case softCyan
    case softGray
    case softInk

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .themeDefault: return "Theme default"
        case .softWhite: return "Soft white"
        case .softGreen: return "Soft green"
        case .softAmber: return "Soft amber"
        case .softCyan: return "Soft cyan"
        case .softGray: return "Soft gray"
        case .softInk: return "Soft ink"
        }
    }

    /// Fixed RGB when not themeDefault; nil means use theme textPrimary.
    public var fixedRGB: (r: CGFloat, g: CGFloat, b: CGFloat)? {
        switch self {
        case .themeDefault: return nil
        // Soft but clear on dark backgrounds (~WCAG-ish comfort, not pure #FFF)
        case .softWhite: return (0.90, 0.91, 0.92)   // #E6E8EB
        case .softGreen: return (0.78, 0.92, 0.80)   // #C7EACC
        case .softAmber: return (0.95, 0.86, 0.68)   // #F2DBAD
        case .softCyan: return (0.78, 0.90, 0.95)    // #C7E6F2
        case .softGray: return (0.78, 0.80, 0.82)    // #C7CCD1
        case .softInk: return (0.18, 0.18, 0.17)     // #2E2E2B — for light themes
        }
    }
}

// MARK: - Tokens

/// Semantic colors + radii for terminal UI (AppKit + SwiftUI consumers).
public struct NotesThemeTokens: Sendable {
    public var window: NSColor
    public var sidebar: NSColor
    public var editor: NSColor
    public var elevated: NSColor
    public var chromeBar: NSColor

    public var textPrimary: NSColor
    public var textSecondary: NSColor
    public var textTertiary: NSColor

    public var accent: NSColor
    public var accentDim: NSColor
    public var accentBright: NSColor

    public var borderSubtle: NSColor
    public var borderStrong: NSColor

    public var lockFill: NSColor
    public var templateBadge: NSColor
    public var dirty: NSColor
    public var danger: NSColor

    public var prompt: NSColor
    public var selection: NSColor
    public var caret: NSColor

    public var chipRadius: CGFloat
    public var cardRadius: CGFloat
    public var rowRadius: CGFloat

    /// Override primary body text (and soft secondary/tertiary derived from it).
    public func applying(textColor option: NotesTextColorOption) -> NotesThemeTokens {
        guard let rgb = option.fixedRGB else { return self }
        var copy = self
        let primary = NSColor(calibratedRed: rgb.r, green: rgb.g, blue: rgb.b, alpha: 1)
        copy.textPrimary = primary
        // Derive hierarchy without pure gray washouts (use fixed RGB, not color-space components)
        let factor2: CGFloat = option == .softInk ? 0.55 : 0.72
        let factor3: CGFloat = option == .softInk ? 0.40 : 0.52
        let base: CGFloat = option == .softInk ? 0.92 : 0.12
        func mix(_ f: CGFloat) -> NSColor {
            NSColor(
                calibratedRed: rgb.r * f + base * (1 - f),
                green: rgb.g * f + base * (1 - f),
                blue: rgb.b * f + base * (1 - f),
                alpha: 1
            )
        }
        copy.textSecondary = mix(factor2)
        copy.textTertiary = mix(factor3)
        copy.caret = primary
        return copy
    }
}

// MARK: - Settings

public struct NotesAppearanceSettings: Codable, Equatable, Sendable {
    public var theme: NotesTheme
    public var font: NotesFontOption
    public var fontSize: Double
    public var textColor: NotesTextColorOption

    public static let `default` = NotesAppearanceSettings(
        theme: .phosphor,
        font: .sfMono,
        fontSize: 14,
        textColor: .themeDefault
    )

    public init(
        theme: NotesTheme,
        font: NotesFontOption,
        fontSize: Double = 14,
        textColor: NotesTextColorOption = .themeDefault
    ) {
        self.theme = theme
        self.font = font
        self.fontSize = fontSize
        self.textColor = textColor
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        if let themeRaw = try? c.decode(String.self, forKey: .theme) {
            theme = NotesTheme.migrated(fromRaw: themeRaw)
        } else {
            theme = .phosphor
        }
        if let fontRaw = try? c.decode(String.self, forKey: .font) {
            font = NotesFontOption.migrated(fromRaw: fontRaw)
        } else {
            font = .sfMono
        }
        fontSize = try c.decodeIfPresent(Double.self, forKey: .fontSize) ?? 14
        if let textRaw = try? c.decode(String.self, forKey: .textColor),
           let tc = NotesTextColorOption(rawValue: textRaw) {
            textColor = tc
        } else {
            textColor = .themeDefault
        }
    }

    enum CodingKeys: String, CodingKey {
        case theme, font, fontSize, textColor
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(theme.rawValue, forKey: .theme)
        try c.encode(font.rawValue, forKey: .font)
        try c.encode(fontSize, forKey: .fontSize)
        try c.encode(textColor.rawValue, forKey: .textColor)
    }
}

public final class NotesAppearanceStore: @unchecked Sendable {
    private let defaults: UserDefaults
    private let key = "BiSpell.NotesAppearance"

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    public func load() -> NotesAppearanceSettings {
        guard let data = defaults.data(forKey: key),
              let value = try? JSONDecoder().decode(NotesAppearanceSettings.self, from: data) else {
            return .default
        }
        return value
    }

    public func save(_ settings: NotesAppearanceSettings) {
        if let data = try? JSONEncoder().encode(settings) {
            defaults.set(data, forKey: key)
        }
    }
}

// MARK: - Theme factories

#if canImport(AppKit)
public extension NotesTheme {
    /// Legacy shim used by older call sites.
    struct Colors: Sendable {
        public var editorBackground: NSColor
        public var editorText: NSColor
        public var sidebarBackground: NSColor
        public var chromeBackground: NSColor
        public var secondaryText: NSColor

        public init(
            editorBackground: NSColor,
            editorText: NSColor,
            sidebarBackground: NSColor,
            chromeBackground: NSColor,
            secondaryText: NSColor
        ) {
            self.editorBackground = editorBackground
            self.editorText = editorText
            self.sidebarBackground = sidebarBackground
            self.chromeBackground = chromeBackground
            self.secondaryText = secondaryText
        }
    }

    func colors(effectiveDark: Bool) -> Colors {
        let t = tokens()
        return Colors(
            editorBackground: t.editor,
            editorText: t.textPrimary,
            sidebarBackground: t.sidebar,
            chromeBackground: t.chromeBar,
            secondaryText: t.textSecondary
        )
    }

    func tokens() -> NotesThemeTokens {
        switch self {
        case .phosphor:
            return .make(
                window: hex(0x0B0F0C),
                sidebar: hex(0x080B09),
                editor: hex(0x0E1410),
                elevated: hex(0x141C16),
                chrome: hex(0x101612),
                text: hex(0xD4F5DE),
                text2: hex(0x8FCBAA),
                text3: hex(0x5A8A70),
                accent: hex(0x3DDC84),
                accentDim: hex(0x1A3D2A),
                accentBright: hex(0x6EF5A8),
                lockAlpha: 0.14,
                dirty: hex(0xE8B86D),
                danger: hex(0xE85D5D)
            )
        case .amber:
            return .make(
                window: hex(0x120E08),
                sidebar: hex(0x0E0B06),
                editor: hex(0x161008),
                elevated: hex(0x1E160C),
                chrome: hex(0x18120A),
                text: hex(0xFFE4B8),
                text2: hex(0xD4B07A),
                text3: hex(0x8A6B40),
                accent: hex(0xFFB000),
                accentDim: hex(0x3D2A10),
                accentBright: hex(0xFFC94D),
                lockAlpha: 0.14,
                dirty: hex(0xFFB000),
                danger: hex(0xE85D5D)
            )
        case .cyan:
            return .make(
                window: hex(0x0A0E14),
                sidebar: hex(0x070A10),
                editor: hex(0x0C121A),
                elevated: hex(0x121A24),
                chrome: hex(0x0E141C),
                text: hex(0xDCF2FA),
                text2: hex(0x8BB8CC),
                text3: hex(0x4A7080),
                accent: hex(0x5FD0FF),
                accentDim: hex(0x163040),
                accentBright: hex(0x8ADFFF),
                lockAlpha: 0.14,
                dirty: hex(0xE8B86D),
                danger: hex(0xE85D5D)
            )
        case .paperMono:
            return .make(
                window: hex(0xEDEBE3),
                sidebar: hex(0xE4E1D7),
                editor: hex(0xF2F0E9),
                elevated: hex(0xFFFFFF),
                chrome: hex(0xE8E5DB),
                text: hex(0x141412),
                text2: hex(0x4A4840),
                text3: hex(0x6E6B62),
                accent: hex(0x0B6E4F),
                accentDim: hex(0xC5D9CF),
                accentBright: hex(0x0E8A63),
                lockAlpha: 0.12,
                dirty: hex(0xB8860B),
                danger: hex(0xC23B3B),
                isLight: true
            )
        }
    }
}

private extension NotesThemeTokens {
    static func make(
        window: NSColor,
        sidebar: NSColor,
        editor: NSColor,
        elevated: NSColor,
        chrome: NSColor,
        text: NSColor,
        text2: NSColor,
        text3: NSColor,
        accent: NSColor,
        accentDim: NSColor,
        accentBright: NSColor,
        lockAlpha: CGFloat,
        dirty: NSColor,
        danger: NSColor,
        isLight: Bool = false
    ) -> NotesThemeTokens {
        let borderSubtle = isLight
            ? NSColor(calibratedWhite: 0, alpha: 0.10)
            : NSColor(calibratedWhite: 1, alpha: 0.08)
        let borderStrong = isLight
            ? NSColor(calibratedWhite: 0, alpha: 0.18)
            : NSColor(calibratedWhite: 1, alpha: 0.16)
        return NotesThemeTokens(
            window: window,
            sidebar: sidebar,
            editor: editor,
            elevated: elevated,
            chromeBar: chrome,
            textPrimary: text,
            textSecondary: text2,
            textTertiary: text3,
            accent: accent,
            accentDim: accentDim,
            accentBright: accentBright,
            borderSubtle: borderSubtle,
            borderStrong: borderStrong,
            lockFill: accent.withAlphaComponent(lockAlpha),
            templateBadge: accentDim,
            dirty: dirty,
            danger: danger,
            prompt: accent,
            selection: accent.withAlphaComponent(isLight ? 0.20 : 0.28),
            caret: accentBright,
            chipRadius: 5,
            cardRadius: 6,
            rowRadius: 4
        )
    }
}

private func hex(_ value: UInt32, alpha: CGFloat = 1) -> NSColor {
    let r = CGFloat((value >> 16) & 0xFF) / 255
    let g = CGFloat((value >> 8) & 0xFF) / 255
    let b = CGFloat(value & 0xFF) / 255
    return NSColor(calibratedRed: r, green: g, blue: b, alpha: alpha)
}

public extension NotesFontOption {
    func nsFont(size: CGFloat) -> NSFont {
        for name in fontNames {
            if let font = NSFont(name: name, size: size) {
                return font
            }
        }
        return NSFont.monospacedSystemFont(ofSize: size, weight: .regular)
    }
}
#endif
