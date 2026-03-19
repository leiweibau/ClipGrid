using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using ThumbnailGridStudio.WinUI.Models;

namespace ThumbnailGridStudio.WinUI.Services;

public static class ContactSheetRenderer
{
    public static void RenderPlaceholderAndSave(
        string title,
        TimeSpan duration,
        long fileSizeBytes,
        int width,
        int height,
        long bitrateBitsPerSecond,
        string videoCodec,
        IReadOnlyList<string> audioCodecs,
        AppSettings settings,
        string outputPath)
    {
        var count = Math.Max(settings.Columns * settings.Rows, 1);
        var thumbSize = settings.ResolvePlaceholderThumbnailSize();
        var frames = new List<ThumbnailFrame>(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                using var placeholder = CreatePlaceholderThumbnail(thumbSize.Width, thumbSize.Height);
                frames.Add(new ThumbnailFrame(placeholder, TimeSpan.FromSeconds(i * 10)));
            }

            var metadata = new VideoMetadata(
                Duration: duration,
                Width: width,
                Height: height,
                FileSizeBytes: fileSizeBytes,
                BitrateBitsPerSecond: bitrateBitsPerSecond,
                VideoCodec: videoCodec,
                AudioCodecs: audioCodecs ?? Array.Empty<string>());

            RenderAndSave(metadata, title, frames, settings, outputPath);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    public static void RenderAndSave(
        VideoMetadata metadata,
        string fileName,
        IReadOnlyList<ThumbnailFrame> thumbnails,
        AppSettings settings,
        string outputPath)
    {
        var horizontalPadding = 28;
        var verticalPadding = 28;
        var metadataToGridGap = 10;
        var thumbWidth = thumbnails.Count > 0 ? Math.Max(thumbnails[0].Image.Width, 1) : settings.ThumbnailWidth;
        var thumbHeight = thumbnails.Count > 0 ? Math.Max(thumbnails[0].Image.Height, 1) : settings.ThumbnailHeight;
        var gridWidth = settings.Columns * thumbWidth + Math.Max(0, settings.Columns - 1) * settings.Spacing;
        var gridHeight = settings.Rows * thumbHeight + Math.Max(0, settings.Rows - 1) * settings.Spacing;
        var headerHeight = CalculateHeaderHeight(metadata, settings);
        var canvasWidth = horizontalPadding * 2 + gridWidth;
        var canvasHeight = verticalPadding * 2 + headerHeight + metadataToGridGap + gridHeight;

        using var bitmap = new Bitmap(canvasWidth, canvasHeight);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(ParseHexColor(settings.BackgroundHex, Color.FromArgb(31, 33, 38)));

        var headerTop = verticalPadding;
        var gridTop = verticalPadding + headerHeight + metadataToGridGap;
        DrawHeader(graphics, metadata, fileName, settings, horizontalPadding, headerTop);
        DrawGrid(graphics, thumbnails, settings, horizontalPadding, gridTop, thumbWidth, thumbHeight);

        var format = settings.ExportFormatIndex == 1 ? ImageFormat.Png : ImageFormat.Jpeg;
        bitmap.Save(outputPath, format);
    }

    private static void DrawHeader(Graphics graphics, VideoMetadata metadata, string fileName, AppSettings settings, int leftPadding, int topY)
    {
        var labelTitle = IsGermanUi() ? "Titel" : "Title";
        var labelDuration = IsGermanUi() ? "Dauer" : "Duration";
        var labelSize = IsGermanUi() ? "Größe" : "Size";
        var labelResolution = IsGermanUi() ? "Auflösung" : "Resolution";
        var labelBitrate = "Bitrate";
        var labelVideo = "Video";
        var labelAudio = "Audio";
        var unknownValue = IsGermanUi() ? "unbekannt" : "unknown";
        const float secondColumnGapPx = 150f;

        using var titleBrush = new SolidBrush(Color.White);
        using var metaBrush = new SolidBrush(ParseHexColor(settings.MetadataHex, Color.White));
        using var titleFont = new Font(FontFamily.GenericSansSerif, settings.FileNameFontSize, FontStyle.Bold);
        using var durationFont = new Font(FontFamily.GenericSansSerif, settings.DurationFontSize, FontStyle.Regular);
        using var fileSizeFont = new Font(FontFamily.GenericSansSerif, settings.FileSizeFontSize, FontStyle.Regular);
        using var resolutionFont = new Font(FontFamily.GenericSansSerif, settings.ResolutionFontSize, FontStyle.Regular);
        using var bitrateFont = new Font(FontFamily.GenericSansSerif, settings.BitrateFontSize, FontStyle.Regular);
        using var videoCodecFont = new Font(FontFamily.GenericSansSerif, settings.VideoCodecFontSize, FontStyle.Regular);
        using var audioCodecFont = new Font(FontFamily.GenericSansSerif, settings.AudioCodecFontSize, FontStyle.Regular);

        var y = topY;
        if (settings.ShowFileName)
        {
            var titleText = $"{labelTitle}: {Path.GetFileName(fileName)}";
            var titleSize = graphics.MeasureString(titleText, titleFont);
            graphics.DrawString(titleText, titleFont, titleBrush, new PointF(leftPadding, y));
            y += (int)Math.Ceiling(titleSize.Height) + 6;
        }

        var durationText = settings.ShowDuration ? $"{labelDuration}: {FormatDuration(metadata.Duration)}" : null;
        var fileSizeText = settings.ShowFileSize ? $"{labelSize}: {FormatFileSize(metadata.FileSizeBytes)}" : null;
        var resolutionText = settings.ShowResolution ? $"{labelResolution}: {Math.Max(metadata.Width, 0)} x {Math.Max(metadata.Height, 0)}" : null;
        var bitrateText = settings.ShowBitrate ? $"{labelBitrate}: {FormatBitrate(metadata.BitrateBitsPerSecond, unknownValue)}" : null;

        var firstColumnWidth = 0f;
        if (!string.IsNullOrWhiteSpace(durationText))
        {
            firstColumnWidth = Math.Max(firstColumnWidth, graphics.MeasureString(durationText, durationFont).Width);
        }

        if (!string.IsNullOrWhiteSpace(resolutionText))
        {
            firstColumnWidth = Math.Max(firstColumnWidth, graphics.MeasureString(resolutionText, resolutionFont).Width);
        }

        var secondColumnX = leftPadding + firstColumnWidth + (firstColumnWidth > 0f ? secondColumnGapPx : 0f);

        if (!string.IsNullOrWhiteSpace(durationText) || !string.IsNullOrWhiteSpace(fileSizeText))
        {
            var line2Height = 0f;
            if (!string.IsNullOrWhiteSpace(durationText))
            {
                graphics.DrawString(durationText, durationFont, metaBrush, new PointF(leftPadding, y));
                line2Height = Math.Max(line2Height, graphics.MeasureString(durationText, durationFont).Height);
            }

            if (!string.IsNullOrWhiteSpace(fileSizeText))
            {
                var fileSizeX = !string.IsNullOrWhiteSpace(durationText) ? secondColumnX : leftPadding;
                graphics.DrawString(fileSizeText, fileSizeFont, metaBrush, new PointF(fileSizeX, y));
                line2Height = Math.Max(line2Height, graphics.MeasureString(fileSizeText, fileSizeFont).Height);
            }

            y += (int)Math.Ceiling(line2Height) + 4;
        }

        if (!string.IsNullOrWhiteSpace(resolutionText) || !string.IsNullOrWhiteSpace(bitrateText))
        {
            var line3Height = 0f;
            if (!string.IsNullOrWhiteSpace(resolutionText))
            {
                graphics.DrawString(resolutionText, resolutionFont, metaBrush, new PointF(leftPadding, y));
                line3Height = Math.Max(line3Height, graphics.MeasureString(resolutionText, resolutionFont).Height);
            }

            if (!string.IsNullOrWhiteSpace(bitrateText))
            {
                var bitrateX = !string.IsNullOrWhiteSpace(resolutionText) ? secondColumnX : leftPadding;
                graphics.DrawString(bitrateText, bitrateFont, metaBrush, new PointF(bitrateX, y));
                line3Height = Math.Max(line3Height, graphics.MeasureString(bitrateText, bitrateFont).Height);
            }

            y += (int)Math.Ceiling(line3Height) + 4;
        }

        if (settings.ShowVideoCodec)
        {
            var videoText = $"{labelVideo}: {FormatCodec(metadata.VideoCodec, unknownValue)}";
            graphics.DrawString(videoText, videoCodecFont, metaBrush, new PointF(leftPadding, y));
            var line4Size = graphics.MeasureString(videoText, videoCodecFont);
            y += (int)Math.Ceiling(line4Size.Height) + 4;
        }

        if (settings.ShowAudioCodec)
        {
            IReadOnlyList<string> audioCodecs = metadata.AudioCodecs is { Count: > 0 }
                ? metadata.AudioCodecs
                : new List<string> { unknownValue };

            foreach (var codec in audioCodecs)
            {
                var audioText = $"{labelAudio}: {FormatCodec(codec, unknownValue)}";
                graphics.DrawString(audioText, audioCodecFont, metaBrush, new PointF(leftPadding, y));
                var lineSize = graphics.MeasureString(audioText, audioCodecFont);
                y += (int)Math.Ceiling(lineSize.Height) + 4;
            }
        }
    }

    private static void DrawSegmentLine(
        Graphics graphics,
        Brush brush,
        float x,
        float y,
        IReadOnlyList<(string Text, Font Font)> segments,
        string separator,
        out int lineHeight)
    {
        var cursorX = x;
        var maxHeight = 0f;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            graphics.DrawString(segment.Text, segment.Font, brush, new PointF(cursorX, y));
            var segmentSize = graphics.MeasureString(segment.Text, segment.Font);
            cursorX += segmentSize.Width;
            maxHeight = Math.Max(maxHeight, segmentSize.Height);

            if (i >= segments.Count - 1)
            {
                continue;
            }

            var separatorSize = graphics.MeasureString(separator, segment.Font);
            graphics.DrawString(separator, segment.Font, brush, new PointF(cursorX, y));
            cursorX += separatorSize.Width;
            maxHeight = Math.Max(maxHeight, separatorSize.Height);
        }

        lineHeight = (int)Math.Ceiling(maxHeight);
    }

