import SwiftUI
import BiSpellCore

/// Horizontal tab strip for open notes (VS Code–style).
struct NotesTabBar: View {
    @Environment(\.notesTokens) private var t
    @ObservedObject var viewModel: NotesViewModel

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 0) {
                ForEach(viewModel.openTabIDs, id: \.self) { id in
                    tabCell(id: id)
                }
                Spacer(minLength: 8)
                NotesToolbarIconChip(
                    systemName: viewModel.isNoteSplit ? "rectangle.split.2x1.fill" : "rectangle.split.2x1",
                    title: nil,
                    isPrimary: viewModel.isNoteSplit,
                    action: { viewModel.toggleNoteSplit() },
                    helpText: viewModel.isNoteSplit ? "Close note split" : "Split view (two notes)"
                )
                .padding(.trailing, 8)
            }
        }
        .frame(height: 34)
        .background(Color(nsColor: t.chromeBar))
        .overlay(alignment: .bottom) {
            Rectangle().fill(Color(nsColor: t.borderSubtle)).frame(height: 1)
        }
    }

    @ViewBuilder
    private func tabCell(id: UUID) -> some View {
        let note = viewModel.notes.first { $0.id == id }
        let title = note?.displayTitle ?? "Note"
        let isPrimary = viewModel.selectedNoteID == id
        let isSecondary = viewModel.isNoteSplit && viewModel.secondaryNoteID == id
        let isActive = (viewModel.focusedPane == .primary && isPrimary)
            || (viewModel.focusedPane == .secondary && isSecondary)
        let dirty = (isPrimary && viewModel.isDirty) || (isSecondary && viewModel.secondaryIsDirty)

        HStack(spacing: 6) {
            Button {
                // Clicking a tab activates it in the focused pane.
                if viewModel.isNoteSplit, viewModel.focusedPane == .secondary {
                    _ = viewModel.selectSecondary(id: id)
                } else {
                    _ = viewModel.activateTab(id)
                    viewModel.focusPane(.primary)
                }
            } label: {
                HStack(spacing: 4) {
                    if dirty {
                        Circle()
                            .fill(Color(nsColor: t.accent))
                            .frame(width: 6, height: 6)
                    }
                    Text(title)
                        .font(.system(size: 11, weight: isActive ? .semibold : .regular, design: .monospaced))
                        .lineLimit(1)
                    if isSecondary {
                        Text("R")
                            .font(.system(size: 8, weight: .bold, design: .monospaced))
                            .foregroundStyle(Color(nsColor: t.accentBright))
                    } else if isPrimary && viewModel.isNoteSplit {
                        Text("L")
                            .font(.system(size: 8, weight: .bold, design: .monospaced))
                            .foregroundStyle(Color(nsColor: t.textTertiary))
                    }
                }
                .foregroundStyle(Color(nsColor: isActive ? t.textPrimary : t.textSecondary))
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(
                    isActive
                        ? Color(nsColor: t.elevated)
                        : Color.clear
                )
                .overlay(alignment: .bottom) {
                    if isActive {
                        Rectangle()
                            .fill(Color(nsColor: t.accent))
                            .frame(height: 2)
                    }
                }
            }
            .buttonStyle(.plain)
            .contextMenu {
                if viewModel.isNoteSplit {
                    Button("Open in Left Pane") {
                        viewModel.focusPane(.primary)
                        _ = viewModel.select(id: id, force: false) || viewModel.select(id: id, force: true)
                    }
                    Button("Open in Right Pane") {
                        viewModel.openInSplit(id)
                    }
                } else {
                    Button("Open in Split (Right)") {
                        viewModel.openInSplit(id)
                    }
                }
                Button("Close Tab") {
                    _ = viewModel.closeTab(id)
                }
            }

            Button {
                _ = viewModel.closeTab(id)
            } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 8, weight: .bold))
                    .foregroundStyle(Color(nsColor: t.textTertiary))
                    .frame(width: 16, height: 16)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("Close tab")
        }
        .padding(.leading, 2)
        .background(Color(nsColor: isActive ? t.editor : t.chromeBar))
        .overlay(alignment: .trailing) {
            Rectangle().fill(Color(nsColor: t.borderSubtle)).frame(width: 1)
        }
    }
}
