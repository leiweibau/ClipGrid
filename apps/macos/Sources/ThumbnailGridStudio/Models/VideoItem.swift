import AppKit
import Foundation

@MainActor
final class VideoItem: ObservableObject, Identifiable {
    enum Status: Equatable {
        case idle
        case generating
        case ready
        case exporting
        case exported(URL)
        case failed(String)

        var label: String {
            switch self {
            case .idle: AppStrings.statusReady
            case .generating: AppStrings.statusGenerating
            case .ready: AppStrings.statusReadyDone
            case .exporting: AppStrings.statusExporting
            case .exported: AppStrings.statusExported
            case .failed(let message): message
            }
        }
    }

    let id = UUID()
    let url: URL
    let fileName: String
    var duration: TimeInterval
    let fileSize: Int64
    var resolution: CGSize

    @Published var previewImage: NSImage?
    @Published var status: Status = .idle

    init(url: URL, duration: TimeInterval, fileSize: Int64, resolution: CGSize = .zero) {
        self.url = url
        self.fileName = url.lastPathComponent
        self.duration = duration
        self.fileSize = fileSize
        self.resolution = resolution
    }
}