    private static void DrawGrid(
        Graphics graphics,
        IReadOnlyList<ThumbnailFrame> thumbnails,
        AppSettings settings,
        int leftPadding,
        int topPadding,
        int thumbWidth,
        int thumbHeight)
    {
        for (var row = 0; row < settings.Rows; row++)
        {
            for (var column = 0; column < settings.Columns; column++)
            {
                var index = row * settings.Columns + column;
                var x = leftPadding + column * (thumbWidth + settings.Spacing);
                var y = topPadding + row * (thumbHeight + settings.Spacing);
                var target = new Rectangle(x, y, thumbWidth, thumbHeight);

                using (var bgBrush = new SolidBrush(Color.FromArgb(24, 255, 255, 255)))
                {
                    graphics.FillRectangle(bgBrush, target);
                }

                if (index >= thumbnails.Count)
                {
                    continue;
                }

                DrawCoverImage(graphics, thumbnails[index], target);
                if (settings.ShowTimestamp)
                {
                    DrawTimestampBadge(graphics, thumbnails[index].Timestamp, target, settings.TimestampFontSize);
                }
            }
        }
    }

    private static void DrawCoverImage(Graphics graphics, ThumbnailFrame frame, Rectangle target)
    {
        var source = frame.Image;
        var scale = Math.Max(target.Width / (float)source.Width, target.Height / (float)source.Height);
        var drawWidth = source.Width * scale;
        var drawHeight = source.Height * scale;
        var drawX = target.X + (target.Width - drawWidth) / 2f;
        var drawY = target.Y + (target.Height - drawHeight) / 2f;

        graphics.SetClip(target);
        graphics.DrawImage(source, drawX, drawY, drawWidth, drawHeight);
        graphics.ResetClip();
    }

