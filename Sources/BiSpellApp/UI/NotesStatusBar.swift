import SwiftUI
import BiSpellCore

struct NotesStatusPill: View {
    @Environment(\.notesTokens) private var t
    let text: String
    var accent: Bool = false
    var warning: Bool = false

    var body: some View {
        Text(text)
            .font(.system(size: 10, weight: .medium, design: .monospaced))
            .foregroundStyle(Color(nsColor: warning ? t.dirty : (accent ? t.accentBright : t.textSecondary)))
            .padding(.horizontal, 7)
            .padding(.vertical, 3)
            .background(Color(nsColor: accent || warning ? t.accentDim : t.elevated))
            .overlay(
                RoundedRectangle(cornerRadius: 3, style: .continuous)
                    .strokeBorder(Color(nsColor: t.borderSubtle), lineWidth: 1)
            )
            .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
    }
}

struct NotesStatusBar: View {
    @Environment(\.notesTokens) private var t
    @ObservedObject var viewModel: NotesViewModel
    @ObservedObject var appearance: NotesAppearanceController

    var body: some View {
        HStack(spacing: 8) {
            if viewModel.isDirty {
                NotesStatusPill(text: "[unsaved]", warning: true)
            }
            if !viewModel.draftLockedSpans.isEmpty {
                NotesStatusPill(text: "[\(viewModel.draftLockedSpans.count) locked]", accent: true)
            }
            if viewModel.draftIsTemplate {
                NotesStatusPill(text: "[template]", accent: true)
            }
            if let active = viewModel.activeSuggestion {
                NotesStatusPill(text: "[⌘1–5: \(active.word)]", accent: true)
            } else if viewModel.misspellings.isEmpty {
                if !viewModel.draftBody.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    NotesStatusPill(text: "[spell ok]")
                }
            } else {
                NotesStatusPill(text: "[\(viewModel.misspellings.count) issues]", warning: true)
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
