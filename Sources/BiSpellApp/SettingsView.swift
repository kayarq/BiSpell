import SwiftUI
import AppKit
import BiSpellCore

struct SettingsView: View {
    @ObservedObject var session: SpellSessionController
    var notesViewModel: NotesViewModel?
    @State private var newDeniedBundleID: String = ""
    @State private var libraryPathDraft: String = ""

    var body: some View {
        Form {
            Section("Notes library") {
                LabeledContent("Location") {
                    Text(libraryPathDraft.isEmpty ? session.settings.libraryPath : libraryPathDraft)
                        .font(.system(.caption, design: .monospaced))
                        .lineLimit(2)
                        .textSelection(.enabled)
                }
                HStack {
                    Button("Choose Folder…") { chooseLibraryFolder() }
                    Button("Reveal in Finder") {
                        if let vm = notesViewModel {
                            vm.revealLibraryInFinder()
                        } else {
                            NSWorkspace.shared.open(session.settings.libraryRootURL)
                        }
                    }
                }
                HStack {
                    Button("Migrate from App Support…") {
                        let n = notesViewModel?.migrateFromAppSupport() ?? 0
                        // status shown in notes
                        _ = n
                    }
                    .help("Import notes from ~/Library/Application Support/BiSpell/Notes without deleting them")
                    Button("Backup ZIP…") {
                        _ = notesViewModel?.backupLibrary()
                    }
                }
                if let vm = notesViewModel, vm.trashCount > 0 {
                    Button("Empty Trash (\(vm.trashCount))") {
                        vm.emptyTrash()
                    }
                }
                Text("Notes are Markdown files under this folder. Changing a note’s folder moves its files.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Section("Status") {
                LabeledContent("Accessibility") {
                    HStack {
                        Image(systemName: session.accessibilityGranted ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
                            .foregroundStyle(session.accessibilityGranted ? .green : .orange)
                        Text(session.accessibilityGranted ? "Granted" : "Missing")
                    }
                }
                if !session.accessibilityGranted {
                    Button("Request Permission") { session.requestAccessibility() }
                    Button("Open System Settings") { session.openAccessibilitySettings() }
                }
                LabeledContent("Focus") { Text(session.lastSnapshotSummary).lineLimit(2) }
                LabeledContent("Issues") { Text("\(session.misspellingCount)") }
            }

            Section("Checking") {
                Toggle("Enabled", isOn: binding(\.isEnabled))
                Toggle("Turkish", isOn: binding(\.turkishEnabled))
                Toggle("English", isOn: binding(\.englishEnabled))
                Toggle("Paused", isOn: $session.isPaused)
            }

            Section("Behavior") {
                Toggle("Launch at Login", isOn: binding(\.launchAtLogin))
                Toggle("Hotkey ⌥⌘. fallback", isOn: binding(\.hotkeyFallbackEnabled))
                Toggle("Clipboard replace fallback", isOn: binding(\.useClipboardFallback))
                Toggle("Electron / Chrome support", isOn: binding(\.electronSupportEnabled))
                Text("Off by default. When on, BiSpell may enable accessibility in Chromium apps (extra CPU/RAM there).")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Stepper(value: binding(\.debounceMilliseconds), in: 150...800, step: 50) {
                    Text("Debounce: \(session.settings.debounceMilliseconds) ms")
                }
            }

            Section("Denied apps (bundle IDs)") {
                ForEach(session.settings.deniedBundleIDs.sorted(), id: \.self) { bundleID in
                    HStack {
                        Text(bundleID)
                            .font(.system(.caption, design: .monospaced))
                            .textSelection(.enabled)
                        Spacer()
                        Button {
                            session.removeDeniedBundleID(bundleID)
                        } label: {
                            Image(systemName: "minus.circle.fill")
                                .foregroundStyle(.secondary)
                        }
                        .buttonStyle(.plain)
                        .help("Remove from denylist")
                    }
                }
                HStack {
                    TextField("com.example.app", text: $newDeniedBundleID)
                        .font(.system(.caption, design: .monospaced))
                        .textFieldStyle(.roundedBorder)
                        .onSubmit(addDeniedBundleID)
                    Button("Add", action: addDeniedBundleID)
                        .disabled(newDeniedBundleID.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
                Text("BiSpell never reads text in these apps.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Section("Personal dictionary") {
                let added = session.engine.lexicon.addedWords.sorted()
                if added.isEmpty {
                    Text("No words yet — use “Add to Dictionary” in the suggestion popup.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(added, id: \.self) { word in
                        HStack {
                            Text(word)
                            Spacer()
                            Button {
                                session.removeDictionaryWord(word)
                            } label: {
                                Image(systemName: "minus.circle.fill")
                                    .foregroundStyle(.secondary)
                            }
                            .buttonStyle(.plain)
                            .help("Remove from dictionary")
                        }
                    }
                }
            }

            Section("Ignored words") {
                let ignored = ignoredWordEntries()
                if ignored.isEmpty {
                    Text("Nothing ignored.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(ignored, id: \.self) { entry in
                        HStack {
                            Text(entry)
                            Spacer()
                            Button {
                                session.unignoreWord(baseWord(of: entry))
                            } label: {
                                Image(systemName: "arrow.uturn.backward.circle.fill")
                                    .foregroundStyle(.secondary)
                            }
                            .buttonStyle(.plain)
                            .help("Stop ignoring")
                        }
                    }
                }
            }

            Section("Support probe") {
                Button("Probe Frontmost App") { session.probeSupport() }
                if let s = session.lastSupport {
                    Text("\(s.appName) [\(s.tier.rawValue)] read=\(String(s.canReadValue)) sel=\(String(s.canReadSelection)) bounds=\(String(s.canReadBounds))")
                        .font(.caption)
                    Text(s.notes).font(.caption2).foregroundStyle(.secondary)
                }
                ForEach(session.supportMatrix(), id: \.bundleID) { sample in
                    Text("\(sample.tier.rawValue) · \(sample.appName) · \(sample.bundleID)")
                        .font(.caption2)
                }
            }
        }
        .formStyle(.grouped)
        .padding()
        .frame(minWidth: 420, minHeight: 520)
        .onAppear {
            libraryPathDraft = session.settings.libraryPath
        }
    }

    private func chooseLibraryFolder() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.prompt = "Use as Library"
        panel.message = "Choose the parent folder for BiSpell notes (Markdown + sidecars)."
        panel.directoryURL = session.settings.libraryRootURL
        panel.begin { resp in
            guard resp == .OK, let url = panel.url else { return }
            let display = LibraryPaths.displayPath(for: url)
            var s = session.settings
            s.libraryPath = display
            session.updateSettings(s)
            libraryPathDraft = display
            notesViewModel?.rebindLibrary(to: url, migrateLegacy: false)
        }
    }

    private func addDeniedBundleID() {
        session.addDeniedBundleID(newDeniedBundleID)
        newDeniedBundleID = ""
    }

    private func ignoredWordEntries() -> [String] {
        let lexicon = session.engine.lexicon
        var entries = lexicon.ignoredWords.sorted()
        for (bundleID, words) in lexicon.ignoredInApps.sorted(by: { $0.key < $1.key }) {
            entries.append(contentsOf: words.sorted().map { "\($0) — in \(bundleID)" })
        }
        return entries
    }

    private func baseWord(of entry: String) -> String {
        entry.components(separatedBy: " — in ").first ?? entry
    }

    private func binding<T>(_ keyPath: WritableKeyPath<AppSettings, T>) -> Binding<T> {
        Binding(
            get: { session.settings[keyPath: keyPath] },
            set: { newValue in
                var s = session.settings
                s[keyPath: keyPath] = newValue
                session.updateSettings(s)
            }
        )
    }
}