    private static void DrawTimestampBadge(Graphics graphics, TimeSpan timestamp, Rectangle target, float fontSize)
    {
        var text = FormatDuration(timestamp);
        using var font = new Font(FontFamily.GenericMonospace, fontSize, FontStyle.Bold);
        var textSize = graphics.MeasureString(text, font);
        var badge = new RectangleF(
            target.Right - textSize.Width - 18f,
            target.Bottom - textSize.Height - 12f,
            textSize.Width + 12f,
            textSize.Height + 6f);

        using var badgeBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        using var textBrush = new SolidBrush(Color.White);
        graphics.FillRectangle(badgeBrush, badge);
        graphics.DrawString(text, font, textBrush, new PointF(badge.X + 6f, badge.Y + 3f));
    }

    private static int CalculateHeaderHeight(VideoMetadata metadata, AppSettings settings)
    {
        using var titleFont = new Font(FontFamily.GenericSansSerif, settings.FileNameFontSize, FontStyle.Bold);
        using var durationFont = new Font(FontFamily.GenericSansSerif, settings.DurationFontSize, FontStyle.Regular);
        using var fileSizeFont = new Font(FontFamily.GenericSansSerif, settings.FileSizeFontSize, FontStyle.Regular);
        using var resolutionFont = new Font(FontFamily.GenericSansSerif, settings.ResolutionFontSize, FontStyle.Regular);
        using var bitrateFont = new Font(FontFamily.GenericSansSerif, settings.BitrateFontSize, FontStyle.Regular);
        using var videoCodecFont = new Font(FontFamily.GenericSansSerif, settings.VideoCodecFontSize, FontStyle.Regular);
        using var audioCodecFont = new Font(FontFamily.GenericSansSerif, settings.AudioCodecFontSize, FontStyle.Regular);

        var height = 18;
        if (settings.ShowFileName)
        {
            height += (int)Math.Ceiling(titleFont.GetHeight()) + 10;
        }

        if (settings.ShowDuration || settings.ShowFileSize)
        {
            var line2Height = Math.Max(durationFont.GetHeight(), fileSizeFont.GetHeight());
            height += (int)Math.Ceiling(line2Height) + 8;
        }

        if (settings.ShowResolution || settings.ShowBitrate)
        {
            var line3Height = Math.Max(resolutionFont.GetHeight(), bitrateFont.GetHeight());
            height += (int)Math.Ceiling(line3Height) + 6;
        }

        if (settings.ShowVideoCodec)
        {
            height += (int)Math.Ceiling(videoCodecFont.GetHeight()) + 6;
        }

        if (settings.ShowAudioCodec)
        {
            var audioLineCount = Math.Max(metadata.AudioCodecs?.Count ?? 0, 1);
            height += audioLineCount * ((int)Math.Ceiling(audioCodecFont.GetHeight()) + 6);
        }

        return Math.Max(height, 18);
    }

