import SwiftUI
import BiSpellCore

struct NotesSidebarRow: View {
    @Environment(\.notesTokens) private var t
    let note: Note
    var isTemplate: Bool
    var isSelected: Bool
    var isDirty: Bool
    var taxonomy: TaxonomyController

    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            RoundedRectangle(cornerRadius: 1)
                .fill(isSelected ? Color(nsColor: t.accent) : Color.clear)
                .frame(width: 2)
                .padding(.vertical, 2)

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(note.displayTitle)
                        .font(.system(size: 12, weight: .semibold, design: .default))
                        .foregroundStyle(Color(nsColor: t.textPrimary))
                        .lineLimit(1)
                    if !note.lockedSpans.isEmpty {
                        Image(systemName: "lock.fill")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundStyle(Color(nsColor: t.accent))
                    }
                    if isDirty {
                        Circle()
                            .fill(Color(nsColor: t.dirty))
                            .frame(width: 6, height: 6)
                    }
                    Spacer(minLength: 0)
                    if isTemplate {
                        Text("TPL")
                            .font(.system(size: 9, weight: .bold, design: .monospaced))
                            .foregroundStyle(Color(nsColor: t.accent))
                            .padding(.horizontal, 5)
                            .padding(.vertical, 1)
                            .background(Color(nsColor: t.accentDim))
                            .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
                    }
                }
                Text(note.preview)
                    .font(.system(size: 11))
                    .foregroundStyle(Color(nsColor: t.textSecondary))
                    .lineLimit(2)

                // Folder + tags color row
                if note.folder != nil || !note.tags.isEmpty {
                    HStack(spacing: 5) {
                        if let folder = note.folder, !folder.isEmpty {
                            HStack(spacing: 3) {
                                TaxonomyColorDot(palette: taxonomy.folderPalette(folder), size: 6)
                                Text(folder)
                                    .font(.system(size: 9, design: .monospaced))
                                    .foregroundStyle(Color(nsColor: taxonomy.folderNSColor(folder)))
                                    .lineLimit(1)
                            }
                        }
                        ForEach(note.tags.prefix(4), id: \.self) { tag in
                            HStack(spacing: 3) {
                                TaxonomyColorDot(palette: taxonomy.tagPalette(tag), size: 6)
                                Text(tag)
                                    .font(.system(size: 9, design: .monospaced))
                                    .foregroundStyle(Color(nsColor: taxonomy.tagNSColor(tag)))
                                    .lineLimit(1)
                            }
                        }
                        if note.tags.count > 4 {
                            Text("+\(note.tags.count - 4)")
                                .font(.system(size: 9, design: .monospaced))
                                .foregroundStyle(Color(nsColor: t.textTertiary))
                        }
                        Spacer(minLength: 0)
                    }
                }

                Text(note.updatedAt.formatted(date: .abbreviated, time: .shortened))
                    .font(.system(size: 10, design: .monospaced))
                    .foregroundStyle(Color(nsColor: t.textTertiary))
            }
        }
        .padding(.vertical, 6)
        .padding(.trailing, 8)
        .padding(.leading, 4)
        .background(
            RoundedRectangle(cornerRadius: t.rowRadius, style: .continuous)
                .fill(isSelected ? Color(nsColor: t.accentDim) : Color.clear)
        )
        .contentShape(Rectangle())
    }
}

struct NotesSectionHeader: View {
    @Environment(\.notesTokens) private var t
    let title: String

    var body: some View {
        HStack(spacing: 6) {
            Text(">")
                .font(.system(size: 10, weight: .bold, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.prompt))
            Text(title.uppercased())
                .font(.system(size: 10, weight: .bold, design: .monospaced))
                .tracking(1.2)
                .foregroundStyle(Color(nsColor: t.textSecondary))
            Spacer()
        }
        .padding(.top, 8)
        .padding(.bottom, 2)
    }
}
