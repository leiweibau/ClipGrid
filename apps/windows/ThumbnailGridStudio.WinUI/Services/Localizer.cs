using System.Globalization;

namespace ThumbnailGridStudio.WinUI.Services;

public static class Localizer
{
    private static readonly Dictionary<string, string> De = new(StringComparer.Ordinal)
    {
        ["Main.CheckUpdates"] = "Auf Updates prüfen",
        ["Main.NoPreview"] = "Keine Vorschau",
        ["DragDrop.Insert"] = "Einfügen",
        ["Update.Available.Title"] = "Update verfügbar",
        ["Update.Available.Content"] = "Neue Version: {0}\nInstalliert: {1}\n\nMöchtest du die Release-Seite öffnen?",
        ["Update.Available.Primary"] = "Release öffnen",
        ["Update.Available.Close"] = "Später",
        ["Update.None.Title"] = "Kein Update gefunden",
        ["Update.None.Content"] = "Installierte Version: {0}",
        ["Update.Error.Title"] = "Update-Prüfung fehlgeschlagen",
        ["Update.InvalidResponse"] = "Ungültige Antwort von GitHub.",

        ["Settings.Title"] = "Einstellungen",
        ["Settings.Columns"] = "Spalten",
        ["Settings.Rows"] = "Zeilen",
        ["Settings.ThumbWidth"] = "Thumbnail Breite",
        ["Settings.ThumbHeight"] = "Thumbnail Höhe",
        ["Settings.Spacing"] = "Abstand",
        ["Settings.BgHex"] = "Hintergrundfarbe (HEX-Code)",
        ["Settings.TextHex"] = "Schriftfarbe (HEX-Code)",
        ["Settings.ExportSeparate"] = "Separate Thumbnails exportieren",
        ["Settings.ShowTitle"] = "Titel",
        ["Settings.ShowDuration"] = "Laufzeit",
        ["Settings.ShowTimestamp"] = "Timestamp",
        ["Settings.ShowFileSize"] = "Größe",
        ["Settings.ShowResolution"] = "Auflösung",
        ["Settings.ShowBitrate"] = "Bitrate",
        ["Settings.ShowVideoCodec"] = "Video-Codec",
        ["Settings.ShowAudioCodec"] = "Audio-Codec",
        ["Settings.RenderConcurrency"] = "Parallelität",
        ["Settings.Layout"] = "Layout",
        ["Settings.ColorsExport"] = "Farben & Export",
        ["Settings.ExportFormat"] = "Exportformat",
        ["Settings.MetadataPreview"] = "Metadaten im Vorschaubild",
        ["Settings.Cancel"] = "Abbrechen",
        ["Settings.Apply"] = "Übernehmen",

        ["Render.Label.Title"] = "Titel",
        ["Render.Label.Duration"] = "Dauer",
        ["Render.Label.Size"] = "Größe",
        ["Render.Label.Resolution"] = "Auflösung",
        ["Render.Label.Bitrate"] = "Bitrate",
        ["Render.Label.Video"] = "Video",
        ["Render.Label.Audio"] = "Audio",
        ["Render.Value.Unknown"] = "unbekannt",

        ["View.PreviewFallbackTitle"] = "Vorschau",
        ["View.Status.Waiting"] = "Wartet...",
        ["View.Status.Rendering"] = "Render läuft...",
        ["View.Status.Exported"] = "Exportiert: {0}",
        ["View.Status.Error"] = "Fehler: {0}",
        ["View.Error.ImportFailed"] = "Import fehlgeschlagen ({0}): {1}",
        ["View.Error.ExportFailed"] = "Export fehlgeschlagen ({0}): {1}",
        ["View.Error.UnsupportedExtensions"] = "Folgende Dateiendungen wurden wegen fehlender Unterstützung nicht importiert: {0}",
        ["View.Error.ExtensionCountItem"] = "{0}x {1}",
        ["View.Error.NoExtension"] = "(ohne Endung)"
    };

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["Main.CheckUpdates"] = "Check for updates",
        ["Main.NoPreview"] = "No preview",
        ["DragDrop.Insert"] = "Insert",
        ["Update.Available.Title"] = "Update available",
        ["Update.Available.Content"] = "New version: {0}\nInstalled: {1}\n\nDo you want to open the release page?",
        ["Update.Available.Primary"] = "Open release",
        ["Update.Available.Close"] = "Later",
        ["Update.None.Title"] = "No update found",
        ["Update.None.Content"] = "Installed version: {0}",
        ["Update.Error.Title"] = "Update check failed",
        ["Update.InvalidResponse"] = "Invalid response from GitHub.",

