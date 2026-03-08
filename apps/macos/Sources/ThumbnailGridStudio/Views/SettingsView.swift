import SwiftUI

struct SettingsView: View {
    @ObservedObject var settings: AppSettings
    let backgroundColor: Binding<Color>
    let metadataTextColor: Binding<Color>
    @Environment(\.dismiss) private var dismiss
    @State private var backgroundHexText = ""
    @State private var metadataHexText = ""

    private let settingsFieldWidth: CGFloat = 64
    private let metadataFieldWidth: CGFloat = 64
    private let hexFieldWidth: CGFloat = 90

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text(AppStrings.settingsTitle)
                .font(.title2)
                .fontWeight(.semibold)

            settingsSection(AppStrings.thumbnails) {
                HStack(alignment: .top, spacing: 44) {
                    VStack(alignment: .leading, spacing: 14) {
                        StepperRow(title: AppStrings.columns(settings.columns), value: $settings.columns, range: 1...10)
                        LabeledFieldRow(title: "\(AppStrings.thumbnailWidth):", placeholder: widthPlaceholder, text: $settings.thumbnailWidthText, fieldWidth: settingsFieldWidth)
                        LabeledFieldRow(title: "\(AppStrings.thumbnailHeight):", placeholder: heightPlaceholder, text: $settings.thumbnailHeightText, fieldWidth: settingsFieldWidth)
                    }

                    VStack(alignment: .leading, spacing: 14) {
                        StepperRow(title: AppStrings.rows(settings.rows), value: $settings.rows, range: 1...10)
                        LabeledFieldRow(title: "\(AppStrings.spacingLabel):", placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultThumbnailSpacing)), text: $settings.thumbnailSpacingText, fieldWidth: settingsFieldWidth)
                    }
                }

                HStack(alignment: .center, spacing: 12) {
                    Text(AppStrings.exportFormat)
                        .frame(width: max(140, labelColumnWidth), alignment: .leading)
                    Picker("", selection: $settings.exportFormat) {
                        ForEach(ExportFormat.allCases) { format in
                            Text(format.displayName).tag(format)
                        }
                    }
                    .labelsHidden()
                    .pickerStyle(.segmented)
                    .frame(width: 200)
                }

                Toggle(AppStrings.exportSeparateThumbnails, isOn: $settings.exportSeparateThumbnails)
            }

            settingsSection(AppStrings.colors) {
                HStack(alignment: .top, spacing: 44) {
                    colorRow(
                        title: "\(AppStrings.background):",
                        selection: backgroundColor,
                        hexText: $backgroundHexText,
                        applyHex: { settings.updateBackgroundColorHex($0) }
                    )
                    colorRow(
                        title: "\(AppStrings.metadataTextColor):",
                        selection: metadataTextColor,
                        hexText: $metadataHexText,
                        applyHex: { settings.updateMetadataTextColorHex($0) }
                    )
                }
            }

            settingsSection(AppStrings.metadata) {
                HStack(alignment: .top, spacing: 44) {
                    VStack(alignment: .leading, spacing: 12) {
                        MetadataGridRow(
                            title: AppStrings.showFileName,
                            isOn: $settings.showFileName,
                            fontSizeText: $settings.fileNameFontSizeText,
                            placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultFileNameFontSize)),
                            fieldWidth: metadataFieldWidth
                        )
                        MetadataGridRow(
                            title: AppStrings.showDuration,
                            isOn: $settings.showDuration,
                            fontSizeText: $settings.durationFontSizeText,
                            placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultDurationFontSize)),
                            fieldWidth: metadataFieldWidth
                        )
                        MetadataGridRow(
                            title: AppStrings.showFileSize,
                            isOn: $settings.showFileSize,
                            fontSizeText: $settings.fileSizeFontSizeText,
                            placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultFileSizeFontSize)),
                            fieldWidth: metadataFieldWidth
                        )
                    }

                    VStack(alignment: .leading, spacing: 12) {
                        MetadataGridRow(
                            title: AppStrings.showResolution,
                            isOn: $settings.showResolution,
                            fontSizeText: $settings.resolutionFontSizeText,
                            placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultResolutionFontSize)),
                            fieldWidth: metadataFieldWidth
                        )
                        MetadataGridRow(
                            title: AppStrings.showTimestamp,
                            isOn: $settings.showTimestamp,
                            fontSizeText: $settings.timestampFontSizeText,
                            placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultTimestampFontSize)),
                            fieldWidth: metadataFieldWidth
                        )
                    }
                }

                HStack(spacing: 10) {
                    Text(AppStrings.renderConcurrency(settings.renderConcurrency))
                    Stepper("", value: $settings.renderConcurrency, in: 1...8)
                        .labelsHidden()
                }
            }

            HStack {
                Spacer()
                Button(AppStrings.close) {
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 620)
        .onAppear {
            backgroundHexText = settings.backgroundHexCode
            metadataHexText = settings.metadataTextHexCode
        }
        .onChange(of: settings.backgroundRed) { _ in
            backgroundHexText = settings.backgroundHexCode
        }
        .onChange(of: settings.backgroundGreen) { _ in
            backgroundHexText = settings.backgroundHexCode
        }
        .onChange(of: settings.backgroundBlue) { _ in
            backgroundHexText = settings.backgroundHexCode
        }
        .onChange(of: settings.metadataTextRed) { _ in
            metadataHexText = settings.metadataTextHexCode
        }
        .onChange(of: settings.metadataTextGreen) { _ in
            metadataHexText = settings.metadataTextHexCode
        }
        .onChange(of: settings.metadataTextBlue) { _ in
            metadataHexText = settings.metadataTextHexCode
        }
    }

    private var labelColumnWidth: CGFloat {
        max(
            "\(AppStrings.thumbnailWidth):".size(using: .systemFont(ofSize: NSFont.systemFontSize)),
            "\(AppStrings.thumbnailHeight):".size(using: .systemFont(ofSize: NSFont.systemFontSize)),
            "\(AppStrings.spacingLabel):".size(using: .systemFont(ofSize: NSFont.systemFontSize)),
            "\(AppStrings.background):".size(using: .systemFont(ofSize: NSFont.systemFontSize)),
            "\(AppStrings.metadataTextColor):".size(using: .systemFont(ofSize: NSFont.systemFontSize)),
            AppStrings.exportFormat.size(using: .systemFont(ofSize: NSFont.systemFontSize))
        ) + 8
    }

    private var widthPlaceholder: String {
        if settings.thumbnailWidthText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
           settings.thumbnailHeightText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return AppStrings.pixelPlaceholder(Int(AppSettings.defaultThumbnailWidth))
        }
        return AppStrings.auto
    }

    private var heightPlaceholder: String {
        if settings.thumbnailWidthText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
           settings.thumbnailHeightText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return AppStrings.pixelPlaceholder(Int(AppSettings.defaultThumbnailHeight))
        }
        return AppStrings.auto
    }

    @ViewBuilder
    private func settingsSection<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title)
                .font(.headline)
            content()
        }
    }

    private func colorRow(
        title: String,
        selection: Binding<Color>,
        hexText: Binding<String>,
        applyHex: @escaping (String) -> Bool
    ) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text(title)
                .frame(width: max(150, labelColumnWidth), alignment: .leading)
            VStack(alignment: .leading, spacing: 8) {
                ColorPicker("", selection: selection, supportsOpacity: false)
                    .labelsHidden()
                    .frame(width: hexFieldWidth, alignment: .leading)
                TextField(AppStrings.hexPlaceholder, text: hexText)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: hexFieldWidth, alignment: .leading)
                    .onChange(of: hexText.wrappedValue) { newValue in
                        let filtered = newValue
                            .uppercased()
                            .filter { $0.isHexDigit }
                        if filtered != newValue {
                            hexText.wrappedValue = filtered
                        }
                        if filtered.count > 6 {
                            hexText.wrappedValue = String(filtered.prefix(6))
                        }
                        if filtered.count == 6 {
                            _ = applyHex(filtered)
                        }
                    }
                    .onSubmit {
                        if !applyHex(hexText.wrappedValue) {
                            hexText.wrappedValue = String(hexText.wrappedValue.prefix(6)).uppercased()
                        }
                    }
            }
        }
    }
}

