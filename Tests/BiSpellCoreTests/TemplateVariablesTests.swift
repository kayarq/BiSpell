import XCTest
@testable import BiSpellCore

final class TemplateVariablesTests: XCTestCase {
    func testScanAndOrderedKeys() {
        let body = "Hi {{name}}, re {{topic}} and {{name}} again."
        let keys = TemplateVariables.orderedKeys(in: body)
        XCTAssertEqual(keys, ["name", "topic"])
    }

    func testFillUnlockedOnlyBackToFront() {
        // "AA {{x}} BB LOCK {{y}} CC" — lock covers "{{y}}"
        let body = "AA {{x}} BB {{y}} CC"
        // lock from index of {{y}}
        let yRange = (body as NSString).range(of: "{{y}}")
        let locks = [LockedSpan(location: yRange.location, length: yRange.length, label: "Y")]
        let result = TemplateVariables.fill(
            body: body,
            locks: locks,
            values: ["x": "XXX", "y": "YYY"]
        )
        XCTAssertTrue(result.body.contains("XXX"))
        XCTAssertTrue(result.body.contains("{{y}}"), "locked placeholder must remain")
        XCTAssertFalse(result.body.contains("YYY"))
        XCTAssertEqual(result.filledCount, 1)
        XCTAssertEqual(result.skippedInLocks, 1)
        XCTAssertEqual(result.lockedSpans.count, 1)
        XCTAssertEqual(result.lockedSpans[0].label, "Y")
        // lock still covers {{y}} after length change before it
        let still = (result.body as NSString).substring(with: result.lockedSpans[0].utf16Range)
        XCTAssertEqual(still, "{{y}}")
    }

    func testOrderedKeysUnlockedSkipsLocks() {
        let body = "a {{a}} b {{b}}"
        let bRange = (body as NSString).range(of: "{{b}}")
        let keys = TemplateVariables.orderedKeysUnlocked(
            in: body,
            locks: [LockedSpan(range: bRange)]
        )
        XCTAssertEqual(keys, ["a"])
    }
}