        ["Settings.Title"] = "Settings",
        ["Settings.Columns"] = "Columns",
        ["Settings.Rows"] = "Rows",
        ["Settings.ThumbWidth"] = "Thumbnail width",
        ["Settings.ThumbHeight"] = "Thumbnail height",
        ["Settings.Spacing"] = "Spacing",
        ["Settings.BgHex"] = "Background color (hex)",
        ["Settings.TextHex"] = "Text color (hex)",
        ["Settings.ExportSeparate"] = "Export separate thumbnails",
        ["Settings.ShowTitle"] = "Title",
        ["Settings.ShowDuration"] = "Duration",
        ["Settings.ShowTimestamp"] = "Timestamp",
        ["Settings.ShowFileSize"] = "Size",
        ["Settings.ShowResolution"] = "Resolution",
        ["Settings.ShowBitrate"] = "Bitrate",
        ["Settings.ShowVideoCodec"] = "Video codec",
        ["Settings.ShowAudioCodec"] = "Audio codec",
        ["Settings.RenderConcurrency"] = "Parallelism",
        ["Settings.Layout"] = "Layout",
        ["Settings.ColorsExport"] = "Colors & export",
        ["Settings.ExportFormat"] = "Export format",
        ["Settings.MetadataPreview"] = "Metadata in preview image",
        ["Settings.Cancel"] = "Cancel",
        ["Settings.Apply"] = "Apply",

        ["Render.Label.Title"] = "Title",
        ["Render.Label.Duration"] = "Duration",
        ["Render.Label.Size"] = "Size",
        ["Render.Label.Resolution"] = "Resolution",
        ["Render.Label.Bitrate"] = "Bitrate",
        ["Render.Label.Video"] = "Video",
        ["Render.Label.Audio"] = "Audio",
        ["Render.Value.Unknown"] = "unknown",