private struct MetadataGridRow: View {
    let title: String
    @Binding var isOn: Bool
    @Binding var fontSizeText: String
    let placeholder: String
    let fieldWidth: CGFloat

    var body: some View {
        HStack(spacing: 10) {
            Toggle("", isOn: $isOn)
                .labelsHidden()
            Text(title)
                .frame(minWidth: 170, alignment: .leading)
            TextField(placeholder, text: $fontSizeText)
                .textFieldStyle(.roundedBorder)
                .frame(width: fieldWidth)
        }
    }
}

private struct LabeledFieldRow: View {
    let title: String
    let placeholder: String
    @Binding var text: String
    let fieldWidth: CGFloat

    var body: some View {
        HStack(spacing: 12) {
            Text(title)
                .frame(width: 150, alignment: .leading)
            TextField(placeholder, text: $text)
                .textFieldStyle(.roundedBorder)
                .frame(width: fieldWidth)
        }
    }
}

private struct StepperRow: View {
    let title: String
    @Binding var value: Int
    let range: ClosedRange<Int>

    var body: some View {
        HStack(spacing: 8) {
            Text(title)
                .fixedSize()
            Stepper("", value: $value, in: range)
                .labelsHidden()
        }
    }
}

private extension String {
    func size(using font: NSFont) -> CGFloat {
        (self as NSString).size(withAttributes: [.font: font]).width
    }
}
