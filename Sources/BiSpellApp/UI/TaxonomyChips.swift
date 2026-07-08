import SwiftUI
import AppKit
import BiSpellCore

// MARK: - Color swatch + picker

struct TaxonomyColorDot: View {
    let palette: TaxonomyPalette
    var size: CGFloat = 8

    var body: some View {
        Circle()
            .fill(Color(nsColor: palette.nsColor))
            .frame(width: size, height: size)
            .overlay(Circle().strokeBorder(Color.white.opacity(0.15), lineWidth: 0.5))
    }
}

struct TaxonomyColorPickerMenu: View {
    let title: String
    let current: TaxonomyPalette
    let onPick: (TaxonomyPalette) -> Void

    var body: some View {
        Menu {
            ForEach(TaxonomyPalette.allCases) { p in
                Button {
                    onPick(p)
                } label: {
                    HStack {
                        Circle().fill(Color(nsColor: p.nsColor)).frame(width: 8, height: 8)
                        Text(p.displayName)
                        if p == current { Image(systemName: "checkmark") }
                    }
                }
            }
        } label: {
            HStack(spacing: 4) {
                TaxonomyColorDot(palette: current, size: 8)
                Image(systemName: "chevron.down")
                    .font(.system(size: 7, weight: .bold))
                    .opacity(0.5)
            }
            .padding(.horizontal, 5)
            .padding(.vertical, 3)
            .contentShape(Rectangle())
        }
        .menuStyle(.borderlessButton)
        .help("Color for \(title)")
        .fixedSize()
    }
}

// MARK: - Filter / label chip

struct TaxonomyChip: View {
    @Environment(\.notesTokens) private var t
    let title: String
    let palette: TaxonomyPalette
    var selected: Bool = false
    var showDot: Bool = true
    let action: () -> Void
    var onColorPick: ((TaxonomyPalette) -> Void)? = nil

    var body: some View {
        Button(action: action) {
            HStack(spacing: 5) {
                if showDot {
                    TaxonomyColorDot(palette: palette, size: 7)
                }
                Text(title)
                    .font(.system(size: 10, weight: .medium, design: .monospaced))
                    .lineLimit(1)
            }
            .foregroundStyle(Color(nsColor: selected ? t.accentBright : t.textSecondary))
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .background(
                RoundedRectangle(cornerRadius: 3)
                    .fill(selected
                          ? Color(nsColor: palette.dimNSColor)
                          : Color(nsColor: t.elevated))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 3)
                    .strokeBorder(
                        selected ? Color(nsColor: palette.nsColor).opacity(0.7) : Color(nsColor: t.borderSubtle),
                        lineWidth: 1
                    )
            )
        }
        .buttonStyle(.plain)
        .contextMenu {
            if let onColorPick {
                ForEach(TaxonomyPalette.allCases) { p in
                    Button {
                        onColorPick(p)
                    } label: {
                        HStack {
                            Circle().fill(Color(nsColor: p.nsColor)).frame(width: 8, height: 8)
                            Text(p.displayName)
                            if p == palette { Image(systemName: "checkmark") }
                        }
                    }
                }
            }
        }
    }
}

// MARK: - Folder field with suggestions

struct FolderPickerField: View {
    @Environment(\.notesTokens) private var t
    @Binding var folder: String
    let knownFolders: [String]
    @ObservedObject var taxonomy: TaxonomyController
    var onChange: (String) -> Void

