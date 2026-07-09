import XCTest
@testable import BiSpellCore

final class NotesStoreTests: XCTestCase {
    private var dir: URL!
    private var store: NotesStore!

    override func setUp() {
        super.setUp()
        dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("BiSpellNotesTests-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        store = NotesStore(libraryRoot: dir)
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: dir)
        super.tearDown()
    }

    func testSaveLoadDeleteRoundTrip() throws {
        var note = Note(title: "Hello", body: "World", folder: LibraryPaths.inbox)
        try store.save(note)
        var all = try store.loadAll()
        XCTAssertEqual(all.count, 1)
        XCTAssertEqual(all[0].title, "Hello")
        XCTAssertEqual(all[0].body, "World")
        XCTAssertEqual(all[0].folder, LibraryPaths.inbox)

        // MD + sidecar exist
        let rel = store.relativePath(for: note.id)
        XCTAssertNotNil(rel)
        XCTAssertTrue(rel!.hasSuffix(".md"))
        let mdURL = store.fileURL(for: note.id)!
        XCTAssertTrue(FileManager.default.fileExists(atPath: mdURL.path))
        let scURL = mdURL.deletingPathExtension().appendingPathExtension("bispell.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: scURL.path))

        note = all[0]
        note.body = "Updated"
        note.updatedAt = Date()
        try store.save(note)
        all = try store.loadAll()
        XCTAssertEqual(all[0].body, "Updated")

        try store.delete(id: note.id)
        all = try store.loadAll()
        XCTAssertTrue(all.isEmpty)
        XCTAssertEqual(store.trashItemCount(), 1)
    }

    func testMoveBetweenFolders() throws {
        var note = Note(title: "Movable", body: "x", folder: LibraryPaths.inbox)
        try store.save(note)
        _ = try store.loadAll()

        let moved = try store.move(id: note.id, toFolder: "work/ideas")
        XCTAssertEqual(moved.folder, "work/ideas")

        let url = store.fileURL(for: note.id)!
        XCTAssertTrue(url.path.contains("/work/ideas/"))
        XCTAssertTrue(FileManager.default.fileExists(atPath: url.path))

        let all = try store.loadAll()
        XCTAssertEqual(all.count, 1)
        XCTAssertEqual(all[0].folder, "work/ideas")
    }

    func testCollisionBasenames() throws {
        let a = Note(title: "Same Title", body: "a", folder: LibraryPaths.inbox)
        let b = Note(title: "Same Title", body: "b", folder: LibraryPaths.inbox)
        try store.save(a)
        try store.save(b)
        let all = try store.loadAll()
        XCTAssertEqual(all.count, 2)
        let paths = Set(all.compactMap { store.relativePath(for: $0.id) })
        XCTAssertEqual(paths.count, 2)
    }

    func testMigrationFromAppSupportJSON() throws {
        let legacy = FileManager.default.temporaryDirectory
            .appendingPathComponent("BiSpellLegacy-\(UUID().uuidString)", isDirectory: true)
            .appendingPathComponent("Notes", isDirectory: true)
        try FileManager.default.createDirectory(at: legacy, withIntermediateDirectories: true)

        let old = Note(title: "Legacy", body: "old body", folder: "Work", tags: ["x"])
        let enc = JSONEncoder()
        enc.dateEncodingStrategy = .iso8601
        let data = try enc.encode(old)
        try data.write(to: legacy.appendingPathComponent("\(old.id.uuidString).json"))

        let n = try store.migrateFromAppSupport(sourceDirectory: legacy)
        XCTAssertEqual(n, 1)
        let all = try store.loadAll()
        XCTAssertEqual(all.count, 1)
        XCTAssertEqual(all[0].title, "Legacy")
        XCTAssertEqual(all[0].body, "old body")
        XCTAssertEqual(all[0].folder, "Work")
        // Legacy JSON still present (backup)
        XCTAssertTrue(FileManager.default.fileExists(atPath: legacy.appendingPathComponent("\(old.id.uuidString).json").path))
        try? FileManager.default.removeItem(at: legacy.deletingLastPathComponent())
    }

    func testDisplayTitleFallsBackToBody() {
        let note = Note(title: "Untitled", body: "First line\nSecond")
        XCTAssertEqual(note.displayTitle, "First line")
    }

    func testPathNormalization() {
        XCTAssertEqual(LibraryPaths.normalizeFolder(nil), "inbox")
        XCTAssertEqual(LibraryPaths.normalizeFolder(""), "inbox")
        XCTAssertEqual(LibraryPaths.normalizeFolder("work / ideas"), "work/ideas")
        XCTAssertEqual(LibraryPaths.normalizeFolder("../x"), "x")
        XCTAssertEqual(LibraryPaths.normalizeFolder("a/b/c/d/e/f/g/h"), "a/b/c/d/e/f")
        XCTAssertFalse(LibraryPaths.normalizeFolder("..").contains(".."))
    }

    func testForeignMarkdownGetsSidecar() throws {
        let inbox = dir.appendingPathComponent("inbox", isDirectory: true)
        try FileManager.default.createDirectory(at: inbox, withIntermediateDirectories: true)
        let md = inbox.appendingPathComponent("foreign.md")
        try "# Foreign\n\nHello".write(to: md, atomically: true, encoding: .utf8)

        let all = try store.loadAll()
        XCTAssertEqual(all.count, 1)
        XCTAssertEqual(all[0].title, "Foreign")
        let sc = md.deletingPathExtension().appendingPathExtension("bispell.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: sc.path))
    }

    func testImportMarkdownFolder() throws {
        let src = FileManager.default.temporaryDirectory
            .appendingPathComponent("BiSpellImport-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: src, withIntermediateDirectories: true)
        try "# One\n\nbody".write(to: src.appendingPathComponent("one.md"), atomically: true, encoding: .utf8)
        try "---\ntitle: Two\ntags: [a, b]\n---\n\nbody2".write(
            to: src.appendingPathComponent("two.md"),
            atomically: true,
            encoding: .utf8
        )

        let imported = try store.importMarkdownFolder(from: src, destFolder: "archive/2026")
        XCTAssertEqual(imported.count, 2)
        let all = try store.loadAll()
        XCTAssertEqual(all.count, 2)
        XCTAssertTrue(all.allSatisfy { $0.folder == "archive/2026" })
        try? FileManager.default.removeItem(at: src)
    }

    func testRenameFolder() throws {
        var note = Note(title: "InFolder", body: "z", folder: "projects/alpha")
        try store.save(note)
        _ = try store.loadAll()
        try store.renameFolder(from: "projects/alpha", to: "projects/beta")
        let all = try store.loadAll()
        XCTAssertEqual(all[0].folder, "projects/beta")
        let url = store.fileURL(for: all[0].id)!
        XCTAssertTrue(url.path.contains("/projects/beta/"))
    }

    func testSkeletonCreated() {
        let inbox = dir.appendingPathComponent("inbox", isDirectory: true)
        let daily = dir.appendingPathComponent("daily", isDirectory: true)
        let templates = dir.appendingPathComponent("_templates", isDirectory: true)
        let lib = dir.appendingPathComponent(".bispell/library.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: inbox.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: daily.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: templates.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: lib.path))
    }

    func testLocksRoundTrip() throws {
        let span = LockedSpan(location: 0, length: 5, label: "Header")
        let note = Note(title: "Locked", body: "Hello world", lockedSpans: [span], folder: "inbox")
        try store.save(note)
        let all = try store.loadAll()
        XCTAssertEqual(all[0].lockedSpans.count, 1)
        XCTAssertEqual(all[0].lockedSpans[0].label, "Header")
        XCTAssertEqual(all[0].lockedSpans[0].length, 5)
    }

    func testCorrectionRanking() {
        let corrDir = FileManager.default.temporaryDirectory
            .appendingPathComponent("BiSpellCorrRank-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: corrDir, withIntermediateDirectories: true)
        let log = CorrectionLogStore(filename: "corrections.json", baseDirectory: corrDir)
        _ = log.record(wrong: "teh", correct: "the")
        _ = log.record(wrong: "teh", correct: "the")
        let ranked = log.rankSuggestions(["then", "the", "tea"], wrong: "teh")
        XCTAssertEqual(ranked.first, "the")
        try? FileManager.default.removeItem(at: corrDir)
    }

    func testDeleteMissingRefThrows() {
        XCTAssertThrowsError(try store.delete(id: UUID())) { error in
            XCTAssertEqual(error as? NotesStoreError, .noteNotFound)
        }
    }

    func testCorruptSidecarDoesNotOverwrite() throws {
        let note = Note(title: "Broken", body: "body", folder: LibraryPaths.inbox)
        try store.save(note)
        _ = try store.loadAll()
        let md = store.fileURL(for: note.id)!
        let sc = md.deletingPathExtension().appendingPathExtension("bispell.json")
        try "{ not valid json".write(to: sc, atomically: true, encoding: .utf8)

        // Single-note reload surfaces a hard error (does not rewrite sidecar)
        XCTAssertThrowsError(try store.reloadNote(id: note.id)) { error in
            XCTAssertEqual(error as? NotesStoreError, .corruptSidecar)
        }
        XCTAssertEqual(try String(contentsOf: sc, encoding: .utf8), "{ not valid json")

        // loadAll skips the pair without rewriting the corrupt sidecar
        let all = try store.loadAll()
        XCTAssertFalse(all.contains(where: { $0.id == note.id }))
        XCTAssertEqual(try String(contentsOf: sc, encoding: .utf8), "{ not valid json")
    }

    func testMigrationSkipsExistingIDs() throws {
        let legacy = FileManager.default.temporaryDirectory
            .appendingPathComponent("BiSpellLegacySkip-\(UUID().uuidString)", isDirectory: true)
            .appendingPathComponent("Notes", isDirectory: true)
        try FileManager.default.createDirectory(at: legacy, withIntermediateDirectories: true)

        var note = Note(title: "KeepMe", body: "library body", folder: LibraryPaths.inbox)
        try store.save(note)
        _ = try store.loadAll()

        // Legacy file with same UUID but stale body
        note.body = "stale app support body"
        let enc = JSONEncoder()
        enc.dateEncodingStrategy = .iso8601
        try enc.encode(note).write(to: legacy.appendingPathComponent("\(note.id.uuidString).json"))

        let n = try store.migrateFromAppSupport(sourceDirectory: legacy)
        XCTAssertEqual(n, 0)
        let all = try store.loadAll()
        XCTAssertEqual(all.count, 1)
        XCTAssertEqual(all[0].body, "library body")
        try? FileManager.default.removeItem(at: legacy.deletingLastPathComponent())
    }

    func testLatin1MarkdownLoads() throws {
        let inbox = dir.appendingPathComponent("inbox", isDirectory: true)
        try FileManager.default.createDirectory(at: inbox, withIntermediateDirectories: true)
        let md = inbox.appendingPathComponent("latin.md")
        // ISO-Latin-1 bytes for "café" (e-acute = 0xE9)
        let bytes: [UInt8] = Array("caf".utf8) + [0xE9]
        try Data(bytes).write(to: md)

        let all = try store.loadAll()
        XCTAssertEqual(all.count, 1)
        XCTAssertTrue(all[0].body.contains("caf"))
    }

    func testWriteYAMLFrontMatterDefaultsFalse() {
        XCTAssertFalse(AppSettings.default.writeYAMLFrontMatter)
    }

    func testRootLevelMarkdownPromotedToInbox() throws {
        // Archive dump dropped on library root — must move into inbox/ on load.
        let rootMD = dir.appendingPathComponent("dumped.md")
        try "# Dump\n\nhello from root".write(to: rootMD, atomically: true, encoding: .utf8)

        let all = try store.loadAll()
        XCTAssertEqual(all.count, 1)
        XCTAssertEqual(all[0].folder, LibraryPaths.inbox)
        XCTAssertEqual(all[0].title, "Dump")
        // Original root path is gone; live path is under inbox/
        XCTAssertFalse(FileManager.default.fileExists(atPath: rootMD.path))
        let url = store.fileURL(for: all[0].id)!
        XCTAssertTrue(url.path.contains("/inbox/"))
        XCTAssertTrue(FileManager.default.fileExists(atPath: url.path))
        // Sidecar created next to the promoted body
        let sc = url.deletingPathExtension().appendingPathExtension("bispell.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: sc.path))

        // Subsequent save must hit the inbox copy, not invent a second root file
        var note = all[0]
        note.body = "updated"
        note.updatedAt = Date()
        try store.save(note)
        XCTAssertFalse(FileManager.default.fileExists(atPath: rootMD.path))
        XCTAssertEqual(try String(contentsOf: url, encoding: .utf8), "updated")
    }

    func testRootLevelPairPromotedWithSidecar() throws {
        let rootMD = dir.appendingPathComponent("paired.md")
        try "body".write(to: rootMD, atomically: true, encoding: .utf8)
        let id = UUID()
        let sc = NoteSidecar(
            id: id,
            title: "Paired Root",
            createdAt: Date(),
            updatedAt: Date(),
            isTemplate: false,
            tags: ["root"],
            lockedSpans: [],
            bodySHA256: NoteBodyHash.sha256("body")
        )
        let enc = JSONEncoder()
        enc.dateEncodingStrategy = .iso8601
        try enc.encode(sc).write(
            to: dir.appendingPathComponent("paired.bispell.json"),
            options: .atomic
        )

        let all = try store.loadAll()
        XCTAssertEqual(all.count, 1)
        XCTAssertEqual(all[0].id, id)
        XCTAssertEqual(all[0].folder, LibraryPaths.inbox)
        XCTAssertEqual(all[0].title, "Paired Root")
        XCTAssertFalse(FileManager.default.fileExists(atPath: rootMD.path))
        let url = store.fileURL(for: id)!
        XCTAssertTrue(url.path.contains("/inbox/paired"))
        XCTAssertTrue(FileManager.default.fileExists(atPath: url.path))
        let destSC = url.deletingPathExtension().appendingPathExtension("bispell.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: destSC.path))
    }

    func testDeleteRemovesPairFromIndex() throws {
        let note = Note(title: "TrashMe", body: "bye", folder: LibraryPaths.inbox)
        try store.save(note)
        _ = try store.loadAll()
        let md = store.fileURL(for: note.id)!
        let sc = md.deletingPathExtension().appendingPathExtension("bispell.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: md.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: sc.path))

        try store.delete(id: note.id)
        XCTAssertTrue(try store.loadAll().isEmpty)
        XCTAssertNil(store.fileURL(for: note.id))
        XCTAssertFalse(FileManager.default.fileExists(atPath: md.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: sc.path))
        // trashItemCount counts soft-deleted notes (md files only)
        XCTAssertEqual(store.trashItemCount(), 1)
        let trash = dir.appendingPathComponent(LibraryPaths.trashDir, isDirectory: true)
        let trashItems = try FileManager.default.contentsOfDirectory(at: trash, includingPropertiesForKeys: nil)
        XCTAssertEqual(trashItems.filter { $0.pathExtension.lowercased() == "md" }.count, 1)
        XCTAssertEqual(trashItems.filter { $0.lastPathComponent.hasSuffix(".bispell.json") }.count, 1)
    }

    func testRewriteTagRenameAndDelete() throws {
        var a = Note(title: "A", body: "a", folder: LibraryPaths.inbox, tags: ["work", "draft"])
        var b = Note(title: "B", body: "b", folder: LibraryPaths.inbox, tags: ["Work", "personal"])
        var c = Note(title: "C", body: "c", folder: LibraryPaths.inbox, tags: ["other"])
        try store.save(a)
        try store.save(b)
        try store.save(c)
        _ = try store.loadAll()

        let renamed = try store.rewriteTag(from: "work", to: "job")
        XCTAssertEqual(renamed, 2)
        let afterRename = try store.loadAll()
        let a2 = afterRename.first { $0.id == a.id }!
        let b2 = afterRename.first { $0.id == b.id }!
        let c2 = afterRename.first { $0.id == c.id }!
        XCTAssertTrue(a2.tags.map { $0.lowercased() }.contains("job"))
        XCTAssertFalse(a2.tags.map { $0.lowercased() }.contains("work"))
        XCTAssertTrue(b2.tags.map { $0.lowercased() }.contains("job"))
        XCTAssertEqual(c2.tags, ["other"])

        let removed = try store.rewriteTag(from: "job", to: nil)
        XCTAssertEqual(removed, 2)
        let afterDelete = try store.loadAll()
        XCTAssertFalse(afterDelete.flatMap(\.tags).map { $0.lowercased() }.contains("job"))
        XCTAssertTrue(afterDelete.first { $0.id == a.id }!.tags.map { $0.lowercased() }.contains("draft"))
    }

    func testDeleteFolderMovesNotesToInbox() throws {
        let nested = Note(title: "Nest", body: "n", folder: "archive/2026")
        let top = Note(title: "Top", body: "t", folder: "archive")
        let keep = Note(title: "Keep", body: "k", folder: "work")
        try store.save(nested)
        try store.save(top)
        try store.save(keep)
        _ = try store.loadAll()

        let moved = try store.deleteFolderMovingNotesToInbox("archive")
        XCTAssertEqual(moved, 2)
        let all = try store.loadAll()
        XCTAssertEqual(all.first { $0.id == nested.id }?.folder, LibraryPaths.inbox)
        XCTAssertEqual(all.first { $0.id == top.id }?.folder, LibraryPaths.inbox)
        XCTAssertEqual(all.first { $0.id == keep.id }?.folder, "work")
        XCTAssertTrue(store.fileURL(for: nested.id)!.path.contains("/inbox/"))
    }

    func testCannotDeleteInboxFolder() throws {
        XCTAssertThrowsError(try store.deleteFolderMovingNotesToInbox("inbox")) { err in
            XCTAssertEqual(err as? NotesStoreError, .protectedFolder)
        }
    }
}
