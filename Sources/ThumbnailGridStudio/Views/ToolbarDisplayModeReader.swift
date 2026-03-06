import AppKit
import SwiftUI

@MainActor
final class ToolbarDisplayModeObserver: ObservableObject {
    @Published var displayMode: NSToolbar.DisplayMode = .default

    private var observation: NSKeyValueObservation?
    private weak var currentToolbar: NSToolbar?

    func attach(to window: NSWindow?) {
        guard let toolbar = window?.toolbar else { return }
        guard currentToolbar !== toolbar else { return }

        observation?.invalidate()
        observation = nil
        currentToolbar = toolbar
        updateDisplayMode(toolbar.displayMode)
        observation = toolbar.observe(\.displayMode, options: [.initial, .new]) { [weak self] toolbar, _ in
            Task { @MainActor in
                self?.updateDisplayMode(toolbar.displayMode)
            }
        }
    }

    private func updateDisplayMode(_ newValue: NSToolbar.DisplayMode) {
        guard displayMode != newValue else { return }
        displayMode = newValue
    }
}

struct ToolbarDisplayModeReader: NSViewRepresentable {
    @ObservedObject var observer: ToolbarDisplayModeObserver

    func makeNSView(context: Context) -> NSView {
        TrackingView(observer: observer)
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

private final class TrackingView: NSView {
    private weak var observer: ToolbarDisplayModeObserver?

    init(observer: ToolbarDisplayModeObserver) {
        self.observer = observer
        super.init(frame: .zero)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        observer?.attach(to: window)
    }
}