    @State private var draft: String = ""
    @State private var showSuggest = false
    @FocusState private var focused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                if let name = NoteTagging.normalizeFolder(folder), !name.isEmpty {
                    TaxonomyColorDot(palette: taxonomy.folderPalette(name), size: 8)
                    TaxonomyColorPickerMenu(
                        title: name,
                        current: taxonomy.folderPalette(name)
                    ) { taxonomy.setFolderColor($0, for: name) }
                }
                TextField("folder…", text: $draft)
                    .textFieldStyle(.plain)
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(Color(nsColor: t.textSecondary))
                    .focused($focused)
                    .onSubmit { commit(draft) }
                    .onChange(of: focused) { _, on in
                        showSuggest = on
                    }
                    .onChange(of: draft) { _, v in
                        showSuggest = focused && !suggestions(for: v).isEmpty
                    }
                if !folder.isEmpty {
                    Button {
                        commit("")
                    } label: {
                        Image(systemName: "xmark.circle.fill")
                            .font(.system(size: 11))
                            .foregroundStyle(Color(nsColor: t.textTertiary))
                    }
                    .buttonStyle(.plain)
                    .help("Clear folder")
                }
            }
            if showSuggest {
                suggestionList
            }
        }
        .onAppear { draft = folder }
        .onChange(of: folder) { _, v in
            if !focused { draft = v }
        }
    }

    private var suggestionList: some View {
        let items = suggestions(for: draft)
        return Group {
            if !items.isEmpty {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(items, id: \.self) { name in
                        Button {
                            commit(name)
                            showSuggest = false
                            focused = false
                        } label: {
                            HStack(spacing: 6) {
                                TaxonomyColorDot(palette: taxonomy.folderPalette(name), size: 7)
                                Text(name)
                                    .font(.system(size: 11, design: .monospaced))
                                    .foregroundStyle(Color(nsColor: t.textPrimary))
                                Spacer()
                            }
                            .padding(.horizontal, 8)
                            .padding(.vertical, 4)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .background(Color(nsColor: t.elevated))
                    }
                }
                .padding(4)
                .background(Color(nsColor: t.chromeBar))
                .overlay(
                    RoundedRectangle(cornerRadius: 4)
                        .strokeBorder(Color(nsColor: t.borderSubtle), lineWidth: 1)
                )
                .clipShape(RoundedRectangle(cornerRadius: 4))
            }
        }
    }

    private func suggestions(for query: String) -> [String] {
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let current = NoteTagging.normalizeFolder(folder)?.lowercased()
        return knownFolders.filter { name in
            let key = name.lowercased()
            if key == current { return false }
            if q.isEmpty { return true }
            return key.contains(q) || key.hasPrefix(q)
        }.prefix(8).map { $0 }
    }

    private func commit(_ raw: String) {
        let n = NoteTagging.normalizeFolder(raw) ?? ""
        draft = n
        folder = n
        onChange(n)
        showSuggest = false
    }
}

// MARK: - Tags field with chips + suggestions

struct TagsPickerField: View {
    @Environment(\.notesTokens) private var t
    @Binding var tagsText: String
    let knownTags: [String]
    @ObservedObject var taxonomy: TaxonomyController
    var onChange: (String) -> Void

    @State private var input: String = ""
    @State private var showSuggest = false
    @FocusState private var focused: Bool

