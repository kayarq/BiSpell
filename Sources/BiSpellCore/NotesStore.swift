import Foundation

/// Persists notes as Markdown body + `.bispell.json` sidecar under a user-chosen library root.
public final class NotesStore: @unchecked Sendable {
    private let lock = NSLock()
    private var root: URL
    private var refs: [UUID: NoteFileRef] = [:]
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    public init(libraryRoot: URL? = nil) {
        self.root = libraryRoot ?? LibraryPaths.defaultLibraryRoot()
        self.encoder = JSONEncoder()
        self.encoder.outputFormatting = []
        self.encoder.dateEncodingStrategy = .iso8601
        self.decoder = JSONDecoder()
        self.decoder.dateDecodingStrategy = .iso8601
        try? ensureSkeletonUnlocked()
    }

    /// Test helper: use a temporary root (same as libraryRoot init).
    public convenience init(directory: URL) {
        self.init(libraryRoot: directory)
    }

    public var libraryRoot: URL {
        lock.lock(); defer { lock.unlock() }
        return root
    }

    public var notesDirectory: URL { libraryRoot }

    public func setLibraryRoot(_ url: URL) throws {
        lock.lock()
        defer { lock.unlock() }
        root = url
        refs.removeAll()
        try ensureSkeletonUnlocked()
    }

    public func ensureSkeleton() throws {
        lock.lock()
        defer { lock.unlock() }
        try ensureSkeletonUnlocked()
    }

    // MARK: - Load / Save

    public func loadAll() throws -> [Note] {
        lock.lock()
        defer { lock.unlock() }
        try ensureSkeletonUnlocked()
        refs.removeAll()
        var notes: [Note] = []
        let mdFiles = try enumerateMarkdownFilesUnlocked()
        for mdURL in mdFiles {
            if let note = try? loadNotePairUnlocked(mdURL: mdURL) {
                notes.append(note)
            }
        }
        return notes.sorted { $0.updatedAt > $1.updatedAt }
    }

    public func save(_ note: Note) throws {
        lock.lock()
        defer { lock.unlock() }
        try saveUnlocked(note)
    }

    public func saveAll(_ notes: [Note]) throws {
        lock.lock()
        defer { lock.unlock() }
        for note in notes {
            try saveUnlocked(note)
        }
    }

    /// Soft-delete: move MD+sidecar pair into `.bispell/trash/` (atomic pair with MD rollback).
    public func delete(id: UUID) throws {
        lock.lock()
        defer { lock.unlock() }
        guard let ref = refs[id] else {
            throw NotesStoreError.noteNotFound
        }
        let md = markdownURLUnlocked(ref: ref)
        let sc = sidecarURLUnlocked(ref: ref)
        let trash = root.appendingPathComponent(LibraryPaths.trashDir, isDirectory: true)
        try FileManager.default.createDirectory(at: trash, withIntermediateDirectories: true)

        let stamp = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "-")
        let base = "\(ref.basename)-\(String(id.uuidString.prefix(8)))-\(stamp)"
        let destMD = trash.appendingPathComponent("\(base).md")
        let destSC = trash.appendingPathComponent("\(base).bispell.json")

