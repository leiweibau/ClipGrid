import SwiftUI

struct ContentView: View {
    @ObservedObject var viewModel: AppViewModel
    @ObservedObject private var settings: AppSettings
    @State private var isDropTargeted = false
    @StateObject private var toolbarObserver = ToolbarDisplayModeObserver()

    init(viewModel: AppViewModel) {
        self.viewModel = viewModel
        _settings = ObservedObject(wrappedValue: viewModel.settings)
    }

    var body: some View {
        ZStack {
            NavigationSplitView {
                VStack(spacing: 0) {
                    if viewModel.isImporting {
                        VStack(alignment: .leading, spacing: 8) {
                            HStack {
                                Text(AppStrings.importingVideos)
                                    .font(.subheadline)
                                    .fontWeight(.medium)
                                Spacer()
                                Text("\(viewModel.importCompletedCount) / \(viewModel.importTotalCount)")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }

                            ProgressView(value: viewModel.importProgress)
                                .progressViewStyle(.linear)
                        }
                        .padding(12)
                        .background(Color.secondary.opacity(0.08))
                    }

                    List(selection: $viewModel.selectedVideoID) {
                        ForEach(viewModel.videos) { item in
                            VideoRow(
                                item: item,
                                fileSize: viewModel.formattedSize(for: item)
                            )
                                .tag(item.id)
                        }
                    }

                    Spacer(minLength: 0)

                    Divider()

                    HStack(spacing: 10) {
                        SidebarIconButton(systemImage: "plus", helpText: AppStrings.addVideosHelp) {
                            viewModel.chooseVideos()
                        }

                        SidebarIconButton(
                            systemImage: "trash",
                            helpText: AppStrings.removeSelectionHelp,
                            isDisabled: viewModel.selectedVideo == nil
                        ) {
                            viewModel.removeSelected()
                        }

                        SidebarIconButton(systemImage: "gearshape", helpText: AppStrings.settingsHelp) {
                            viewModel.isShowingSettings = true
                        }
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 10)
                }
                .navigationTitle(AppStrings.sidebarVideos)
                .onDeleteCommand {
                    viewModel.removeSelected()
                }
            } detail: {
                PreviewPane(viewModel: viewModel)
            }

            if isDropTargeted {
                RoundedRectangle(cornerRadius: 18)
                    .strokeBorder(style: StrokeStyle(lineWidth: 3, dash: [10]))
                    .foregroundStyle(.tint)
                    .padding(20)
                    .overlay {
                        VStack(spacing: 10) {
                            Image(systemName: "square.and.arrow.down")
                                .font(.system(size: 34, weight: .semibold))
                            Text(AppStrings.dropVideosHere)
                                .font(.title3)
                                .fontWeight(.semibold)
                        }
                        .padding(32)
                        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 18))
                    }
                    .allowsHitTesting(false)
            }
        }
        .sheet(isPresented: $viewModel.isShowingSettings) {
            SettingsView(
                settings: settings,
                backgroundColor: viewModel.backgroundColorBinding(),
                metadataTextColor: viewModel.metadataTextColorBinding()
            )
        }
        .toolbar {
            ToolbarItem(placement: .principal) {
                HStack(alignment: .center, spacing: 28) {
                    Button {
                        viewModel.clearAll()
                    } label: {
                        toolbarButtonLabel(
                            title: AppStrings.clearAllHelp,
                            systemImage: "trash.circle",
                            weight: .regular
                        )
                    }
                    .help(AppStrings.clearAllHelp)
                    .disabled(viewModel.videos.isEmpty || viewModel.isRendering || viewModel.isExporting)

                    Button {
                        Task {
                            await viewModel.startRendering()
                        }
                    } label: {
                        toolbarButtonLabel(
                            title: AppStrings.startHelp,
                            systemImage: "play.fill",
                            weight: .semibold,
                            tint: .green
                        )
                    }
                    .help(AppStrings.startHelp)
                    .disabled(viewModel.videos.isEmpty || viewModel.isRendering || viewModel.isExporting)
                }
            }
        }
        .alert(AppStrings.errorTitle, isPresented: Binding(
            get: { viewModel.lastError != nil },
            set: { isPresented in
                if !isPresented {
                    viewModel.lastError = nil
                }
            }
        )) {
            Button(AppStrings.ok, role: .cancel) {}
        } message: {
            Text(viewModel.lastError ?? "")
        }
        .onChange(of: settings.renderKey) { _ in
            viewModel.invalidatePreviews()
        }
        .onDrop(
            of: viewModel.supportedDropTypeIdentifiers,
            isTargeted: $isDropTargeted
        ) { providers in
            viewModel.handleDrop(providers: providers)
        }
        .background(
            ToolbarDisplayModeReader(observer: toolbarObserver)
                .frame(width: 0, height: 0)
        )
    }

    private var toolbarIconSize: CGFloat {
        switch toolbarObserver.displayMode {
        case .iconOnly:
            return 24
        case .iconAndLabel, .labelOnly, .default:
            return 14
        @unknown default:
            return 14
        }
    }

    @ViewBuilder
    private func toolbarButtonLabel(
        title: String,
        systemImage: String,
        weight: Font.Weight,
        tint: Color = .primary
    ) -> some View {
        if toolbarObserver.displayMode == .iconOnly {
            Image(systemName: systemImage)
                .resizable()
                .scaledToFit()
                .frame(width: toolbarIconSize, height: toolbarIconSize)
                .foregroundStyle(tint)
        } else {
            Label {
                Text(title)
            } icon: {
                Image(systemName: systemImage)
                    .font(.system(size: toolbarIconSize, weight: weight))
                    .foregroundStyle(tint)
            }
        }
    }
}

private struct SidebarIconButton: View {
    let systemImage: String
    let helpText: String
    var tint: Color = .primary
    var isDisabled = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: systemImage)
                .font(.system(size: 15, weight: .semibold))
                .frame(width: 30, height: 30)
                .foregroundStyle(isDisabled ? Color.secondary : tint)
                .background(
                    RoundedRectangle(cornerRadius: 8)
                        .fill(Color.secondary.opacity(0.12))
                )
        }
        .buttonStyle(.plain)
        .help(helpText)
        .disabled(isDisabled)
    }
}

private struct VideoRow: View {
    @ObservedObject var item: VideoItem
    let fileSize: String

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(item.fileName)
                .font(.headline)
                .lineLimit(1)

            Text(fileSize)
                .font(.subheadline)
                .foregroundStyle(.secondary)

            Text(item.status.label)
                .font(.caption)
                .foregroundStyle(statusColor)
                .lineLimit(1)
        }
        .padding(.vertical, 4)
    }

    private var statusColor: Color {
        switch item.status {
        case .failed:
            return .red
        case .exported:
            return .green
        default:
            return .secondary
        }
    }
}