        ["View.PreviewFallbackTitle"] = "Preview",
        ["View.Status.Waiting"] = "Waiting...",
        ["View.Status.Rendering"] = "Rendering...",
        ["View.Status.Exported"] = "Exported: {0}",
        ["View.Status.Error"] = "Error: {0}",
        ["View.Error.ImportFailed"] = "Import failed ({0}): {1}",
        ["View.Error.ExportFailed"] = "Export failed ({0}): {1}",
        ["View.Error.UnsupportedExtensions"] = "The following file extensions were not imported due to missing support: {0}",
        ["View.Error.ExtensionCountItem"] = "{0}x {1}",
        ["View.Error.NoExtension"] = "(no extension)"
    };

    private static readonly Dictionary<string, string> Es = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "Configuración",
        ["Main.CheckUpdates"] = "Buscar actualizaciones",
        ["DragDrop.Insert"] = "Insertar",
        ["Settings.ShowTitle"] = "Título",
        ["Settings.ShowDuration"] = "Duración",
        ["Settings.ShowTimestamp"] = "Marca de tiempo",
        ["Settings.ShowFileSize"] = "Tamaño",
        ["Settings.ShowResolution"] = "Resolución",
        ["Settings.ShowBitrate"] = "Tasa de bits",
        ["Settings.ShowVideoCodec"] = "Códec de video",
        ["Settings.ShowAudioCodec"] = "Códec de audio",
        ["Render.Label.Title"] = "Título",
        ["Render.Label.Duration"] = "Duración",
        ["Render.Label.Size"] = "Tamaño",
        ["Render.Label.Resolution"] = "Resolución",
        ["Render.Label.Bitrate"] = "Tasa de bits",
        ["Render.Label.Video"] = "Video",
        ["Render.Label.Audio"] = "Audio",
        ["Render.Value.Unknown"] = "desconocido"
    };

    private static readonly Dictionary<string, string> Fr = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "Paramètres",
        ["Main.CheckUpdates"] = "Vérifier les mises à jour",
        ["DragDrop.Insert"] = "Insérer",
        ["Settings.ShowTitle"] = "Titre",
        ["Settings.ShowDuration"] = "Durée",
        ["Settings.ShowTimestamp"] = "Horodatage",
        ["Settings.ShowFileSize"] = "Taille",
        ["Settings.ShowResolution"] = "Résolution",
        ["Settings.ShowBitrate"] = "Débit binaire",
        ["Settings.ShowVideoCodec"] = "Codec vidéo",
        ["Settings.ShowAudioCodec"] = "Codec audio",
        ["Render.Label.Title"] = "Titre",
        ["Render.Label.Duration"] = "Durée",
        ["Render.Label.Size"] = "Taille",
        ["Render.Label.Resolution"] = "Résolution",
        ["Render.Label.Bitrate"] = "Débit binaire",
        ["Render.Label.Video"] = "Vidéo",
        ["Render.Label.Audio"] = "Audio",
        ["Render.Value.Unknown"] = "inconnu"
    };

    private static readonly Dictionary<string, string> Pt = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "Configurações",
        ["Main.CheckUpdates"] = "Verificar atualizações",
        ["DragDrop.Insert"] = "Inserir",
        ["Settings.ShowTitle"] = "Título",
        ["Settings.ShowDuration"] = "Duração",
        ["Settings.ShowTimestamp"] = "Timestamp",
        ["Settings.ShowFileSize"] = "Tamanho",
        ["Settings.ShowResolution"] = "Resolução",
        ["Settings.ShowBitrate"] = "Taxa de bits",
        ["Settings.ShowVideoCodec"] = "Codec de vídeo",
        ["Settings.ShowAudioCodec"] = "Codec de áudio",
        ["Render.Label.Title"] = "Título",
        ["Render.Label.Duration"] = "Duração",
        ["Render.Label.Size"] = "Tamanho",
        ["Render.Label.Resolution"] = "Resolução",
        ["Render.Label.Bitrate"] = "Taxa de bits",
        ["Render.Label.Video"] = "Vídeo",
        ["Render.Label.Audio"] = "Áudio",
        ["Render.Value.Unknown"] = "desconhecido"
    };

    private static readonly Dictionary<string, string> Ru = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "Настройки",
        ["Main.CheckUpdates"] = "Проверить обновления",
        ["DragDrop.Insert"] = "Вставить",
        ["Settings.ShowTitle"] = "Название",
        ["Settings.ShowDuration"] = "Длительность",
        ["Settings.ShowTimestamp"] = "Временная метка",
        ["Settings.ShowFileSize"] = "Размер",
        ["Settings.ShowResolution"] = "Разрешение",
        ["Settings.ShowBitrate"] = "Битрейт",
        ["Settings.ShowVideoCodec"] = "Видеокодек",
        ["Settings.ShowAudioCodec"] = "Аудиокодек",
        ["Render.Label.Title"] = "Название",
        ["Render.Label.Duration"] = "Длительность",
        ["Render.Label.Size"] = "Размер",
        ["Render.Label.Resolution"] = "Разрешение",
        ["Render.Label.Bitrate"] = "Битрейт",
        ["Render.Label.Video"] = "Видео",
        ["Render.Label.Audio"] = "Аудио",
        ["Render.Value.Unknown"] = "неизвестно"
    };

    private static readonly Dictionary<string, string> Tr = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "Ayarlar",
        ["Main.CheckUpdates"] = "Güncellemeleri denetle",
        ["DragDrop.Insert"] = "Yapıştır",
        ["Settings.ShowTitle"] = "Başlık",
        ["Settings.ShowDuration"] = "Süre",
        ["Settings.ShowTimestamp"] = "Zaman damgası",
        ["Settings.ShowFileSize"] = "Boyut",
        ["Settings.ShowResolution"] = "Çözünürlük",
        ["Settings.ShowBitrate"] = "Bit hızı",
        ["Settings.ShowVideoCodec"] = "Video kodeği",
        ["Settings.ShowAudioCodec"] = "Ses kodeği",
        ["Render.Label.Title"] = "Başlık",
        ["Render.Label.Duration"] = "Süre",
        ["Render.Label.Size"] = "Boyut",
        ["Render.Label.Resolution"] = "Çözünürlük",
        ["Render.Label.Bitrate"] = "Bit hızı",
        ["Render.Label.Video"] = "Video",
        ["Render.Label.Audio"] = "Ses",
        ["Render.Value.Unknown"] = "bilinmiyor"
    };

    private static readonly Dictionary<string, string> Ar = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "الإعدادات",
        ["Main.CheckUpdates"] = "التحقق من التحديثات",
        ["DragDrop.Insert"] = "إدراج",
        ["Settings.ShowTitle"] = "العنوان",
        ["Settings.ShowDuration"] = "المدة",
        ["Settings.ShowTimestamp"] = "الطابع الزمني",
        ["Settings.ShowFileSize"] = "الحجم",
        ["Settings.ShowResolution"] = "الدقة",
        ["Settings.ShowBitrate"] = "معدل البت",
        ["Settings.ShowVideoCodec"] = "ترميز الفيديو",
        ["Settings.ShowAudioCodec"] = "ترميز الصوت",
        ["Render.Label.Title"] = "العنوان",
        ["Render.Label.Duration"] = "المدة",
        ["Render.Label.Size"] = "الحجم",
        ["Render.Label.Resolution"] = "الدقة",
        ["Render.Label.Bitrate"] = "معدل البت",
        ["Render.Label.Video"] = "فيديو",
        ["Render.Label.Audio"] = "صوت",
        ["Render.Value.Unknown"] = "غير معروف"
    };

    private static readonly Dictionary<string, string> Bn = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "সেটিংস",
        ["Main.CheckUpdates"] = "আপডেট পরীক্ষা করুন",
        ["DragDrop.Insert"] = "সন্নিবেশ করুন",
        ["Settings.ShowTitle"] = "শিরোনাম",
        ["Settings.ShowDuration"] = "সময়কাল",
        ["Settings.ShowTimestamp"] = "টাইমস্ট্যাম্প",
        ["Settings.ShowFileSize"] = "আকার",
        ["Settings.ShowResolution"] = "রেজোলিউশন",
        ["Settings.ShowBitrate"] = "বিটরেট",
        ["Settings.ShowVideoCodec"] = "ভিডিও কোডেক",
        ["Settings.ShowAudioCodec"] = "অডিও কোডেক",
        ["Render.Label.Title"] = "শিরোনাম",
        ["Render.Label.Duration"] = "সময়কাল",
        ["Render.Label.Size"] = "আকার",
        ["Render.Label.Resolution"] = "রেজোলিউশন",
        ["Render.Label.Bitrate"] = "বিটরেট",
        ["Render.Label.Video"] = "ভিডিও",
        ["Render.Label.Audio"] = "অডিও",
        ["Render.Value.Unknown"] = "অজানা"
    };

    private static readonly Dictionary<string, string> Hi = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "सेटिंग्स",
        ["Main.CheckUpdates"] = "अपडेट जांचें",
        ["DragDrop.Insert"] = "सम्मिलित करें",
        ["Settings.ShowTitle"] = "शीर्षक",
        ["Settings.ShowDuration"] = "अवधि",
        ["Settings.ShowTimestamp"] = "टाइमस्टैम्प",
        ["Settings.ShowFileSize"] = "आकार",
        ["Settings.ShowResolution"] = "रिज़ॉल्यूशन",
        ["Settings.ShowBitrate"] = "बिटरेट",
        ["Settings.ShowVideoCodec"] = "वीडियो कोडेक",
        ["Settings.ShowAudioCodec"] = "ऑडियो कोडेक",
        ["Render.Label.Title"] = "शीर्षक",
        ["Render.Label.Duration"] = "अवधि",
        ["Render.Label.Size"] = "आकार",
        ["Render.Label.Resolution"] = "रिज़ॉल्यूशन",
        ["Render.Label.Bitrate"] = "बिटरेट",
        ["Render.Label.Video"] = "वीडियो",
        ["Render.Label.Audio"] = "ऑडियो",
        ["Render.Value.Unknown"] = "अज्ञात"
    };

    private static readonly Dictionary<string, string> Ja = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "設定",
        ["Main.CheckUpdates"] = "更新を確認",
        ["DragDrop.Insert"] = "挿入",
        ["Settings.ShowTitle"] = "タイトル",
        ["Settings.ShowDuration"] = "再生時間",
        ["Settings.ShowTimestamp"] = "タイムスタンプ",
        ["Settings.ShowFileSize"] = "サイズ",
        ["Settings.ShowResolution"] = "解像度",
        ["Settings.ShowBitrate"] = "ビットレート",
        ["Settings.ShowVideoCodec"] = "ビデオコーデック",
        ["Settings.ShowAudioCodec"] = "オーディオコーデック",
        ["Render.Label.Title"] = "タイトル",
        ["Render.Label.Duration"] = "再生時間",
        ["Render.Label.Size"] = "サイズ",
        ["Render.Label.Resolution"] = "解像度",
        ["Render.Label.Bitrate"] = "ビットレート",
        ["Render.Label.Video"] = "ビデオ",
        ["Render.Label.Audio"] = "オーディオ",
        ["Render.Value.Unknown"] = "不明"
    };

    private static readonly Dictionary<string, string> Ko = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "설정",
        ["Main.CheckUpdates"] = "업데이트 확인",
        ["DragDrop.Insert"] = "삽입",
        ["Settings.ShowTitle"] = "제목",
        ["Settings.ShowDuration"] = "재생 시간",
        ["Settings.ShowTimestamp"] = "타임스탬프",
        ["Settings.ShowFileSize"] = "크기",
        ["Settings.ShowResolution"] = "해상도",
        ["Settings.ShowBitrate"] = "비트레이트",
        ["Settings.ShowVideoCodec"] = "비디오 코덱",
        ["Settings.ShowAudioCodec"] = "오디오 코덱",
        ["Render.Label.Title"] = "제목",
        ["Render.Label.Duration"] = "재생 시간",
        ["Render.Label.Size"] = "크기",
        ["Render.Label.Resolution"] = "해상도",
        ["Render.Label.Bitrate"] = "비트레이트",
        ["Render.Label.Video"] = "비디오",
        ["Render.Label.Audio"] = "오디오",
        ["Render.Value.Unknown"] = "알 수 없음"
    };

    private static readonly Dictionary<string, string> ZhHans = new(En, StringComparer.Ordinal)
    {
        ["Settings.Title"] = "设置",
        ["Main.CheckUpdates"] = "检查更新",
        ["DragDrop.Insert"] = "插入",
        ["Settings.ShowTitle"] = "标题",
        ["Settings.ShowDuration"] = "时长",
        ["Settings.ShowTimestamp"] = "时间戳",
        ["Settings.ShowFileSize"] = "大小",
        ["Settings.ShowResolution"] = "分辨率",
        ["Settings.ShowBitrate"] = "比特率",
        ["Settings.ShowVideoCodec"] = "视频编解码器",
        ["Settings.ShowAudioCodec"] = "音频编解码器",
        ["Render.Label.Title"] = "标题",
        ["Render.Label.Duration"] = "时长",
        ["Render.Label.Size"] = "大小",
        ["Render.Label.Resolution"] = "分辨率",
        ["Render.Label.Bitrate"] = "比特率",
        ["Render.Label.Video"] = "视频",
        ["Render.Label.Audio"] = "音频",
        ["Render.Value.Unknown"] = "未知"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = De,
        ["en"] = En,
        ["es"] = Es,
        ["fr"] = Fr,
        ["pt"] = Pt,
        ["ru"] = Ru,
        ["tr"] = Tr,
        ["ar"] = Ar,
        ["bn"] = Bn,
        ["hi"] = Hi,
        ["ja"] = Ja,
        ["ko"] = Ko,
        ["zh"] = ZhHans
    };

    public static string Get(string key, string fallback)
    {
        var map = ResolveMap(CultureInfo.CurrentUICulture);
        if (map.TryGetValue(key, out var value))
        {
            return value;
        }

        if (En.TryGetValue(key, out var english))
        {
            return english;
        }

        return fallback;
    }

    private static Dictionary<string, string> ResolveMap(CultureInfo culture)
    {
        if (string.Equals(culture.Name, "zh-Hans", StringComparison.OrdinalIgnoreCase))
        {
            return ZhHans;
        }

        if (ByLanguage.TryGetValue(culture.TwoLetterISOLanguageName, out var map))
        {
            return map;
        }

        return En;
    }
}
