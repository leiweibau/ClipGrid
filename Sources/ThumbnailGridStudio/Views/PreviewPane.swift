import SwiftUI

struct PreviewPane: View {
    @ObservedObject var viewModel: AppViewModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                ZStack {
                    RoundedRectangle(cornerRadius: 14)
                        .fill(.quaternary)

                    if let item = viewModel.selectedVideo, case .generating = item.status {
                        ProgressView(AppStrings.generatingPreview)
                    } else {
                        Image(nsImage: viewModel.previewImage(for: viewModel.selectedVideo))
                            .resizable()
                            .scaledToFit()
                            .padding(14)
                    }
                }
            }
            .padding(24)
        }
    }
}
