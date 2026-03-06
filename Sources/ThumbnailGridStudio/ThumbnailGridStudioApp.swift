import SwiftUI

@main
struct ThumbnailGridStudioApp: App {
    @StateObject private var viewModel = AppViewModel()

    var body: some Scene {
        WindowGroup {
            ContentView(viewModel: viewModel)
                .frame(minWidth: 1100, minHeight: 700)
        }
        .windowResizability(.contentMinSize)
        .commands {
            CommandGroup(after: .appInfo) {
                Button(AppStrings.checkForUpdatesMenu) {
                    Task {
                        await viewModel.checkForUpdates()
                    }
                }
                .disabled(viewModel.isCheckingForUpdates)
            }
        }
    }
}
