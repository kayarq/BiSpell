import AppKit
import SwiftUI
import Combine
import BiSpellCore

@MainActor
final class TaxonomyController: ObservableObject {
    @Published var settings: NotesTaxonomySettings {
        didSet { store.save(settings) }
    }

    private let store = NotesTaxonomyStore()

    init() {
        settings = store.load()
    }

    func tagPalette(_ name: String) -> TaxonomyPalette {
        TaxonomyColorResolver.palette(for: name, map: settings.tagColors)
    }

    func folderPalette(_ name: String) -> TaxonomyPalette {
        TaxonomyColorResolver.palette(for: name, map: settings.folderColors)
    }

    func setTagColor(_ palette: TaxonomyPalette, for name: String) {
        var s = settings
        TaxonomyColorResolver.set(palette, for: name, map: &s.tagColors)
        settings = s
    }

    func setFolderColor(_ palette: TaxonomyPalette, for name: String) {
        var s = settings
        TaxonomyColorResolver.set(palette, for: name, map: &s.folderColors)
        settings = s
    }

    /// Move a color entry when a tag is renamed (case-insensitive keys).
    func renameTagColor(from old: String, to new: String) {
        let o = old.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let n = new.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !o.isEmpty, !n.isEmpty, o != n else { return }
        var s = settings
        if let raw = s.tagColors.removeValue(forKey: o) {
            s.tagColors[n] = raw
            settings = s
        }
    }

    func removeTagColor(_ name: String) {
        let key = name.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !key.isEmpty else { return }
        var s = settings
        if s.tagColors.removeValue(forKey: key) != nil {
            settings = s
        }
    }

    func renameFolderColor(from old: String, to new: String) {
        let o = old.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let n = new.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !o.isEmpty, !n.isEmpty, o != n else { return }
        var s = settings
        if let raw = s.folderColors.removeValue(forKey: o) {
            s.folderColors[n] = raw
            settings = s
        }
    }

    func removeFolderColor(_ name: String) {
        let key = name.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !key.isEmpty else { return }
        var s = settings
        if s.folderColors.removeValue(forKey: key) != nil {
            settings = s
        }
    }

    func tagNSColor(_ name: String) -> NSColor { tagPalette(name).nsColor }
    func folderNSColor(_ name: String) -> NSColor { folderPalette(name).nsColor }
}
