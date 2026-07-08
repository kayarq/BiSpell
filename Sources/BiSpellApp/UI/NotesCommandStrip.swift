import SwiftUI
import BiSpellCore

struct NotesToolbarDivider: View {
    @Environment(\.notesTokens) private var t
    var body: some View {
        Rectangle()
            .fill(Color(nsColor: t.borderSubtle))
            .frame(width: 1, height: 18)
            .padding(.horizontal, 4)
    }
}

struct NotesToolbarChip<LabelContent: View>: View {
    @Environment(\.notesTokens) private var t
    var isPrimary: Bool = false
    var isDestructive: Bool = false
    var disabled: Bool = false
    let action: () -> Void
    @ViewBuilder let label: () -> LabelContent

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            label()
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(foreground)
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(background)
                .overlay(
                    RoundedRectangle(cornerRadius: t.chipRadius, style: .continuous)
                        .strokeBorder(border, lineWidth: 1)
                )
                .clipShape(RoundedRectangle(cornerRadius: t.chipRadius, style: .continuous))
        }
        .buttonStyle(.plain)
        .disabled(disabled)
        .opacity(disabled ? 0.4 : 1)
        .onHover { hovering = $0 }
        .help("") // callers set .help outside
    }

    private var foreground: Color {
        if isDestructive { return Color(nsColor: t.danger) }
        if isPrimary { return Color(nsColor: t.accentBright) }
        return Color(nsColor: t.textPrimary)
    }

    private var background: Color {
        if isPrimary {
            return Color(nsColor: hovering ? t.accent : t.accentDim)
        }
        if hovering {
            return Color(nsColor: t.accentDim)
        }
        return Color(nsColor: t.elevated)
    }

    private var border: Color {
        if isPrimary { return Color(nsColor: t.accent) }
        if hovering { return Color(nsColor: t.borderStrong) }
        return Color(nsColor: t.borderSubtle)
    }
}

extension NotesToolbarChip where LabelContent == Text {
    init(_ title: String, isPrimary: Bool = false, isDestructive: Bool = false, disabled: Bool = false, action: @escaping () -> Void) {
        self.isPrimary = isPrimary
        self.isDestructive = isDestructive
        self.disabled = disabled
        self.action = action
        self.label = { Text(title) }
    }
}

struct NotesToolbarIconChip: View {
    @Environment(\.notesTokens) private var t
    let systemName: String
    var title: String? = nil
    var isPrimary: Bool = false
    var disabled: Bool = false
    let action: () -> Void
    var helpText: String = ""

    var body: some View {
        NotesToolbarChip(isPrimary: isPrimary, disabled: disabled, action: action) {
            HStack(spacing: 5) {
                Image(systemName: systemName)
                    .font(.system(size: 11, weight: .semibold))
                    .symbolRenderingMode(.monochrome)
                if let title {
                    Text(title)
                }
            }
        }
        .help(helpText)
    }
}

/// Terminal-style command strip for Notes detail chrome.
struct NotesCommandStrip: View {
    @Environment(\.notesTokens) private var t
    @ObservedObject var viewModel: NotesViewModel
    @ObservedObject var appearance: NotesAppearanceController

    var onNewNote: () -> Void
    var onNewTemplate: () -> Void
    var onFromTemplate: (UUID) -> Void
    var onDelete: () -> Void

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                themeMenu
                fontMenu
                textColorMenu
                NotesToolbarDivider()

                NotesToolbarIconChip(
                    systemName: "lock.fill",
                    title: "Lock",
                    disabled: !viewModel.canLockSelection,
                    action: { viewModel.lockSelection() },
                    helpText: "Lock selected text"
                )
                NotesToolbarIconChip(
                    systemName: "lock.open.fill",
                    title: "Unlock",
                    disabled: !viewModel.canUnlockSelection,
                    action: { viewModel.unlockSelection() },
                    helpText: "Unlock selection"
                )

                NotesToolbarDivider()

                NotesToolbarIconChip(
                    systemName: "square.and.pencil",
                    title: "Note",
                    isPrimary: true,
                    action: onNewNote,
                    helpText: "New note (⌘N)"
                )
                NotesToolbarIconChip(
                    systemName: "doc.badge.plus",
                    title: "Template",
                    action: onNewTemplate,
                    helpText: "New template"
                )
                fromTemplateMenu

                NotesToolbarDivider()

