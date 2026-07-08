import SwiftUI
import BiSpellCore

/// Renders note body as end-product Markdown (YAML front matter stripped).
struct MarkdownPreviewView: View {
    let markdown: String
    let tokens: NotesThemeTokens
    var pointSize: CGFloat = 14

    @State private var rendered: AttributedString = AttributedString()
    @State private var status: String?
    @State private var workItem: DispatchWorkItem?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 6) {
                    Text("preview")
                        .font(.system(size: 10, weight: .bold, design: .monospaced))
                        .foregroundStyle(Color(nsColor: tokens.accent))
                    Text("// end product")
                        .font(.system(size: 10, design: .monospaced))
                        .foregroundStyle(Color(nsColor: tokens.textTertiary))
                    if let status {
                        Text(status)
                            .font(.system(size: 10, design: .monospaced))
                            .foregroundStyle(Color(nsColor: tokens.dirty))
                    }
                    Spacer()
                }
                Text(rendered)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .lineSpacing(4)
            }
            .padding(16)
        }
        .background(Color(nsColor: tokens.editor))
        .onAppear { scheduleRender(markdown) }
        .onChange(of: markdown) { _, newValue in
            scheduleRender(newValue)
        }
        .onChange(of: pointSize) { _, _ in
            scheduleRender(markdown)
        }
    }

    private func scheduleRender(_ source: String) {
        workItem?.cancel()
        let size = pointSize
        let tok = tokens
        let item = DispatchWorkItem {
            let body = TemplatePack.previewBody(from: source)
            let result = Self.render(body, tokens: tok, pointSize: size)
            DispatchQueue.main.async {
                rendered = result.text
                status = result.status
            }
        }
        workItem = item
        DispatchQueue.global(qos: .userInitiated).asyncAfter(deadline: .now() + 0.12, execute: item)
    }

    private struct RenderResult {
        var text: AttributedString
        var status: String?
    }

    private static func render(_ source: String, tokens: NotesThemeTokens, pointSize: CGFloat) -> RenderResult {
        let trimmed = source.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty {
            var empty = AttributedString("∅ empty document")
            empty.foregroundColor = Color(nsColor: tokens.textTertiary)
            empty.font = .system(size: pointSize, design: .monospaced)
            return RenderResult(text: empty, status: nil)
        }

        var options = AttributedString.MarkdownParsingOptions()
        options.interpretedSyntax = .full
        options.failurePolicy = .returnPartiallyParsedIfPossible

        do {
            var attr = try AttributedString(markdown: source, options: options)
            // Base theme colors (presentation intents keep heading weights from parser).
            attr.foregroundColor = Color(nsColor: tokens.textPrimary)
            for run in attr.runs {
                let range = run.range
                if let intent = run.inlinePresentationIntent, intent.contains(.code) {
                    attr[range].font = .system(size: pointSize * 0.92, design: .monospaced)
                    attr[range].backgroundColor = Color(nsColor: tokens.elevated)
                    attr[range].foregroundColor = Color(nsColor: tokens.accentBright)
                }
                if run.link != nil {
                    attr[range].foregroundColor = Color(nsColor: tokens.accent)
                    attr[range].underlineStyle = .single
                }
            }
            return RenderResult(text: attr, status: nil)
        } catch {
            var plain = AttributedString(source)
            plain.foregroundColor = Color(nsColor: tokens.textPrimary)
            plain.font = .system(size: pointSize)
            return RenderResult(text: plain, status: "plain fallback")
        }
    }
}