    private static Color ParseHexColor(string hex, Color fallback)
    {
        var value = hex.Trim().TrimStart('#');
        if (value.Length != 6)
        {
            return fallback;
        }

        try
        {
            var r = Convert.ToInt32(value[..2], 16);
            var g = Convert.ToInt32(value.Substring(2, 2), 16);
            var b = Convert.ToInt32(value.Substring(4, 2), 16);
            return Color.FromArgb(r, g, b);
        }
        catch
        {
            return fallback;
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return $"{Math.Max(duration.Minutes, 0):00}:{Math.Max(duration.Seconds, 0):00}";
    }

    private static string FormatFileSize(long size)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(size, 0);
        var suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix += 1;
        }

        return $"{value:0.##} {suffixes[suffix]}";
    }

    private static string FormatBitrate(long bitrateBitsPerSecond, string unknownText)
    {
        if (bitrateBitsPerSecond <= 0)
        {
            return unknownText;
        }

        var kbps = bitrateBitsPerSecond / 1000d;
        if (kbps >= 1000d)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{kbps / 1000d:0.##} Mbps");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{kbps:0} kbps");
    }

    private static string FormatCodec(string? codecName, string unknownText)
    {
        if (string.IsNullOrWhiteSpace(codecName))
        {
            return unknownText;
        }

        return codecName.Trim();
    }

    private static bool IsGermanUi()
    {
        return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase);
    }

    private static Bitmap CreatePlaceholderThumbnail(int width, int height)
    {
        var w = Math.Max(width, 1);
        var h = Math.Max(height, 1);
        var bitmap = new Bitmap(w, h);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var background = new LinearGradientBrush(
            new Rectangle(0, 0, w, h),
            Color.FromArgb(16, 18, 24),
            Color.FromArgb(5, 7, 11),
            90f);
        g.FillRectangle(background, 0, 0, w, h);

        using var gloss = new GraphicsPath();
        gloss.AddEllipse(-w / 3f, -h * 0.9f, w * 1.6f, h * 2.0f);
        using var glossBrush = new PathGradientBrush(gloss)
        {
            CenterColor = Color.FromArgb(52, 195, 205, 220),
            SurroundColors = [Color.FromArgb(0, 195, 205, 220)]
        };
        g.FillPath(glossBrush, gloss);

        using var borderPen = new Pen(Color.FromArgb(38, 255, 255, 255), 1f);
        g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

        return bitmap;
    }
}
