import SwiftUI
import AppKit
import BiSpellCore

extension NotesThemeTokens {
    var swiftWindow: Color { Color(nsColor: window) }
    var swiftSidebar: Color { Color(nsColor: sidebar) }
    var swiftEditor: Color { Color(nsColor: editor) }
    var swiftElevated: Color { Color(nsColor: elevated) }
    var swiftChrome: Color { Color(nsColor: chromeBar) }
    var swiftText: Color { Color(nsColor: textPrimary) }
    var swiftText2: Color { Color(nsColor: textSecondary) }
    var swiftText3: Color { Color(nsColor: textTertiary) }
    var swiftAccent: Color { Color(nsColor: accent) }
    var swiftAccentDim: Color { Color(nsColor: accentDim) }
    var swiftAccentBright: Color { Color(nsColor: accentBright) }
    var swiftBorder: Color { Color(nsColor: borderSubtle) }
    var swiftBorderStrong: Color { Color(nsColor: borderStrong) }
    var swiftLock: Color { Color(nsColor: lockFill) }
    var swiftDirty: Color { Color(nsColor: dirty) }
    var swiftDanger: Color { Color(nsColor: danger) }
    var swiftPrompt: Color { Color(nsColor: prompt) }
}

/// Environment key for theme tokens in the Notes window.
private struct NotesTokensKey: EnvironmentKey {
    static let defaultValue: NotesThemeTokens = NotesTheme.phosphor.tokens()
}

extension EnvironmentValues {
    var notesTokens: NotesThemeTokens {
        get { self[NotesTokensKey.self] }
        set { self[NotesTokensKey.self] = newValue }
    }
}
