import SwiftUI
import BiSpellCore

struct TemplateVariablesSheet: View {
    @Environment(\.notesTokens) private var t
    @Environment(\.dismiss) private var dismiss

    let keys: [String]
    @State private var values: [String: String]
    let onSubmit: ([String: String]) -> Void
    let onCancel: () -> Void

    init(keys: [String], initial: [String: String] = [:], onSubmit: @escaping ([String: String]) -> Void, onCancel: @escaping () -> Void) {
        self.keys = keys
        var v = initial
        for k in keys where v[k] == nil { v[k] = "" }
        _values = State(initialValue: v)
        self.onSubmit = onSubmit
        self.onCancel = onCancel
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Fill template fields")
                .font(.system(size: 14, weight: .bold, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.textPrimary))
            Text("Placeholders like {{name}} in unlocked text.")
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.textSecondary))

            ForEach(keys, id: \.self) { key in
                VStack(alignment: .leading, spacing: 4) {
                    Text("{{\(key)}}")
                        .font(.system(size: 11, weight: .semibold, design: .monospaced))
                        .foregroundStyle(Color(nsColor: t.accent))
                    TextField(key, text: binding(for: key))
                        .textFieldStyle(.roundedBorder)
                        .font(.system(size: 13, design: .monospaced))
                }
            }

            HStack {
                Spacer()
                Button("Cancel") {
                    onCancel()
                    dismiss()
                }
                .keyboardShortcut(.cancelAction)
                Button("Create note") {
                    onSubmit(values)
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .buttonStyle(.borderedProminent)
            }
        }
        .padding(20)
        .frame(minWidth: 360)
        .background(Color(nsColor: t.elevated))
    }

    private func binding(for key: String) -> Binding<String> {
        Binding(
            get: { values[key] ?? "" },
            set: { values[key] = $0 }
        )
    }
}