    private var tags: [String] {
        NoteTagging.parseTagString(tagsText)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            // Flow of chips + input
            FlowTagLayout(spacing: 5) {
                ForEach(tags, id: \.self) { tag in
                    tagChip(tag)
                }
                TextField(tags.isEmpty ? "add tags…" : "+", text: $input)
                    .textFieldStyle(.plain)
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(Color(nsColor: t.textSecondary))
                    .frame(minWidth: 64)
                    .focused($focused)
                    .onSubmit { addFromInput() }
                    .onChange(of: focused) { _, on in
                        showSuggest = on
                    }
                    .onChange(of: input) { _, v in
                        showSuggest = focused && !suggestions(for: v).isEmpty
                    }
            }
            if showSuggest {
                suggestionList
            }
        }
    }

    private func tagChip(_ tag: String) -> some View {
        HStack(spacing: 4) {
            TaxonomyColorDot(palette: taxonomy.tagPalette(tag), size: 7)
            Text(tag)
                .font(.system(size: 10, weight: .medium, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.textPrimary))
            Menu {
                ForEach(TaxonomyPalette.allCases) { p in
                    Button {
                        taxonomy.setTagColor(p, for: tag)
                    } label: {
                        HStack {
                            Circle().fill(Color(nsColor: p.nsColor)).frame(width: 8, height: 8)
                            Text(p.displayName)
                            if taxonomy.tagPalette(tag) == p { Image(systemName: "checkmark") }
                        }
                    }
                }
                Divider()
                Button("Remove", role: .destructive) { removeTag(tag) }
            } label: {
                Image(systemName: "chevron.down")
                    .font(.system(size: 7, weight: .bold))
                    .foregroundStyle(Color(nsColor: t.textTertiary))
            }
            .menuStyle(.borderlessButton)
            .fixedSize()
        }
        .padding(.horizontal, 7)
        .padding(.vertical, 3)
        .background(Color(nsColor: taxonomy.tagPalette(tag).dimNSColor))
        .overlay(
            RoundedRectangle(cornerRadius: 3)
                .strokeBorder(Color(nsColor: taxonomy.tagPalette(tag).nsColor).opacity(0.55), lineWidth: 1)
        )
        .clipShape(RoundedRectangle(cornerRadius: 3))
    }

    private var suggestionList: some View {
        let items = suggestions(for: input)
        return Group {
            if !items.isEmpty {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(items, id: \.self) { name in
                        Button {
                            addTag(name)
                            input = ""
                        } label: {
                            HStack(spacing: 6) {
                                TaxonomyColorDot(palette: taxonomy.tagPalette(name), size: 7)
                                Text(name)
                                    .font(.system(size: 11, design: .monospaced))
                                    .foregroundStyle(Color(nsColor: t.textPrimary))
                                Spacer()
                            }
                            .padding(.horizontal, 8)
                            .padding(.vertical, 4)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .background(Color(nsColor: t.elevated))
                    }
                }
                .padding(4)
                .background(Color(nsColor: t.chromeBar))
                .overlay(
                    RoundedRectangle(cornerRadius: 4)
                        .strokeBorder(Color(nsColor: t.borderSubtle), lineWidth: 1)
                )
                .clipShape(RoundedRectangle(cornerRadius: 4))
            }
        }
    }

    private func suggestions(for query: String) -> [String] {
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let have = Set(tags.map { $0.lowercased() })
        return knownTags.filter { name in
            let key = name.lowercased()
            if have.contains(key) { return false }
            if q.isEmpty { return true }
            return key.contains(q) || key.hasPrefix(q)
        }.prefix(8).map { $0 }
    }

    private func addFromInput() {
        let parts = input.split(whereSeparator: { $0 == "," || $0 == ";" })
        if parts.isEmpty {
            let t = input.trimmingCharacters(in: .whitespacesAndNewlines)
            if !t.isEmpty { addTag(t) }
        } else {
            for p in parts {
                let t = p.trimmingCharacters(in: .whitespacesAndNewlines)
                if !t.isEmpty { addTag(t) }
            }
        }
        input = ""
        showSuggest = false
    }

    private func addTag(_ raw: String) {
        var next = tags
        let normalized = NoteTagging.normalizeTags([raw])
        for t in normalized {
            if !next.contains(where: { $0.lowercased() == t.lowercased() }) {
                next.append(t)
            }
        }
        apply(next)
    }

    private func removeTag(_ tag: String) {
        apply(tags.filter { $0.lowercased() != tag.lowercased() })
    }

    private func apply(_ next: [String]) {
        let text = NoteTagging.tagsDisplayString(next)
        tagsText = text
        onChange(text)
    }
}

/// Simple wrapping layout for tag chips.
struct FlowTagLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxW = proposal.width ?? .infinity
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowH: CGFloat = 0
        var height: CGFloat = 0
        var width: CGFloat = 0
        for sub in subviews {
            let size = sub.sizeThatFits(.unspecified)
            if x + size.width > maxW, x > 0 {
                y += rowH + spacing
                x = 0
                rowH = 0
            }
            x += size.width + spacing
            rowH = max(rowH, size.height)
            width = max(width, x - spacing)
            height = y + rowH
        }
        return CGSize(width: maxW.isFinite ? maxW : width, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var rowH: CGFloat = 0
        for sub in subviews {
            let size = sub.sizeThatFits(.unspecified)
            if x + size.width > bounds.maxX, x > bounds.minX {
                y += rowH + spacing
                x = bounds.minX
                rowH = 0
            }
            sub.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(size))
            x += size.width + spacing
            rowH = max(rowH, size.height)
        }
    }
}