                NotesToolbarIconChip(
                    systemName: "text.badge.checkmark",
                    title: "Fix All",
                    disabled: viewModel.selectedNoteID == nil,
                    action: { _ = viewModel.fixAllMisspellings() },
                    helpText: "Fix all (⌥⌘/)"
                )
                NotesToolbarIconChip(
                    systemName: "square.and.arrow.down",
                    title: "Save",
                    isPrimary: viewModel.isDirty,
                    disabled: !viewModel.isDirty,
                    action: { viewModel.save() },
                    helpText: "Save (⌘S)"
                )

                Menu {
                    if viewModel.draftIsTemplate {
                        Button("Move to Notes") { viewModel.convertTemplateToNote() }
                    } else if viewModel.selectedNoteID != nil {
                        Button("Move to Templates") { viewModel.saveCurrentAsTemplate() }
                    }
                    Button("Delete", role: .destructive, action: onDelete)
                        .disabled(viewModel.selectedNoteID == nil)
                } label: {
                    chipLabel(systemName: "ellipsis", title: nil)
                }
                .menuStyle(.borderlessButton)
                .fixedSize()
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
        }
        .background(Color(nsColor: t.chromeBar))
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(Color(nsColor: t.borderSubtle))
                .frame(height: 1)
        }
    }

    private var themeMenu: some View {
        Menu {
            ForEach(NotesTheme.allCases) { theme in
                Button {
                    appearance.theme = theme
                } label: {
                    HStack {
                        Circle()
                            .fill(Color(nsColor: theme.tokens().accent))
                            .frame(width: 8, height: 8)
                        Text(theme.displayName)
                        if appearance.theme == theme {
                            Image(systemName: "checkmark")
                        }
                    }
                }
            }
        } label: {
            chipLabel(systemName: "circle.lefthalf.filled", title: appearance.theme.displayName)
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .help("Theme")
    }

    private var fontMenu: some View {
        Menu {
            ForEach(NotesFontOption.allCases) { font in
                Button {
                    appearance.font = font
                } label: {
                    HStack {
                        Text(font.displayName)
                        if appearance.font == font {
                            Image(systemName: "checkmark")
                        }
                    }
                }
            }
            Divider()
            Button("Smaller") { appearance.fontSize -= 1 }
            Button("Larger") { appearance.fontSize += 1 }
            Text("Size: \(Int(appearance.fontSize))pt")
        } label: {
            chipLabel(systemName: "textformat", title: appearance.font.displayName)
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .help("Font")
    }

    private var textColorMenu: some View {
        Menu {
            ForEach(NotesTextColorOption.allCases) { opt in
                Button {
                    appearance.textColor = opt
                } label: {
                    HStack {
                        Circle()
                            .fill(swatch(for: opt))
                            .frame(width: 8, height: 8)
                        Text(opt.displayName)
                        if appearance.textColor == opt {
                            Image(systemName: "checkmark")
                        }
                    }
                }
            }
        } label: {
            chipLabel(systemName: "paintpalette", title: "Text")
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .help("Text color")
    }

    private func swatch(for opt: NotesTextColorOption) -> Color {
        if let rgb = opt.fixedRGB {
            return Color(red: rgb.r, green: rgb.g, blue: rgb.b)
        }
        return Color(nsColor: appearance.theme.tokens().textPrimary)
    }

    private var fromTemplateMenu: some View {
        Menu {
            if viewModel.templateNotes.isEmpty {
                Text("No templates yet")
            } else {
                ForEach(viewModel.templateNotes) { tmpl in
                    Button(tmpl.displayTitle) { onFromTemplate(tmpl.id) }
                }
            }
        } label: {
            chipLabel(systemName: "doc.on.doc", title: "From")
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .help("New note from template")
    }

    private func chipLabel(systemName: String, title: String?) -> some View {
        HStack(spacing: 5) {
            Image(systemName: systemName)
                .font(.system(size: 11, weight: .semibold))
                .symbolRenderingMode(.monochrome)
            if let title {
                Text(title)
                    .font(.system(size: 12, weight: .medium))
                    .lineLimit(1)
            }
            Image(systemName: "chevron.down")
                .font(.system(size: 8, weight: .bold))
                .opacity(0.6)
        }
        .foregroundStyle(Color(nsColor: t.textPrimary))
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(Color(nsColor: t.elevated))
        .overlay(
            RoundedRectangle(cornerRadius: t.chipRadius, style: .continuous)
                .strokeBorder(Color(nsColor: t.borderSubtle), lineWidth: 1)
        )
        .clipShape(RoundedRectangle(cornerRadius: t.chipRadius, style: .continuous))
    }
}

