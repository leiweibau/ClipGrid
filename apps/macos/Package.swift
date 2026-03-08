// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "ThumbnailGridStudio",
    defaultLocalization: "de",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(
            name: "ThumbnailGridStudio",
            targets: ["ThumbnailGridStudio"]
        ),
        .executable(
            name: "thumbnail-grid-studio-cli",
            targets: ["ThumbnailGridStudioCLI"]
        )
    ],
    targets: [
        .executableTarget(
            name: "ThumbnailGridStudio",
            path: "Sources/ThumbnailGridStudio",
            resources: [
                .process("Resources")
            ]
        ),
        .executableTarget(
            name: "ThumbnailGridStudioCLI",
            path: "Sources/ThumbnailGridStudioCLI"
        )
    ]
)
