using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
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
                FileSizeBytes: fileSizeBytes);

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
        var thumbWidth = thumbnails.Count > 0 ? Math.Max(thumbnails[0].Image.Width, 1) : settings.ThumbnailWidth;
        var thumbHeight = thumbnails.Count > 0 ? Math.Max(thumbnails[0].Image.Height, 1) : settings.ThumbnailHeight;
        var gridWidth = settings.Columns * thumbWidth + Math.Max(0, settings.Columns - 1) * settings.Spacing;
        var gridHeight = settings.Rows * thumbHeight + Math.Max(0, settings.Rows - 1) * settings.Spacing;
        var headerHeight = CalculateHeaderHeight(settings);
        var canvasWidth = horizontalPadding * 2 + gridWidth;
        var canvasHeight = verticalPadding * 2 + headerHeight + gridHeight;

        using var bitmap = new Bitmap(canvasWidth, canvasHeight);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.Clear(ParseHexColor(settings.BackgroundHex, Color.FromArgb(31, 33, 38)));

        var headerTop = verticalPadding;
        var gridTop = verticalPadding + headerHeight;
        DrawHeader(graphics, metadata, fileName, settings, horizontalPadding, headerTop);
        DrawGrid(graphics, thumbnails, settings, horizontalPadding, gridTop, thumbWidth, thumbHeight);

        var format = settings.ExportFormatIndex == 1 ? ImageFormat.Png : ImageFormat.Jpeg;
        bitmap.Save(outputPath, format);
    }

    private static void DrawHeader(Graphics graphics, VideoMetadata metadata, string fileName, AppSettings settings, int leftPadding, int topY)
    {
        using var titleBrush = new SolidBrush(Color.White);
        using var metaBrush = new SolidBrush(ParseHexColor(settings.MetadataHex, Color.White));
        using var titleFont = new Font(FontFamily.GenericSansSerif, settings.FileNameFontSize, FontStyle.Bold);
        using var durationFont = new Font(FontFamily.GenericSansSerif, settings.DurationFontSize, FontStyle.Regular);
        using var fileSizeFont = new Font(FontFamily.GenericSansSerif, settings.FileSizeFontSize, FontStyle.Regular);
        using var resolutionFont = new Font(FontFamily.GenericSansSerif, settings.ResolutionFontSize, FontStyle.Regular);

        var y = topY;
        if (settings.ShowFileName)
        {
            var titleText = Path.GetFileName(fileName);
            var titleSize = graphics.MeasureString(titleText, titleFont);
            graphics.DrawString(titleText, titleFont, titleBrush, new PointF(leftPadding, y));
            y += (int)Math.Ceiling(titleSize.Height) + 6;
        }

        var line2Parts = new List<string>(2);
        if (settings.ShowDuration)
        {
            line2Parts.Add(FormatDuration(metadata.Duration));
        }

        if (settings.ShowFileSize)
        {
            line2Parts.Add(FormatFileSize(metadata.FileSizeBytes));
        }

        if (line2Parts.Count > 0)
        {
            var line2 = string.Join("  •  ", line2Parts);
            graphics.DrawString(line2, durationFont, metaBrush, new PointF(leftPadding, y));
            var line2Size = graphics.MeasureString(line2, durationFont);
            y += (int)Math.Ceiling(line2Size.Height) + 4;
        }

        if (settings.ShowResolution)
        {
            var resolution = $"{Math.Max(metadata.Width, 0)} x {Math.Max(metadata.Height, 0)} px";
            graphics.DrawString(resolution, resolutionFont, metaBrush, new PointF(leftPadding, y));
            var line3Size = graphics.MeasureString(resolution, resolutionFont);
            y += (int)Math.Ceiling(line3Size.Height);
        }
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

    private static int CalculateHeaderHeight(AppSettings settings)
    {
        using var titleFont = new Font(FontFamily.GenericSansSerif, settings.FileNameFontSize, FontStyle.Bold);
        using var durationFont = new Font(FontFamily.GenericSansSerif, settings.DurationFontSize, FontStyle.Regular);
        using var resolutionFont = new Font(FontFamily.GenericSansSerif, settings.ResolutionFontSize, FontStyle.Regular);
        var height = 18;
        if (settings.ShowFileName)
        {
            height += (int)Math.Ceiling(titleFont.GetHeight()) + 10;
        }

        if (settings.ShowDuration || settings.ShowFileSize)
        {
            height += (int)Math.Ceiling(durationFont.GetHeight()) + 8;
        }

        if (settings.ShowResolution)
        {
            height += (int)Math.Ceiling(resolutionFont.GetHeight()) + 6;
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
