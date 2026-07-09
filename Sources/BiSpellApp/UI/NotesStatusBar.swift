import SwiftUI
import BiSpellCore

struct NotesStatusPill: View {
    @Environment(\.notesTokens) private var t
    let text: String
    var accent: Bool = false
    var warning: Bool = false
    var interactive: Bool = false
    var action: (() -> Void)? = nil

    @State private var hovering = false

    var body: some View {
        let label = Text(text)
            .font(.system(size: 10, weight: .medium, design: .monospaced))
            .foregroundStyle(Color(nsColor: warning ? t.dirty : (accent ? t.accentBright : t.textSecondary)))
            .padding(.horizontal, 7)
            .padding(.vertical, 3)
            .background(Color(nsColor: accent || warning ? t.accentDim : t.elevated))
            .overlay(
                RoundedRectangle(cornerRadius: 3, style: .continuous)
                    .strokeBorder(
                        Color(nsColor: hovering && interactive ? t.accent : t.borderSubtle),
                        lineWidth: 1
                    )
            )
            .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
            .opacity(hovering && interactive ? 0.92 : 1)

        Group {
            if let action, interactive {
                Button(action: action) { label }
                    .buttonStyle(.plain)
                    .onHover { hovering = $0 }
                    .help(helpText)
            } else {
                label
            }
        }
    }

    private var helpText: String {
        if text.contains("unsaved") { return "Click to save" }
        if text.contains("issues") { return "Jump to first issue" }
        if text.contains("locked") { return "Open regions" }
        if text.contains("spell ok") { return "No spelling issues" }
        return text
    }
}

struct NotesStatusBar: View {
    @Environment(\.notesTokens) private var t
    @ObservedObject var viewModel: NotesViewModel
    @ObservedObject var appearance: NotesAppearanceController
    var onOpenRegions: () -> Void

    var body: some View {
        HStack(spacing: 8) {
            if viewModel.isDirty {
                NotesStatusPill(
                    text: "[unsaved]",
                    warning: true,
                    interactive: true,
                    action: { viewModel.save() }
                )
            }
            if !viewModel.draftLockedSpans.isEmpty {
                NotesStatusPill(
                    text: "[\(viewModel.draftLockedSpans.count) locked]",
                    accent: true,
                    interactive: true,
                    action: onOpenRegions
                )
            }
            if viewModel.draftIsTemplate {
                NotesStatusPill(text: "[template]", accent: true)
            }
            NotesStatusPill(
                text: "[\(viewModel.editorMode.label.lowercased())]",
                interactive: true,
                action: { viewModel.cycleEditorMode() }
            )
            if let active = viewModel.activeSuggestion {
                NotesStatusPill(text: "[⌘1–5: \(active.word)]", accent: true)
            } else if viewModel.misspellings.isEmpty {
                if !viewModel.draftBody.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    NotesStatusPill(text: "[spell ok]")
                }
            } else {
                NotesStatusPill(
                    text: "[\(viewModel.misspellings.count) issues]",
                    warning: true,
                    interactive: true,
                    action: { viewModel.jumpToFirstMisspelling() }
                )
            }

            if let path = viewModel.selectedRelativePath {
                NotesStatusPill(
                    text: path,
                    interactive: true,
                    action: { viewModel.revealSelectedInFinder() }
                )
                .help("Reveal in Finder")
            }

            Spacer()

            Text(viewModel.saveStatus)
                .font(.system(size: 10, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.textTertiary))
                .lineLimit(1)

            Text("·")
                .foregroundStyle(Color(nsColor: t.textTertiary))
            Text(appearance.theme.displayName)
                .font(.system(size: 10, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.textTertiary))
            Text("·")
                .foregroundStyle(Color(nsColor: t.textTertiary))
            Text(appearance.font.displayName)
                .font(.system(size: 10, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.textTertiary))
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background(Color(nsColor: t.chromeBar))
        .overlay(alignment: .top) {
            Rectangle()
                .fill(Color(nsColor: t.borderSubtle))
                .frame(height: 1)
        }
    }
}