        let fm = FileManager.default
        let mdExists = fm.fileExists(atPath: md.path)
        let scExists = fm.fileExists(atPath: sc.path)
        if mdExists {
            // Pair move: if sidecar fails, MD is rolled back so refs stay valid.
            try movePairUnlocked(mdFrom: md, scFrom: sc, mdTo: destMD, scTo: destSC)
        } else if scExists {
            // Orphan sidecar only — still trash it so the ref can be cleared cleanly.
            try fm.moveItem(at: sc, to: destSC)
        }
        // Only drop the ref after both files have been handled successfully.
        refs.removeValue(forKey: id)
    }

    /// Permanently remove soft-deleted files.
    public func emptyTrash() throws {
        lock.lock()
        defer { lock.unlock() }
        let trash = root.appendingPathComponent(LibraryPaths.trashDir, isDirectory: true)
        let fm = FileManager.default
        guard fm.fileExists(atPath: trash.path) else { return }
        let items = (try? fm.contentsOfDirectory(at: trash, includingPropertiesForKeys: nil)) ?? []
        for url in items {
            try? fm.removeItem(at: url)
        }
    }

    public func trashItemCount() -> Int {
        lock.lock()
        defer { lock.unlock() }
        let trash = root.appendingPathComponent(LibraryPaths.trashDir, isDirectory: true)
        let items = (try? FileManager.default.contentsOfDirectory(at: trash, includingPropertiesForKeys: nil)) ?? []
        return items.filter { $0.pathExtension.lowercased() == "md" }.count
    }

    // MARK: - Move / folder

    /// Move note files to a new relative folder. Returns updated note.
    @discardableResult
    public func move(id: UUID, toFolder rawFolder: String?) throws -> Note {
        lock.lock()
        defer { lock.unlock() }
        guard let ref = refs[id] else {
            throw NotesStoreError.noteNotFound
        }
        let newFolder = LibraryPaths.normalizeFolder(rawFolder)
        if ref.folder == newFolder {
            return try loadNoteFromRefUnlocked(ref)
        }
        let oldMD = markdownURLUnlocked(ref: ref)
        let oldSC = sidecarURLUnlocked(ref: ref)
        guard FileManager.default.fileExists(atPath: oldMD.path) else {
            throw NotesStoreError.fileMissing
        }

        var destRef = NoteFileRef(folder: newFolder, basename: ref.basename)
        destRef = uniqueRefUnlocked(destRef, excluding: id)

        let destDir = directoryURLUnlocked(folder: destRef.folder)
        try FileManager.default.createDirectory(at: destDir, withIntermediateDirectories: true)
        let newMD = markdownURLUnlocked(ref: destRef)
        let newSC = sidecarURLUnlocked(ref: destRef)

        try movePairUnlocked(mdFrom: oldMD, scFrom: oldSC, mdTo: newMD, scTo: newSC)

        refs[id] = destRef
        var note = try loadNoteFromRefUnlocked(destRef)
        note.folder = destRef.folder
        // Refresh sidecar folder cache
        try writeSidecarUnlocked(note: note, ref: destRef, body: note.body)
        return note
    }

    /// Rename a directory under the library and update open refs.
    public func renameFolder(from rawFrom: String, to rawTo: String) throws {
        lock.lock()
        defer { lock.unlock() }
        let from = LibraryPaths.normalizeFolder(rawFrom)
        let to = LibraryPaths.normalizeFolder(rawTo)
        guard from != to else { return }
        let src = directoryURLUnlocked(folder: from)
        let dst = directoryURLUnlocked(folder: to)
        let fm = FileManager.default
        guard fm.fileExists(atPath: src.path) else {
            throw NotesStoreError.fileMissing
        }
        if fm.fileExists(atPath: dst.path) {
            throw NotesStoreError.destinationExists
        }
        try fm.createDirectory(at: dst.deletingLastPathComponent(), withIntermediateDirectories: true)
        try fm.moveItem(at: src, to: dst)

        for (id, ref) in refs {
            if ref.folder == from || ref.folder.hasPrefix(from + "/") {
                let suffix = String(ref.folder.dropFirst(from.count))
                let newFolder = to + suffix
                refs[id] = NoteFileRef(folder: newFolder, basename: ref.basename)
            }
        }
    }

    /// Move every note under `folder` (exact path or nested child) into `inbox/`.
    /// Does not remove protected roots (`inbox`, `daily`, `_templates`). Returns count moved.
    @discardableResult
    public func deleteFolderMovingNotesToInbox(_ rawFolder: String) throws -> Int {
        lock.lock()
        defer { lock.unlock() }
        let folder = LibraryPaths.normalizeFolder(rawFolder)
        guard folder != LibraryPaths.inbox else {
            throw NotesStoreError.protectedFolder
        }
        let ids = refs.compactMap { id, ref -> UUID? in
            if ref.folder == folder || ref.folder.hasPrefix(folder + "/") { return id }
            return nil
        }
        var moved = 0
        for id in ids {
            _ = try moveUnlocked(id: id, toFolder: LibraryPaths.inbox)
            moved += 1
        }
        // Best-effort remove emptied directory tree under the library.
        let dir = directoryURLUnlocked(folder: folder)
        try? FileManager.default.removeItem(at: dir)
        return moved
    }

    /// Replace tag `from` with `to` on every note (case-insensitive match on `from`).
    /// If `to` is nil/empty, remove the tag. Returns number of notes updated.
    @discardableResult
    public func rewriteTag(from rawFrom: String, to rawTo: String?) throws -> Int {
        lock.lock()
        defer { lock.unlock() }
        let fromKey = rawFrom.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !fromKey.isEmpty else { return 0 }
        let newTag: String? = {
            guard let rawTo else { return nil }
            let t = rawTo.trimmingCharacters(in: .whitespacesAndNewlines)
            return t.isEmpty ? nil : String(t.prefix(40))
        }()
        if let newTag, newTag.lowercased() == fromKey { return 0 }

        var updated = 0
        for (_, ref) in refs {
            var note = try loadNoteFromRefUnlocked(ref)
            let had = note.tags.contains { $0.lowercased() == fromKey }
            guard had else { continue }
            var tags = note.tags.filter { $0.lowercased() != fromKey }
            if let newTag {
                tags.append(newTag)
            }
            note.tags = NoteTagging.normalizeTags(tags)
            note.updatedAt = Date()
            try saveUnlocked(note)
            updated += 1
        }
        return updated
    }

    // MARK: - Paths

    public func fileURL(for id: UUID) -> URL? {
        lock.lock(); defer { lock.unlock() }
        guard let ref = refs[id] else { return nil }
        return markdownURLUnlocked(ref: ref)
    }

    public func relativePath(for id: UUID) -> String? {
        lock.lock(); defer { lock.unlock() }
        return refs[id]?.relativeMarkdownPath
    }

    public func fileRef(for id: UUID) -> NoteFileRef? {
        lock.lock(); defer { lock.unlock() }
        return refs[id]
    }

    public func modificationDate(for id: UUID) -> Date? {
        lock.lock(); defer { lock.unlock() }
        guard let ref = refs[id] else { return nil }
        let url = markdownURLUnlocked(ref: ref)
        let values = try? url.resourceValues(forKeys: [.contentModificationDateKey])
        return values?.contentModificationDate
    }

    public func knownFoldersOnDisk() -> [String] {
        lock.lock()
        defer { lock.unlock() }
        var set = Set<String>()
        set.insert(LibraryPaths.inbox)
        set.insert(LibraryPaths.daily)
        set.insert(LibraryPaths.templates)
        for ref in refs.values {
            set.insert(ref.folder)
            // ancestors
            let parts = ref.folder.split(separator: "/")
            var acc = ""
            for p in parts {
                acc = acc.isEmpty ? String(p) : acc + "/" + p
                set.insert(acc)
            }
        }
        // scan dirs
        if let enumerator = FileManager.default.enumerator(
            at: root,
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles]
        ) {
            for case let url as URL in enumerator {
                var isDir: ObjCBool = false
                if FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir), isDir.boolValue {
                    let rel = relativeFolderPathUnlocked(url)
                    if let rel, !rel.isEmpty, !rel.hasPrefix(LibraryPaths.bispellDir) {
                        set.insert(rel)
                    }
                }
            }
        }
        return set.sorted()
    }

    // MARK: - Migration

    /// Import App Support JSON notes into the library. Does not delete source files.
    /// Skips notes whose UUID already exists in the library (never clobbers post-migration edits).
    @discardableResult
    public func migrateFromAppSupport(sourceDirectory: URL? = nil) throws -> Int {
        lock.lock()
        defer { lock.unlock() }
        let src = sourceDirectory ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
            .appendingPathComponent("BiSpell", isDirectory: true)
            .appendingPathComponent("Notes", isDirectory: true)
        let fm = FileManager.default
        guard fm.fileExists(atPath: src.path) else { return 0 }

        // Ensure `refs` reflects on-disk library so re-migration cannot overwrite.
        if refs.isEmpty {
            let mdFiles = (try? enumerateMarkdownFilesUnlocked()) ?? []
            for mdURL in mdFiles {
                _ = try? loadNotePairUnlocked(mdURL: mdURL)
            }
        }
        let existingIDs = Set(refs.keys)

        let files = (try? fm.contentsOfDirectory(at: src, includingPropertiesForKeys: nil)) ?? []
        var count = 0
        let noteDecoder = JSONDecoder()
        noteDecoder.dateDecodingStrategy = .iso8601
        for url in files where url.pathExtension.lowercased() == "json" {
            guard let data = try? Data(contentsOf: url),
                  var note = try? noteDecoder.decode(Note.self, from: data) else { continue }
            if existingIDs.contains(note.id) || refs[note.id] != nil {
                continue
            }
            // Map flat folder → path segment; templates → _templates
            if note.isTemplate {
                note.folder = LibraryPaths.templates
            } else if let f = note.folder, !f.isEmpty {
                note.folder = LibraryPaths.normalizeFolder(f)
            } else {
                note.folder = LibraryPaths.inbox
            }
            try saveUnlocked(note)
            count += 1
        }
        if count > 0 {
            try updateManifestUnlocked { $0.migratedFromAppSupport = true }
        }
        return count
    }

    public var hasLegacyAppSupportNotes: Bool {
        let src = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
            .appendingPathComponent("BiSpell", isDirectory: true)
            .appendingPathComponent("Notes", isDirectory: true)
        let files = (try? FileManager.default.contentsOfDirectory(at: src, includingPropertiesForKeys: nil)) ?? []
        return files.contains { $0.pathExtension.lowercased() == "json" }
    }

    // MARK: - Bulk import MD

    /// Import a folder of `.md` files into `destFolder` (relative). Writes sidecars.
    @discardableResult
    public func importMarkdownFolder(from source: URL, destFolder: String?, recursive: Bool = true) throws -> [Note] {
        lock.lock()
        defer { lock.unlock() }
        let dest = LibraryPaths.normalizeFolder(destFolder)
        var imported: [Note] = []
        let fm = FileManager.default
        let urls: [URL]
        if recursive {
            let en = fm.enumerator(at: source, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])
            var list: [URL] = []
            while let u = en?.nextObject() as? URL {
                if u.pathExtension.lowercased() == "md" { list.append(u) }
            }
            urls = list
        } else {
            urls = ((try? fm.contentsOfDirectory(at: source, includingPropertiesForKeys: nil)) ?? [])
                .filter { $0.pathExtension.lowercased() == "md" }
        }
        for url in urls {
            guard let text = try? readTextFileUnlocked(url) else { continue }
            let fmParsed = NoteFrontMatter.parse(text)
            let title = fmParsed.title
                ?? firstHeading(in: fmParsed.body)
                ?? url.deletingPathExtension().lastPathComponent
            var tags = fmParsed.tags
            if !tags.contains(where: { $0.lowercased() == "imported" }) {
                tags = NoteTagging.normalizeTags(tags + ["imported"])
            }
            let note = Note(
                title: title,
                body: fmParsed.body,
                isTemplate: dest == LibraryPaths.templates,
                lockedSpans: [],
                folder: dest,
                tags: tags
            )
            try saveUnlocked(note)
            imported.append(note)
        }
        return imported
    }

    // MARK: - Backup

    /// Create a ZIP of the library (excludes `.bispell/trash`). Returns zip URL in temp or next to library.
    public func createBackupZip(destinationDirectory: URL? = nil) throws -> URL {
        lock.lock()
        let rootPath = root.path
        let name = "BiSpell-library-\(LibraryPaths.dailyFileName())-\(String(UUID().uuidString.prefix(6))).zip"
        let destDir = destinationDirectory ?? FileManager.default.temporaryDirectory
        let zipURL = destDir.appendingPathComponent(name)
        lock.unlock()

        if FileManager.default.fileExists(atPath: zipURL.path) {
            try FileManager.default.removeItem(at: zipURL)
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/zip")
        process.currentDirectoryURL = URL(fileURLWithPath: rootPath)
        // Exclude trash contents
        process.arguments = ["-r", "-q", zipURL.path, ".", "-x", ".bispell/trash/*", "-x", "*.DS_Store"]
        let err = Pipe()
        process.standardError = err
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0, FileManager.default.fileExists(atPath: zipURL.path) else {
            throw NotesStoreError.backupFailed
        }
        return zipURL
    }

    // MARK: - Reload single note from disk

    public func reloadNote(id: UUID) throws -> Note? {
        lock.lock()
        defer { lock.unlock() }
        guard let ref = refs[id] else { return nil }
        return try loadNoteFromRefUnlocked(ref)
    }

    // MARK: - Private

    private func ensureSkeletonUnlocked() throws {
        let fm = FileManager.default
        try fm.createDirectory(at: root, withIntermediateDirectories: true)
        for sub in [LibraryPaths.inbox, LibraryPaths.daily, LibraryPaths.templates, LibraryPaths.bispellDir, LibraryPaths.trashDir] {
            try fm.createDirectory(
                at: root.appendingPathComponent(sub, isDirectory: true),
                withIntermediateDirectories: true
            )
        }
        let manifestURL = root.appendingPathComponent(LibraryPaths.bispellDir, isDirectory: true)
            .appendingPathComponent("library.json")
        if !fm.fileExists(atPath: manifestURL.path) {
            let manifest = LibraryManifest()
            let data = try encoder.encode(manifest)
            try data.write(to: manifestURL, options: .atomic)
        }
    }

    private func updateManifestUnlocked(_ mutate: (inout LibraryManifest) -> Void) throws {
        let manifestURL = root.appendingPathComponent(LibraryPaths.bispellDir, isDirectory: true)
            .appendingPathComponent("library.json")
        var manifest: LibraryManifest
        if let data = try? Data(contentsOf: manifestURL),
           let decoded = try? decoder.decode(LibraryManifest.self, from: data) {
            manifest = decoded
        } else {
            manifest = LibraryManifest()
        }
        mutate(&manifest)
        let data = try encoder.encode(manifest)
        try data.write(to: manifestURL, options: .atomic)
    }

    private func enumerateMarkdownFilesUnlocked() throws -> [URL] {
        var result: [URL] = []
        let fm = FileManager.default
        guard let enumerator = fm.enumerator(
            at: root,
            includingPropertiesForKeys: [.isRegularFileKey],
            options: [.skipsHiddenFiles]
        ) else { return [] }
        for case let url as URL in enumerator {
            // Skip .bispell (including trash)
            let rel = url.path.replacingOccurrences(of: root.path + "/", with: "")
            if rel.hasPrefix(LibraryPaths.bispellDir) || rel.contains("/\(LibraryPaths.bispellDir)/") {
                enumerator.skipDescendants()
                continue
            }
            if url.pathExtension.lowercased() == "md" {
                result.append(url)
            }
        }
        return result
    }

    private func loadNotePairUnlocked(mdURL: URL) throws -> Note {
        // Archive dumps dropped on the library root must live under inbox/ so
        // fileURL / save / reveal / mtime watch resolve the real on-disk path.
        let mdURL = try promoteRootMarkdownIntoInboxUnlocked(mdURL)

        let bodyRaw = try readTextFileUnlocked(mdURL)
        let scURL = mdURL.deletingPathExtension().appendingPathExtension("bispell.json")
        let folder = relativeFolderPathUnlocked(mdURL.deletingLastPathComponent()) ?? LibraryPaths.inbox
        let basename = mdURL.deletingPathExtension().lastPathComponent

        let fmParsed = NoteFrontMatter.parse(bodyRaw)
        let body = fmParsed.body

        let note: Note
        if FileManager.default.fileExists(atPath: scURL.path) {
            // Sidecar present: must decode successfully. Never overwrite a corrupt sidecar.
            let data: Data
            do {
                data = try Data(contentsOf: scURL)
            } catch {
                throw NotesStoreError.corruptSidecar
            }
            let sidecar: NoteSidecar
            do {
                sidecar = try decoder.decode(NoteSidecar.self, from: data)
            } catch {
                throw NotesStoreError.corruptSidecar
            }
            let title = sidecar.title ?? fmParsed.title ?? firstHeading(in: body) ?? basename
            var tags = sidecar.tags
            if tags.isEmpty, !fmParsed.tags.isEmpty {
                tags = fmParsed.tags
            }
            // Body drift: prefer on-disk body; locks may be stale — still load them
            let isTemplate = sidecar.isTemplate || folder == LibraryPaths.templates
            note = Note(
                id: sidecar.id,
                title: title,
                body: body,
                createdAt: sidecar.createdAt,
                updatedAt: sidecar.updatedAt,
                isTemplate: isTemplate,
                lockedSpans: sidecar.lockedSpans,
                folder: folder,
                tags: tags
            )
        } else {
            // Auto-create sidecar only for foreign MD with no sidecar yet
            let title = fmParsed.title ?? firstHeading(in: body) ?? basename
            let isTemplate = folder == LibraryPaths.templates
            note = Note(
                title: title,
                body: body,
                isTemplate: isTemplate,
                folder: folder,
                tags: fmParsed.tags
            )
            let ref = NoteFileRef(folder: folder, basename: basename)
            refs[note.id] = ref
            try writeSidecarUnlocked(note: note, ref: ref, body: bodyRaw)
            return note
        }

        let ref = NoteFileRef(folder: folder, basename: basename)
        refs[note.id] = ref
        return note
    }

    /// If `mdURL` sits directly under the library root (not a subfolder), move the
    /// MD+sidecar pair into `inbox/` before indexing. Avoids inventing an `inbox/`
    /// ref while files remain at root (which would miss on subsequent save/reveal).
    private func promoteRootMarkdownIntoInboxUnlocked(_ mdURL: URL) throws -> URL {
        let parent = mdURL.deletingLastPathComponent().standardizedFileURL
        let rootStd = root.standardizedFileURL
        guard parent.path == rootStd.path else { return mdURL }

        let basename = mdURL.deletingPathExtension().lastPathComponent
        let inboxDir = directoryURLUnlocked(folder: LibraryPaths.inbox)
        try FileManager.default.createDirectory(at: inboxDir, withIntermediateDirectories: true)

        // Prefer original basename; suffix on collision with an existing inbox file/ref.
        // Unknown id yet — use a fresh UUID so we never treat another note's file as ours.
        let destRef = uniqueRefUnlocked(
            NoteFileRef(folder: LibraryPaths.inbox, basename: basename),
            excluding: UUID()
        )
        let destMD = markdownURLUnlocked(ref: destRef)
        let destSC = sidecarURLUnlocked(ref: destRef)
        let scFrom = mdURL.deletingPathExtension().appendingPathExtension("bispell.json")
        try movePairUnlocked(mdFrom: mdURL, scFrom: scFrom, mdTo: destMD, scTo: destSC)
        return destMD
    }

    /// Read text with encoding fallbacks shared by load + bulk import.
    /// Order: UTF-8 → UTF-16 only when BOM present → ISO-Latin-1.
    /// (Bare `.utf16` accepts arbitrary bytes and would shadow Latin-1.)
    private func readTextFileUnlocked(_ url: URL) throws -> String {
        if let utf8 = try? String(contentsOf: url, encoding: .utf8) {
            return utf8
        }
        if let data = try? Data(contentsOf: url), data.count >= 2 {
            let b0 = data[0], b1 = data[1]
            let hasUTF16BOM = (b0 == 0xFF && b1 == 0xFE) || (b0 == 0xFE && b1 == 0xFF)
            if hasUTF16BOM, let utf16 = String(data: data, encoding: .utf16) {
                return utf16
            }
        }
        if let latin = try? String(contentsOf: url, encoding: .isoLatin1) {
            return latin
        }
        throw NotesStoreError.unreadableEncoding
    }

    /// Move markdown + sidecar together; roll MD back if sidecar move fails.
    private func movePairUnlocked(mdFrom: URL, scFrom: URL, mdTo: URL, scTo: URL) throws {
        let fm = FileManager.default
        try fm.moveItem(at: mdFrom, to: mdTo)
        guard fm.fileExists(atPath: scFrom.path) else { return }
        do {
            try fm.moveItem(at: scFrom, to: scTo)
        } catch {
            // Best-effort rollback so refs/callers still see a consistent pair location.
            try? fm.moveItem(at: mdTo, to: mdFrom)
            throw error
        }
    }

    private func loadNoteFromRefUnlocked(_ ref: NoteFileRef) throws -> Note {
        let md = markdownURLUnlocked(ref: ref)
        return try loadNotePairUnlocked(mdURL: md)
    }

    private func saveUnlocked(_ note: Note) throws {
        try ensureSkeletonUnlocked()
        var note = note
        // Default folder
        if note.isTemplate {
            if note.folder == nil || note.folder?.isEmpty == true || note.folder == LibraryPaths.inbox {
                note.folder = LibraryPaths.templates
            } else {
                note.folder = LibraryPaths.normalizeFolder(note.folder)
            }
        } else {
            note.folder = LibraryPaths.normalizeFolder(note.folder)
        }

        var ref: NoteFileRef
        if let existing = refs[note.id] {
            // Keep basename stable; move files if folder changed
            if existing.folder != (note.folder ?? LibraryPaths.inbox) {
                let moved = try moveUnlocked(id: note.id, toFolder: note.folder)
                note.folder = moved.folder
                ref = refs[note.id]!
            } else {
                ref = existing
            }
        } else {
            let base = LibraryPaths.sanitizeBasename(from: note.title, id: note.id)
            ref = uniqueRefUnlocked(NoteFileRef(folder: note.folder ?? LibraryPaths.inbox, basename: base), excluding: note.id)
            refs[note.id] = ref
        }

        let dir = directoryURLUnlocked(folder: ref.folder)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        // Body file is pure markdown (tags/title/locks in sidecar).
        // YAML front-matter is read on load/import only; write-back is not implemented.
        let diskBody = note.body
        let mdURL = markdownURLUnlocked(ref: ref)
        try atomicWriteString(diskBody, to: mdURL)
        try writeSidecarUnlocked(note: note, ref: ref, body: diskBody)
        refs[note.id] = ref
    }

    private func moveUnlocked(id: UUID, toFolder rawFolder: String?) throws -> Note {
        guard let ref = refs[id] else { throw NotesStoreError.noteNotFound }
        let newFolder = LibraryPaths.normalizeFolder(rawFolder)
        if ref.folder == newFolder {
            return try loadNoteFromRefUnlocked(ref)
        }
        let oldMD = markdownURLUnlocked(ref: ref)
        let oldSC = sidecarURLUnlocked(ref: ref)
        guard FileManager.default.fileExists(atPath: oldMD.path) else {
            throw NotesStoreError.fileMissing
        }
        var destRef = NoteFileRef(folder: newFolder, basename: ref.basename)
        destRef = uniqueRefUnlocked(destRef, excluding: id)
        let destDir = directoryURLUnlocked(folder: destRef.folder)
        try FileManager.default.createDirectory(at: destDir, withIntermediateDirectories: true)
        let newMD = markdownURLUnlocked(ref: destRef)
        let newSC = sidecarURLUnlocked(ref: destRef)
        // uniqueRef avoids collisions; never delete destination files belonging to another note.
        try movePairUnlocked(mdFrom: oldMD, scFrom: oldSC, mdTo: newMD, scTo: newSC)
        refs[id] = destRef
        var note = try loadNoteFromRefUnlocked(destRef)
        note.folder = destRef.folder
        try writeSidecarUnlocked(note: note, ref: destRef, body: note.body)
        return note
    }

    private func writeSidecarUnlocked(note: Note, ref: NoteFileRef, body: String) throws {
        let hash = NoteBodyHash.sha256(body)
        let sc = NoteSidecar(
            id: note.id,
            title: note.title,
            createdAt: note.createdAt,
            updatedAt: note.updatedAt,
            isTemplate: note.isTemplate,
            tags: note.tags,
            lockedSpans: note.lockedSpans,
            folder: ref.folder,
            bodySHA256: hash
        )
        let data = try encoder.encode(sc)
        try data.write(to: sidecarURLUnlocked(ref: ref), options: .atomic)
    }

    private func uniqueRefUnlocked(_ ref: NoteFileRef, excluding id: UUID) -> NoteFileRef {
        var candidate = ref
        var n = 0
        while true {
            let md = markdownURLUnlocked(ref: candidate)
            if !FileManager.default.fileExists(atPath: md.path) {
                // Also ensure no other ref points here
                let clash = refs.contains { $0.key != id && $0.value.folder == candidate.folder && $0.value.basename == candidate.basename }
                if !clash { return candidate }
            } else {
                // Same file belonging to this id is OK
                if let existing = refs[id], existing.folder == candidate.folder, existing.basename == candidate.basename {
                    return candidate
                }
                // Check if sidecar has same id
                let sc = sidecarURLUnlocked(ref: candidate)
                if let data = try? Data(contentsOf: sc),
                   let decoded = try? decoder.decode(NoteSidecar.self, from: data),
                   decoded.id == id {
                    return candidate
                }
            }
            n += 1
            let suffix = n == 1 ? String(id.uuidString.prefix(4)).lowercased() : "\(n)"
            candidate = NoteFileRef(folder: ref.folder, basename: "\(ref.basename)-\(suffix)")
            if n > 50 {
                candidate = NoteFileRef(folder: ref.folder, basename: "\(ref.basename)-\(String(id.uuidString.prefix(8)).lowercased())")
                return candidate
            }
        }
    }

    private func directoryURLUnlocked(folder: String) -> URL {
        let f = LibraryPaths.normalizeFolder(folder)
        var url = root
        for part in f.split(separator: "/") {
            url = url.appendingPathComponent(String(part), isDirectory: true)
        }
        return url
    }

    private func markdownURLUnlocked(ref: NoteFileRef) -> URL {
        directoryURLUnlocked(folder: ref.folder).appendingPathComponent("\(ref.basename).md")
    }

    private func sidecarURLUnlocked(ref: NoteFileRef) -> URL {
        directoryURLUnlocked(folder: ref.folder).appendingPathComponent("\(ref.basename).bispell.json")
    }

    private func relativeFolderPathUnlocked(_ dir: URL) -> String? {
        let rootPath = root.standardizedFileURL.path
        let dirPath = dir.standardizedFileURL.path
        guard dirPath.hasPrefix(rootPath) else { return nil }
        var rel = String(dirPath.dropFirst(rootPath.count))
        if rel.hasPrefix("/") { rel = String(rel.dropFirst()) }
        if rel.isEmpty { return LibraryPaths.inbox }
        if rel.hasPrefix(LibraryPaths.bispellDir) { return nil }
        return LibraryPaths.normalizeFolder(rel)
    }

    private func atomicWriteString(_ string: String, to url: URL) throws {
        let data = Data(string.utf8)
        try data.write(to: url, options: .atomic)
    }

    private func firstHeading(in text: String) -> String? {
        for line in text.split(separator: "\n", omittingEmptySubsequences: false).prefix(40) {
            let t = line.trimmingCharacters(in: .whitespaces)
            if t.hasPrefix("# ") {
                let title = t.dropFirst(2).trimmingCharacters(in: .whitespaces)
                if !title.isEmpty { return String(title.prefix(80)) }
            }
        }
        return nil
    }
}

public enum NotesStoreError: Error, LocalizedError, Equatable {
    case noteNotFound
    case fileMissing
    case destinationExists
    case backupFailed
    case corruptSidecar
    case unreadableEncoding
    case protectedFolder

    public var errorDescription: String? {
        switch self {
        case .noteNotFound: return "Note not found in library"
        case .fileMissing: return "Note file missing on disk"
        case .destinationExists: return "Destination folder already exists"
        case .backupFailed: return "Library backup failed"
        case .corruptSidecar: return "Note sidecar is corrupt or unreadable"
        case .unreadableEncoding: return "Could not read note file encoding"
        case .protectedFolder: return "Cannot delete this system folder"
        }
    }
}
