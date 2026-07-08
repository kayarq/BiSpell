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

    func tagNSColor(_ name: String) -> NSColor { tagPalette(name).nsColor }
    func folderNSColor(_ name: String) -> NSColor { folderPalette(name).nsColor }
}
