import Foundation

struct ReleaseInfo {
    let tagName: String
    let name: String
    let notes: String
    let url: URL
}

enum UpdateService {
    private static let latestReleaseURL = URL(string: "https://api.github.com/repos/leiweibau/ThumbnailGridStudio/releases/latest")!

    static func fetchLatestRelease() async throws -> ReleaseInfo {
        var request = URLRequest(url: latestReleaseURL)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.timeoutInterval = 15

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let httpResponse = response as? HTTPURLResponse, (200..<300).contains(httpResponse.statusCode) else {
            throw URLError(.badServerResponse)
        }

        let payload = try JSONDecoder().decode(GitHubReleasePayload.self, from: data)
        return ReleaseInfo(
            tagName: payload.tagName,
            name: payload.name ?? payload.tagName,
            notes: payload.body ?? "",
            url: payload.htmlURL
        )
    }

    static func isRemoteVersionNewer(remoteTag: String, localVersion: String) -> Bool {
        compareVersions(normalizedVersion(remoteTag), normalizedVersion(localVersion)) == .orderedDescending
    }

    private static func normalizedVersion(_ raw: String) -> String {
        raw.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: "v", with: "", options: [.anchored, .caseInsensitive])
    }

    private static func compareVersions(_ lhs: String, _ rhs: String) -> ComparisonResult {
        let leftParts = lhs.split(separator: ".").map { Int($0) ?? 0 }
        let rightParts = rhs.split(separator: ".").map { Int($0) ?? 0 }
        let count = max(leftParts.count, rightParts.count)

        for index in 0..<count {
            let left = index < leftParts.count ? leftParts[index] : 0
            let right = index < rightParts.count ? rightParts[index] : 0
            if left < right { return .orderedAscending }
            if left > right { return .orderedDescending }
        }
        return .orderedSame
    }
}

private struct GitHubReleasePayload: Decodable {
    let tagName: String
    let name: String?
    let body: String?
    let htmlURL: URL

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case name
        case body
        case htmlURL = "html_url"
    }
}
