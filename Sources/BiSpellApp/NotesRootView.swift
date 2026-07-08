import SwiftUI
import AppKit
import BiSpellCore

struct NotesRootView: View {
    @ObservedObject var viewModel: NotesViewModel
    @ObservedObject var appearance: NotesAppearanceController
    @Environment(\.colorScheme) private var colorScheme

    @State private var confirmDelete = false
    @State private var pendingSelection: UUID?
    @State private var showDirtySwitchAlert = false
    @State private var showDirtyNewAlert = false
    @State private var pendingNewAsTemplate = false
    @State private var pendingTemplateID: UUID?
    @State private var showDirtyFromTemplateAlert = false

    private var tokens: NotesThemeTokens {
        appearance.tokens(colorScheme: colorScheme)
    }

    var body: some View {
        NavigationSplitView {
            sidebar
        } detail: {
            detail
        }
        .navigationTitle("BiSpell Notes")
        .toolbarBackground(Color(nsColor: tokens.chromeBar), for: .windowToolbar)
        .environment(\.notesTokens, tokens)
        .background(Color(nsColor: tokens.window))
        .alert("Delete this note?", isPresented: $confirmDelete) {
            Button("Delete", role: .destructive) { viewModel.deleteSelected() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This cannot be undone.")
        }
        .alert("Unsaved changes", isPresented: $showDirtySwitchAlert) {
            Button("Save") {
                viewModel.save()
                if let id = pendingSelection { _ = viewModel.select(id: id, force: true) }
                pendingSelection = nil
            }
            Button("Discard", role: .destructive) {
                if let id = pendingSelection { _ = viewModel.select(id: id, force: true) }
                pendingSelection = nil
            }
            Button("Cancel", role: .cancel) { pendingSelection = nil }
        } message: {
            Text("Save changes before switching notes?")
        }
        .alert("Unsaved changes", isPresented: $showDirtyNewAlert) {
            Button("Save") {
                viewModel.save()
                viewModel.createNote(saveImmediately: true, asTemplate: pendingNewAsTemplate)
                pendingNewAsTemplate = false
            }
            Button("Discard", role: .destructive) {
                if let id = viewModel.selectedNoteID {
                    _ = viewModel.select(id: id, force: true)
                }
                viewModel.createNote(saveImmediately: true, asTemplate: pendingNewAsTemplate)
                pendingNewAsTemplate = false
            }
            Button("Cancel", role: .cancel) {
                pendingNewAsTemplate = false
            }
        } message: {
            Text(pendingNewAsTemplate
                  ? "Save the current note before creating a new template?"
                  : "Save the current note before creating a new one?")
        }
        .alert("Unsaved changes", isPresented: $showDirtyFromTemplateAlert) {
            Button("Save") {
                viewModel.save()
                if let id = pendingTemplateID {
                    _ = viewModel.createNoteFromTemplate(id, force: true)
                }
                pendingTemplateID = nil
            }
            Button("Discard", role: .destructive) {
                if let id = viewModel.selectedNoteID {
                    _ = viewModel.select(id: id, force: true)
                }
                if let tid = pendingTemplateID {
                    _ = viewModel.createNoteFromTemplate(tid, force: true)
                }
                pendingTemplateID = nil
            }
            Button("Cancel", role: .cancel) {
                pendingTemplateID = nil
            }
        } message: {
            Text("Save the current note before creating one from a template?")
        }
    }

    private var sidebar: some View {
        VStack(spacing: 0) {
            // Terminal header
            HStack(spacing: 6) {
                Text("biSpell")
                    .font(.system(size: 11, weight: .bold, design: .monospaced))
                    .foregroundStyle(Color(nsColor: tokens.accent))
                Text("// notes")
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(Color(nsColor: tokens.textTertiary))
                Spacer()
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .background(Color(nsColor: tokens.sidebar))
            .overlay(alignment: .bottom) {
                Rectangle().fill(Color(nsColor: tokens.borderSubtle)).frame(height: 1)
            }

            List(selection: Binding(
                get: { viewModel.selectedNoteID },
                set: { attemptSelect($0) }
            )) {
                Section {
                    ForEach(viewModel.regularNotes) { note in
                        NotesSidebarRow(
                            note: note,
                            isTemplate: false,
                            isSelected: viewModel.selectedNoteID == note.id,
                            isDirty: viewModel.selectedNoteID == note.id && viewModel.isDirty
                        )
                        .tag(note.id)
                        .listRowBackground(Color(nsColor: tokens.sidebar))
                        .listRowInsets(EdgeInsets(top: 2, leading: 6, bottom: 2, trailing: 6))
                        .contextMenu { noteContextMenu(note, isTemplate: false) }
                    }
                } header: {
                    NotesSectionHeader(title: "Notes")
                }

                Section {
                    ForEach(viewModel.templateNotes) { note in
                        NotesSidebarRow(
                            note: note,
                            isTemplate: true,
                            isSelected: viewModel.selectedNoteID == note.id,
                            isDirty: viewModel.selectedNoteID == note.id && viewModel.isDirty
                        )
                        .tag(note.id)
                        .listRowBackground(Color(nsColor: tokens.sidebar))
                        .listRowInsets(EdgeInsets(top: 2, leading: 6, bottom: 2, trailing: 6))
                        .contextMenu { noteContextMenu(note, isTemplate: true) }
                    }
                } header: {
                    NotesSectionHeader(title: "Templates")
                }
            }
            .listStyle(.sidebar)
            .scrollContentBackground(.hidden)
            .background(Color(nsColor: tokens.sidebar))
            .searchable(text: $viewModel.searchText, prompt: "search…")

            if viewModel.regularNotes.isEmpty && viewModel.templateNotes.isEmpty {
                Text("// empty — create a note")
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(Color(nsColor: tokens.textTertiary))
                    .frame(maxWidth: .infinity)
                    .padding()
            }
        }
        .frame(minWidth: 220)
        .background(Color(nsColor: tokens.sidebar))
    }

    @ViewBuilder
    private func noteContextMenu(_ note: Note, isTemplate: Bool) -> some View {
        if isTemplate {
            Button("New Note from Template") {
                attemptNewFromTemplate(note.id)
            }
            Button("Move to Notes") {
                if viewModel.select(id: note.id, force: true) {
                    viewModel.convertTemplateToNote()
                }
            }
        } else {
            Button("Move to Templates") {
                if viewModel.select(id: note.id, force: true) {
                    viewModel.saveCurrentAsTemplate()
                }
            }
        }
        Button("Delete", role: .destructive) { viewModel.delete(id: note.id) }
    }

    @ViewBuilder
    private var detail: some View {
        if viewModel.selectedNoteID != nil {
            VStack(spacing: 0) {
                NotesCommandStrip(
                    viewModel: viewModel,
                    appearance: appearance,
                    onNewNote: attemptNewNote,
                    onNewTemplate: attemptNewTemplate,
                    onFromTemplate: attemptNewFromTemplate,
                    onDelete: { confirmDelete = true }
                )

                // Title row
                HStack(spacing: 10) {
                    Text("›")
                        .font(.system(size: 18, weight: .bold, design: .monospaced))
                        .foregroundStyle(Color(nsColor: tokens.accent))
                    TextField("untitled", text: viewModel.titleBinding)
                        .textFieldStyle(.plain)
                        .font(Font(appearance.titleFont()))
                        .foregroundStyle(Color(nsColor: tokens.textPrimary))
                    Spacer()
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                .background(Color(nsColor: tokens.chromeBar))
                .overlay(alignment: .bottom) {
                    Rectangle().fill(Color(nsColor: tokens.borderSubtle)).frame(height: 1)
                }

                NoteTextEditor(
                    editorBridge: viewModel.editorBridge,
                    text: viewModel.bodyBinding,
                    selectedRange: $viewModel.selectedRange,
                    activeMisspelling: viewModel.activeSuggestion,
                    lockedSpans: viewModel.draftLockedSpans,
                    editorFont: appearance.bodyFont(),
                    textColor: tokens.textPrimary,
                    backgroundColor: tokens.editor,
                    lockedBackgroundColor: tokens.lockFill,
                    accentColor: tokens.accent,
                    borderColor: tokens.borderStrong,
                    onEditingChanged: {
                        viewModel.markDirtyFromEditor()
                    },
                    onWordBoundary: {
                        viewModel.handleWordBoundary()
                    },
                    onCommandNumber: { n in
                        viewModel.applySuggestionShortcut(number: n)
                    },
                    onApplySuggestion: { suggestion, miss in
                        viewModel.applySuggestion(suggestion, for: miss)
                    },
                    onDismissSuggestions: {
                        viewModel.dismissSuggestions()
                    },
                    canEdit: { range, rep in
                        viewModel.canEdit(range: range, replacement: rep)
                    },
                    commitEditorChange: { newText, edited, replacement, preSpans in
                        viewModel.commitEditorChange(
                            newText: newText,
                            edited: edited,
                            replacement: replacement,
                            previousSpans: preSpans
                        )
                    },
                    smartDelete: { range in
                        viewModel.smartDelete(range: range)
                    },
                    currentLockedSpans: {
                        viewModel.draftLockedSpans
                    },
                    onBlockedEdit: {
                        viewModel.notifyBlockedEdit()
                    },
                    restoreSnapshot: { text, spans, sel in
                        viewModel.restoreSnapshot(text: text, spans: spans, selection: sel)
                    }
                )
                .padding(.horizontal, 4)
                .padding(.bottom, 2)

                NotesStatusBar(viewModel: viewModel, appearance: appearance)
            }
            .background(Color(nsColor: tokens.editor))
        } else {
            VStack(spacing: 12) {
                Text(">")
                    .font(.system(size: 28, weight: .bold, design: .monospaced))
                    .foregroundStyle(Color(nsColor: tokens.accent))
                Text("select a note or create one")
                    .font(.system(size: 13, design: .monospaced))
                    .foregroundStyle(Color(nsColor: tokens.textSecondary))
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color(nsColor: tokens.editor))
        }
    }

    private func attemptSelect(_ id: UUID?) {
        if viewModel.select(id: id) { return }
        pendingSelection = id
        showDirtySwitchAlert = true
    }

    private func attemptNewNote() {
        if viewModel.isDirty {
            pendingNewAsTemplate = false
            showDirtyNewAlert = true
        } else {
            viewModel.createNote(saveImmediately: true, asTemplate: false)
        }
    }

    private func attemptNewTemplate() {
        if viewModel.isDirty {
            pendingNewAsTemplate = true
            showDirtyNewAlert = true
        } else {
            viewModel.createNote(saveImmediately: true, asTemplate: true)
        }
    }

    private func attemptNewFromTemplate(_ id: UUID) {
        if viewModel.isDirty {
            pendingTemplateID = id
            showDirtyFromTemplateAlert = true
        } else {
            _ = viewModel.createNoteFromTemplate(id, force: true)
        }
    }
}
