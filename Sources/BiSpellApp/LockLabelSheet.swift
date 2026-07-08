import SwiftUI
import BiSpellCore

struct LockLabelSheet: View {
    @Environment(\.notesTokens) private var t
    @Environment(\.dismiss) private var dismiss
    @State private var label: String = ""
    let onLock: (String?) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Lock selection")
                .font(.system(size: 14, weight: .bold, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.textPrimary))
            Text("Optional region name (e.g. Question, Footer)")
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(Color(nsColor: t.textSecondary))
            TextField("Label (optional)", text: $label)
                .textFieldStyle(.roundedBorder)
                .font(.system(size: 13, design: .monospaced))
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }
                Button("Lock") {
                    onLock(label)
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .buttonStyle(.borderedProminent)
            }
        }
        .padding(20)
        .frame(minWidth: 320)
        .background(Color(nsColor: t.elevated))
    }
}
