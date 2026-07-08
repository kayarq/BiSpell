import XCTest
@testable import BiSpellCore

final class NotesAppearanceTests: XCTestCase {
    func testMigratesLegacyThemeIDs() {
        XCTAssertEqual(NotesTheme.migrated(fromRaw: "system"), .phosphor)
        XCTAssertEqual(NotesTheme.migrated(fromRaw: "nightInk"), .phosphor)
        XCTAssertEqual(NotesTheme.migrated(fromRaw: "sakuraDusk"), .phosphor)
        XCTAssertEqual(NotesTheme.migrated(fromRaw: "paper"), .paperMono)
        XCTAssertEqual(NotesTheme.migrated(fromRaw: "parchmentLuxe"), .paperMono)
        XCTAssertEqual(NotesTheme.migrated(fromRaw: "roseQuartz"), .amber)
        XCTAssertEqual(NotesTheme.migrated(fromRaw: "phosphor"), .phosphor)
        XCTAssertEqual(NotesTheme.migrated(fromRaw: "unknown"), .phosphor)
    }

    func testMigratesLegacyFonts() {
        XCTAssertEqual(NotesFontOption.migrated(fromRaw: "system"), .sfMono)
        XCTAssertEqual(NotesFontOption.migrated(fromRaw: "palatino"), .sfMono)
        XCTAssertEqual(NotesFontOption.migrated(fromRaw: "avenirNext"), .avenirNext)
        XCTAssertEqual(NotesFontOption.migrated(fromRaw: "menlo"), .menlo)
    }

    func testSettingsRoundTripAndMigration() throws {
        let oldJSON = """
        {"theme":"paper","font":"system","fontSize":16}
        """.data(using: .utf8)!
        let decoded = try JSONDecoder().decode(NotesAppearanceSettings.self, from: oldJSON)
        XCTAssertEqual(decoded.theme, .paperMono)
        XCTAssertEqual(decoded.font, .sfMono)
        XCTAssertEqual(decoded.fontSize, 16)

        let data = try JSONEncoder().encode(decoded)
        let again = try JSONDecoder().decode(NotesAppearanceSettings.self, from: data)
        XCTAssertEqual(again.theme, .paperMono)
        XCTAssertEqual(again.font, .sfMono)
    }

    func testTokensExistForAllThemes() {
        for theme in NotesTheme.allCases {
            let t = theme.tokens()
            XCTAssertNotNil(t.editor)
            XCTAssertNotNil(t.accent)
            XCTAssertGreaterThan(t.chipRadius, 0)
        }
    }
}
