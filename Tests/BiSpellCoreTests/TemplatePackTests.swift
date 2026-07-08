import XCTest
@testable import BiSpellCore

final class TemplatePackTests: XCTestCase {
    func testJSONRoundTrip() throws {
        let note = Note(
            title: "T1",
            body: "Hello {{name}}",
            isTemplate: true,
            lockedSpans: [LockedSpan(location: 0, length: 5, label: "Hi")],
            folder: "work",
            tags: ["email", "tr"]
        )
        let pack = TemplatePack.pack(from: [note])
        let data = try TemplatePack.encodeJSON(pack)
        let decoded = try TemplatePack.decodeJSON(data)
        XCTAssertEqual(decoded.templates.count, 1)
        XCTAssertEqual(decoded.templates[0].title, "T1")
        XCTAssertEqual(decoded.templates[0].lockedSpans[0].label, "Hi")
        XCTAssertEqual(decoded.templates[0].folder, "work")
        XCTAssertEqual(decoded.templates[0].tags, ["email", "tr"])
    }

    func testMarkdownRoundTrip() throws {
        let item = TemplatePackItem(
            title: "MD",
            body: "Body {{x}}\n",
            lockedSpans: [LockedSpan(location: 0, length: 4, label: "Head")],
            folder: "school",
            tags: ["a"]
        )
        let md = TemplatePack.exportMarkdown(item)
        let parsed = try TemplatePack.parseMarkdown(md)
        XCTAssertEqual(parsed.title, "MD")
        XCTAssertEqual(parsed.body.trimmingCharacters(in: .newlines), "Body {{x}}")
        XCTAssertEqual(parsed.folder, "school")
        XCTAssertEqual(parsed.tags, ["a"])
        XCTAssertEqual(parsed.lockedSpans.count, 1)
        XCTAssertEqual(parsed.lockedSpans[0].label, "Head")
        XCTAssertEqual(parsed.lockedSpans[0].location, 0)
        XCTAssertEqual(parsed.lockedSpans[0].length, 4)
    }

    func testTagNormalize() {
        let tags = NoteTagging.normalizeTags([" Work ", "work", "Email", "email "])
        XCTAssertEqual(tags.count, 2)
    }
}
