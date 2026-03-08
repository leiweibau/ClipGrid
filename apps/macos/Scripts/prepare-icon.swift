import AppKit
import Foundation

struct RGBA {
    var r: UInt8
    var g: UInt8
    var b: UInt8
    var a: UInt8
}

enum IconPreparationError: Error {
    case invalidArguments
    case cannotLoadImage
    case cannotCreateBitmap
    case cannotEncodePNG
}

func pixelAt(x: Int, y: Int, data: UnsafeMutablePointer<UInt8>, bytesPerRow: Int) -> RGBA {
    let offset = y * bytesPerRow + x * 4
    return RGBA(
        r: data[offset],
        g: data[offset + 1],
        b: data[offset + 2],
        a: data[offset + 3]
    )
}

func setTransparent(x: Int, y: Int, data: UnsafeMutablePointer<UInt8>, bytesPerRow: Int) {
    let offset = y * bytesPerRow + x * 4
    data[offset + 3] = 0
}

func isSimilar(_ lhs: RGBA, _ rhs: RGBA, tolerance: Int) -> Bool {
    abs(Int(lhs.r) - Int(rhs.r)) <= tolerance &&
    abs(Int(lhs.g) - Int(rhs.g)) <= tolerance &&
    abs(Int(lhs.b) - Int(rhs.b)) <= tolerance
}

func isLightNeutral(_ pixel: RGBA) -> Bool {
    let maxChannel = max(Int(pixel.r), Int(pixel.g), Int(pixel.b))
    let minChannel = min(Int(pixel.r), Int(pixel.g), Int(pixel.b))
    let average = (Int(pixel.r) + Int(pixel.g) + Int(pixel.b)) / 3
    return average >= 190 && (maxChannel - minChannel) <= 28
}

func averageCornerColor(in data: UnsafeMutablePointer<UInt8>, width: Int, height: Int, bytesPerRow: Int) -> RGBA {
    let sample = 8
    let points = [
        (0, 0),
        (width - sample, 0),
        (0, height - sample),
        (width - sample, height - sample)
    ]

    var totalR = 0
    var totalG = 0
    var totalB = 0
    var totalA = 0
    var count = 0

    for (startX, startY) in points {
        for y in startY..<(startY + sample) {
            for x in startX..<(startX + sample) {
                let pixel = pixelAt(x: x, y: y, data: data, bytesPerRow: bytesPerRow)
                totalR += Int(pixel.r)
                totalG += Int(pixel.g)
                totalB += Int(pixel.b)
                totalA += Int(pixel.a)
                count += 1
            }
        }
    }

    return RGBA(
        r: UInt8(totalR / count),
        g: UInt8(totalG / count),
        b: UInt8(totalB / count),
        a: UInt8(totalA / count)
    )
}

func makeOuterBackgroundTransparent(in bitmap: NSBitmapImageRep) throws {
    let width = bitmap.pixelsWide
    let height = bitmap.pixelsHigh
    let bytesPerRow = bitmap.bytesPerRow

    guard let data = bitmap.bitmapData else {
        throw IconPreparationError.cannotCreateBitmap
    }

    let background = averageCornerColor(in: data, width: width, height: height, bytesPerRow: bytesPerRow)
    let tolerance = 22

    var visited = Array(repeating: false, count: width * height)
    var queue: [(Int, Int)] = []

    func enqueue(_ x: Int, _ y: Int) {
        guard x >= 0, y >= 0, x < width, y < height else { return }
        let index = y * width + x
        guard !visited[index] else { return }
        visited[index] = true
        queue.append((x, y))
    }

    for x in 0..<width {
        enqueue(x, 0)
        enqueue(x, height - 1)
    }

    for y in 0..<height {
        enqueue(0, y)
        enqueue(width - 1, y)
    }

    var position = 0
    while position < queue.count {
        let (x, y) = queue[position]
        position += 1

        let pixel = pixelAt(x: x, y: y, data: data, bytesPerRow: bytesPerRow)
        guard isSimilar(pixel, background, tolerance: tolerance) || isLightNeutral(pixel) else { continue }

        setTransparent(x: x, y: y, data: data, bytesPerRow: bytesPerRow)

        enqueue(x + 1, y)
        enqueue(x - 1, y)
        enqueue(x, y + 1)
        enqueue(x, y - 1)
    }
}

let arguments = CommandLine.arguments
guard arguments.count == 3 else {
    throw IconPreparationError.invalidArguments
}

let inputURL = URL(fileURLWithPath: arguments[1])
let outputURL = URL(fileURLWithPath: arguments[2])

guard
    let image = NSImage(contentsOf: inputURL),
    let tiffData = image.tiffRepresentation,
    let bitmap = NSBitmapImageRep(data: tiffData)
else {
    throw IconPreparationError.cannotLoadImage
}

guard
    let rgbaBitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: bitmap.pixelsWide,
        pixelsHigh: bitmap.pixelsHigh,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: bitmap.pixelsWide * 4,
        bitsPerPixel: 32
    )
else {
    throw IconPreparationError.cannotCreateBitmap
}

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rgbaBitmap)
image.draw(
    in: NSRect(x: 0, y: 0, width: rgbaBitmap.pixelsWide, height: rgbaBitmap.pixelsHigh),
    from: .zero,
    operation: .copy,
    fraction: 1
)
NSGraphicsContext.restoreGraphicsState()

try makeOuterBackgroundTransparent(in: rgbaBitmap)

guard let pngData = rgbaBitmap.representation(using: .png, properties: [:]) else {
    throw IconPreparationError.cannotEncodePNG
}

try pngData.write(to: outputURL)
