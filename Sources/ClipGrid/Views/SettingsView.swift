import SwiftUI

struct SettingsView: View {
    @ObservedObject var settings: AppSettings
    let backgroundColor: Binding<Color>
    let metadataTextColor: Binding<Color>
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text(AppStrings.settingsTitle)
                .font(.title2)
                .fontWeight(.semibold)

            HStack {
                Stepper(AppStrings.columns(settings.columns), value: $settings.columns, in: 1...10)
                Stepper(AppStrings.rows(settings.rows), value: $settings.rows, in: 1...10)
            }

            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                GridRow {
                    Text("\(AppStrings.thumbnailWidth):")
                    TextField(widthPlaceholder, text: $settings.thumbnailWidthText)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 88)
                }

                GridRow {
                    Text("\(AppStrings.thumbnailHeight):")
                    TextField(heightPlaceholder, text: $settings.thumbnailHeightText)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 88)
                    Text(AppStrings.spacing(Int(settings.thumbnailSpacing)))
                    Slider(value: $settings.thumbnailSpacing, in: 0...40, step: 1)
                        .frame(width: 150)
                }
            }

            VStack(alignment: .leading, spacing: 10) {
                Picker(AppStrings.exportFormat, selection: $settings.exportFormat) {
                    ForEach(ExportFormat.allCases) { format in
                        Text(format.displayName).tag(format)
                    }
                }
                .pickerStyle(.segmented)

                Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                    GridRow {
                        Text("\(AppStrings.background):")
                        ColorPicker("", selection: backgroundColor, supportsOpacity: false)
                            .labelsHidden()
                    }

                    GridRow {
                        Text("\(AppStrings.metadataTextColor):")
                        ColorPicker("", selection: metadataTextColor, supportsOpacity: false)
                            .labelsHidden()
                    }
                }
            }

            VStack(alignment: .leading, spacing: 10) {
                Text(AppStrings.metadata)
                    .font(.headline)

                Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                    MetadataGridRow(
                        title: AppStrings.showFileName,
                        isOn: $settings.showFileName,
                        fontSizeText: $settings.fileNameFontSizeText,
                        placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultFileNameFontSize))
                    )
                    MetadataGridRow(
                        title: AppStrings.showDuration,
                        isOn: $settings.showDuration,
                        fontSizeText: $settings.durationFontSizeText,
                        placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultDurationFontSize))
                    )
                    MetadataGridRow(
                        title: AppStrings.showFileSize,
                        isOn: $settings.showFileSize,
                        fontSizeText: $settings.fileSizeFontSizeText,
                        placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultFileSizeFontSize))
                    )
                    MetadataGridRow(
                        title: AppStrings.showResolution,
                        isOn: $settings.showResolution,
                        fontSizeText: $settings.resolutionFontSizeText,
                        placeholder: AppStrings.pixelPlaceholder(Int(AppSettings.defaultResolutionFontSize))
                    )

                    GridRow {
                        Toggle("", isOn: $settings.showTimestamp)
                            .labelsHidden()
                        Text(AppStrings.showTimestamp)
                        TextField(
                            AppStrings.pixelPlaceholder(Int(AppSettings.defaultTimestampFontSize)),
                            text: $settings.timestampFontSizeText
                        )
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 88)
                    }
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
}

private struct MetadataGridRow: View {
    let title: String
    @Binding var isOn: Bool
    @Binding var fontSizeText: String
    let placeholder: String

    var body: some View {
        GridRow {
            Toggle("", isOn: $isOn)
                .labelsHidden()
            Text(title)
            TextField(placeholder, text: $fontSizeText)
                .textFieldStyle(.roundedBorder)
                .frame(width: 88)
        }
    }
}
