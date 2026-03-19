using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using ThumbnailGridStudio.WinUI.Services;
using ThumbnailGridStudio.WinUI.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace ThumbnailGridStudio.WinUI;

public sealed partial class MainWindow : Window
{
    private const string LegacyOutputSettingsFileName = "settings.json";
    private const string OutputSettingsFileName = "output-settings.json";
    public MainViewModel ViewModel { get; } = new();
    private string _lastOutputDirectory = LoadLastOutputDirectory();
    private AppWindow? _appWindow;
    private InputNonClientPointerSource? _nonClientPointerSource;

    public MainWindow()
    {
        InitializeComponent();
        ApplyLocalizedUiText();
        Title = string.Empty;
        ConfigureCustomTitleBar();
        if (Content is FrameworkElement root)
        {
            root.DataContext = ViewModel;
        }
    }

    private static string L(string key, string fallback)
    {
        return Localizer.Get(key, fallback);
    }

    private void ApplyLocalizedUiText()
    {
        NoPreviewText.Text = L("Main.NoPreview", "Keine Vorschau");
    }

    private void ConfigureCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow = appWindow;
        _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(windowId);
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.Changed += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTitleBarInsets(titleBar);
                UpdateDragRegions();
            });
        };
        CustomTitleBar.SizeChanged += (_, _) => UpdateDragRegions();
        TitleBarCenterButtons.SizeChanged += (_, _) => UpdateDragRegions();
        ApplyTitleBarInsets(titleBar);
        UpdateDragRegions();

        TrySetWindowIcon(appWindow);
    }

    private void ApplyTitleBarInsets(AppWindowTitleBar titleBar)
    {
        // Keep the custom titlebar content visually centered across the full window width
        // by compensating system caption button inset on the centered button host only.
        var inset = titleBar.RightInset;
        CustomTitleBar.Padding = new Thickness(0);
        TitleBarCenterButtons.Margin = new Thickness(inset, 0, inset, 0);
    }

    private void UpdateDragRegions()
    {
        if (_nonClientPointerSource is null || _appWindow is null)
        {
            return;
        }

        if (CustomTitleBar.ActualWidth <= 0 || CustomTitleBar.ActualHeight <= 0)
        {
            return;
        }

        var scale = Content is FrameworkElement root && root.XamlRoot is not null
            ? root.XamlRoot.RasterizationScale
            : 1.0;

        var barWidthPx = Math.Max(1, (int)Math.Round(CustomTitleBar.ActualWidth * scale));
        var barHeightPx = Math.Max(1, (int)Math.Round(CustomTitleBar.ActualHeight * scale));

        var leftInsetPx = Math.Max(0, _appWindow.TitleBar.LeftInset);
        var rightInsetPx = Math.Max(0, _appWindow.TitleBar.RightInset);

        Point buttonsTopLeft = default;
        if (TitleBarCenterButtons.ActualWidth > 0)
        {
            var transform = TitleBarCenterButtons.TransformToVisual(CustomTitleBar);
            buttonsTopLeft = transform.TransformPoint(new Point(0, 0));
        }

        var buttonsLeftPx = (int)Math.Floor(buttonsTopLeft.X * scale);
        var buttonsRightPx = (int)Math.Ceiling((buttonsTopLeft.X + TitleBarCenterButtons.ActualWidth) * scale);

        buttonsLeftPx = Math.Clamp(buttonsLeftPx, leftInsetPx, barWidthPx - rightInsetPx);
        buttonsRightPx = Math.Clamp(buttonsRightPx, leftInsetPx, barWidthPx - rightInsetPx);

        var captionRects = new List<RectInt32>(2);

        var leftWidth = buttonsLeftPx - leftInsetPx;
        if (leftWidth > 0)
        {
            captionRects.Add(new RectInt32(leftInsetPx, 0, leftWidth, barHeightPx));
        }

        var rightStart = buttonsRightPx;
        var rightWidth = (barWidthPx - rightInsetPx) - rightStart;
        if (rightWidth > 0)
        {
            captionRects.Add(new RectInt32(rightStart, 0, rightWidth, barHeightPx));
        }

        if (captionRects.Count == 0)
        {
            captionRects.Add(new RectInt32(leftInsetPx, 0, Math.Max(1, barWidthPx - leftInsetPx - rightInsetPx), barHeightPx));
        }

        _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Caption, captionRects.ToArray());
    }

    private async void AddVideosClick(object sender, RoutedEventArgs e)
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        var modeDialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot,
            Title = "Import",
            Content = "Was möchtest du hinzufügen?",
            PrimaryButtonText = "Dateien auswählen",
            SecondaryButtonText = "Ordner auswählen",
            CloseButtonText = "Abbrechen"
        };

        var result = await modeDialog.ShowAsync();
        var importedPaths = new List<string>();

        if (result == ContentDialogResult.Primary)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var files = await picker.PickMultipleFilesAsync();
            if (files is not null)
            {
                importedPaths.AddRange(files.Select(file => file.Path));
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                importedPaths.AddRange(await CollectVideoFilesFromFolderAsync(folder));
            }
        }

        if (importedPaths.Count == 0)
        {
            return;
        }

        await ViewModel.AddVideosAsync(importedPaths);
    }

    private void RemoveSelectedClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RemoveSelected();
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAll();
    }

    private async void ExportAllClick(object sender, RoutedEventArgs e)
    {
        var folderPath = ShowFolderDialog(WindowNative.GetWindowHandle(this), _lastOutputDirectory);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        _lastOutputDirectory = folderPath;
        SaveLastOutputDirectory(folderPath);
        await ViewModel.RenderAllAsync(folderPath);
    }

    private static string LoadLastOutputDirectory()
    {
        var fallback = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        try
        {
            if (TryReadOutputDirectory(GetOutputSettingsPath(), out var currentPath))
            {
                return currentPath;
            }

            if (TryReadOutputDirectory(GetLegacyOutputSettingsPath(), out var legacyPath))
            {
                return legacyPath;
            }
        }
        catch
        {
            // Keep fallback when settings are unavailable.
        }

        return fallback;
    }

    private static void SaveLastOutputDirectory(string path)
    {
        try
        {
            var settingsPath = GetOutputSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            var json = JsonSerializer.Serialize(new OutputSettings { LastOutputDirectory = path });
            File.WriteAllText(settingsPath, json);
        }
        catch
        {
            // Ignore persistence failures.
        }
    }

    private static string GetOutputSettingsPath()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThumbnailGridStudio");
        return Path.Combine(baseDir, OutputSettingsFileName);
    }

    private static string GetLegacyOutputSettingsPath()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThumbnailGridStudio");
        return Path.Combine(baseDir, LegacyOutputSettingsFileName);
    }

    private static bool TryReadOutputDirectory(string path, out string directory)
    {
        directory = string.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<OutputSettings>(json);
        if (string.IsNullOrWhiteSpace(data?.LastOutputDirectory) || !Directory.Exists(data.LastOutputDirectory))
        {
            return false;
        }

        directory = data.LastOutputDirectory;
        return true;
    }

    private static void TrySetWindowIcon(AppWindow appWindow)
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Ignore icon setup errors.
        }
    }

    private sealed class OutputSettings
    {
        public string? LastOutputDirectory { get; set; }
    }

    private static string? ShowFolderDialog(nint ownerWindow, string? initialDirectory)
    {
        IFileDialog? dialog = null;
        IShellItem? initialFolder = null;
        IShellItem? result = null;

        try
        {
            var dialogType = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"));
            if (dialogType is null)
            {
                return null;
            }

            dialog = Activator.CreateInstance(dialogType) as IFileDialog;
            if (dialog is null)
            {
                return null;
            }

            dialog.GetOptions(out var options);
            options |= FileOpenOptions.PickFolders | FileOpenOptions.ForceFileSystem | FileOpenOptions.PathMustExist;
            dialog.SetOptions(options);

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                var iidShellItem = typeof(IShellItem).GUID;
                var hr = SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, in iidShellItem, out var shellItemPtr);
                if (hr >= 0 && shellItemPtr != IntPtr.Zero)
                {
                    initialFolder = (IShellItem)Marshal.GetObjectForIUnknown(shellItemPtr);
                    Marshal.Release(shellItemPtr);
                    dialog.SetDefaultFolder(initialFolder);
                    dialog.SetFolder(initialFolder);
                }
            }

            dialog.Show(ownerWindow);
            dialog.GetResult(out result);
            result.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var pathPtr);
            var path = Marshal.PtrToStringUni(pathPtr);
            Marshal.FreeCoTaskMem(pathPtr);
            return path;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x800704C7)
        {
            return null;
        }
        finally
        {
            if (result is not null)
            {
                Marshal.ReleaseComObject(result);
            }

            if (initialFolder is not null)
            {
                Marshal.ReleaseComObject(initialFolder);
            }

            if (dialog is not null)
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
    }

    public Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility StringToVisibility(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    public Visibility BoolToVisibilityInverse(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RootDragOver(object sender, DragEventArgs e)
    {
        var hasStorageItems = e.DataView.Contains(StandardDataFormats.StorageItems);
        e.AcceptedOperation = hasStorageItems ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (!hasStorageItems)
        {
            return;
        }

        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.Caption = L("DragDrop.Insert", "Einfügen");
    }

    private async void RootDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var files = new List<string>();
        foreach (var item in items)
        {
            switch (item)
            {
                case StorageFile file:
                    files.Add(file.Path);
                    break;
                case StorageFolder folder:
                    files.AddRange(await CollectVideoFilesFromFolderAsync(folder));
                    break;
            }
        }

        if (files.Count == 0)
        {
            return;
        }

        await ViewModel.AddVideosAsync(files);
    }

    private static async Task<List<string>> CollectVideoFilesFromFolderAsync(StorageFolder folder)
    {
        var result = new List<string>();
        var pending = new Queue<StorageFolder>();
        pending.Enqueue(folder);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            IReadOnlyList<IStorageItem>? items;
            try
            {
                items = await current.GetItemsAsync();
            }
            catch
            {
                continue;
            }

            foreach (var item in items)
            {
                switch (item)
                {
                    case StorageFile file:
                        result.Add(file.Path);
                        break;
                    case StorageFolder child:
                        pending.Enqueue(child);
                        break;
                }
            }
        }

        return result;
    }

    private async void CheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        await ShowUpdateCheckAsync();
    }

    private async Task ShowUpdateCheckAsync()
    {
        try
        {
            var result = await UpdateService.CheckForUpdatesAsync();
            if (Content is not FrameworkElement root)
            {
                return;
            }

            if (result.IsUpdateAvailable && result.Release is not null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = root.XamlRoot,
                    Title = L("Update.Available.Title", "Update verfÃ¼gbar"),
                    Content = string.Format(
                        CultureInfo.CurrentCulture,
                        L("Update.Available.Content", "Neue Version: {0}\nInstalliert: {1}\n\nMÃ¶chtest du die Release-Seite Ã¶ffnen?"),
                        result.Release.TagName,
                        result.LocalVersion),
                    PrimaryButtonText = L("Update.Available.Primary", "Release Ã¶ffnen"),
                    CloseButtonText = L("Update.Available.Close", "SpÃ¤ter")
                };

                var dialogResult = await dialog.ShowAsync();
                if (dialogResult == ContentDialogResult.Primary)
                {
                    await Launcher.LaunchUriAsync(new Uri(result.Release.HtmlUrl));
                }
            }
            else
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = root.XamlRoot,
                    Title = L("Update.None.Title", "Kein Update gefunden"),
                    Content = string.Format(
                        CultureInfo.CurrentCulture,
                        L("Update.None.Content", "Installierte Version: {0}"),
                        result.LocalVersion),
                    CloseButtonText = "OK"
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            if (Content is not FrameworkElement root)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = root.XamlRoot,
                Title = L("Update.Error.Title", "Update-PrÃ¼fung fehlgeschlagen"),
                Content = ex.Message,
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
    }

    private async void OpenSettingsClick(object sender, RoutedEventArgs e)
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        var settings = ViewModel.Settings;
        var columns = new TextBox { Header = L("Settings.Columns", "Spalten"), Text = settings.ColumnsText };
        var rows = new TextBox { Header = L("Settings.Rows", "Zeilen"), Text = settings.RowsText };
        var width = new TextBox { Header = L("Settings.ThumbWidth", "Thumbnail Breite"), Text = settings.ThumbnailWidthText };
        var height = new TextBox { Header = L("Settings.ThumbHeight", "Thumbnail HÃ¶he"), Text = settings.ThumbnailHeightText };
        var spacing = new TextBox { Header = L("Settings.Spacing", "Abstand"), Text = settings.SpacingText };
        var bgHex = new TextBox { Header = L("Settings.BgHex", "Hintergrundfarbe (HEX-Code)"), Text = settings.BackgroundHex };
        var textHex = new TextBox { Header = L("Settings.TextHex", "Schriftfarbe (HEX-Code)"), Text = settings.MetadataHex };
        var exportSeparate = new CheckBox { Content = L("Settings.ExportSeparate", "Separate Thumbnails exportieren"), IsChecked = settings.ExportSeparateThumbnails };
        var titleVisible = new CheckBox { Content = L("Settings.ShowTitle", "Titel"), IsChecked = settings.ShowFileName };
        var titleFontPx = new TextBox { Text = settings.FileNameFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var durationVisible = new CheckBox { Content = L("Settings.ShowDuration", "Laufzeit"), IsChecked = settings.ShowDuration };
        var durationFontPx = new TextBox { Text = settings.DurationFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var timestampVisible = new CheckBox { Content = L("Settings.ShowTimestamp", "Timestamp"), IsChecked = settings.ShowTimestamp };
        var timestampFontPx = new TextBox { Text = settings.TimestampFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var fileSizeVisible = new CheckBox { Content = L("Settings.ShowFileSize", "Größe"), IsChecked = settings.ShowFileSize };
        var fileSizeFontPx = new TextBox { Text = settings.FileSizeFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var resolutionVisible = new CheckBox { Content = L("Settings.ShowResolution", "Auflösung"), IsChecked = settings.ShowResolution };
        var resolutionFontPx = new TextBox { Text = settings.ResolutionFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var bitrateVisible = new CheckBox { Content = L("Settings.ShowBitrate", "Bitrate"), IsChecked = settings.ShowBitrate };
        var bitrateFontPx = new TextBox { Text = settings.BitrateFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var videoCodecVisible = new CheckBox { Content = L("Settings.ShowVideoCodec", "Video-Codec"), IsChecked = settings.ShowVideoCodec };
        var videoCodecFontPx = new TextBox { Text = settings.VideoCodecFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var audioCodecVisible = new CheckBox { Content = L("Settings.ShowAudioCodec", "Audio-Codec"), IsChecked = settings.ShowAudioCodec };
        var audioCodecFontPx = new TextBox { Text = settings.AudioCodecFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var renderConcurrency = new NumberBox
        {
            Header = L("Settings.RenderConcurrency", "ParallelitÃ¤t"),
            Minimum = 1,
            Maximum = 8,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = settings.RenderConcurrency
        };

        var exportFormat = new ComboBox();
        exportFormat.Items.Add("JPG");
        exportFormat.Items.Add("PNG");
        exportFormat.SelectedIndex = settings.ExportFormatIndex;

        UpdateDimensionPlaceholders(width, height);
        width.TextChanged += (_, _) => UpdateDimensionPlaceholders(width, height);
        height.TextChanged += (_, _) => UpdateDimensionPlaceholders(width, height);
        var rootWidth = root.ActualWidth > 0 ? root.ActualWidth : 1200d;
        var rootHeight = root.ActualHeight > 0 ? root.ActualHeight : 800d;
        var dialogWidth = Math.Clamp(rootWidth * 0.94, 640d, 1300d);
        var dialogHeight = Math.Clamp(rootHeight * 0.9, 420d, 900d);
        var useTwoColumns = dialogWidth >= 900d;

        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new TextBlock { Text = L("Settings.Layout", "Layout"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        left.Children.Add(BuildPairRow(columns, rows));
        left.Children.Add(BuildPairRow(width, height));
        left.Children.Add(spacing);
        left.Children.Add(renderConcurrency);

        var right = new StackPanel { Spacing = 8 };
        right.Children.Add(new TextBlock { Text = L("Settings.ColorsExport", "Farben & Export"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        right.Children.Add(bgHex);
        right.Children.Add(textHex);
        right.Children.Add(new TextBlock { Text = L("Settings.ExportFormat", "Exportformat") });
        right.Children.Add(exportFormat);
        right.Children.Add(exportSeparate);

        var layoutGrid = new Grid
        {
            ColumnSpacing = 22,
            RowSpacing = 10
        };
        layoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layoutGrid.Children.Add(left);
        if (useTwoColumns)
        {
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layoutGrid.Children.Add(right);
            Grid.SetColumn(right, 1);
        }
        else
        {
            layoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layoutGrid.Children.Add(right);
            Grid.SetRow(right, 1);
        }

        var metadataSection = new StackPanel { Spacing = 8 };
        metadataSection.Children.Add(new TextBlock
        {
            Text = L("Settings.MetadataPreview", "Metadaten im Vorschaubild"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var metadataBlocks = new Grid { ColumnSpacing = 18, RowSpacing = 8 };
        metadataBlocks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftMetaBlock = new StackPanel { Spacing = 8 };
        leftMetaBlock.Children.Add(BuildMetadataRow(titleVisible, titleFontPx));
        leftMetaBlock.Children.Add(BuildMetadataRow(durationVisible, durationFontPx));
        leftMetaBlock.Children.Add(BuildMetadataRow(fileSizeVisible, fileSizeFontPx));
        leftMetaBlock.Children.Add(BuildMetadataRow(timestampVisible, timestampFontPx));

        var rightMetaBlock = new StackPanel { Spacing = 8 };
        rightMetaBlock.Children.Add(BuildMetadataRow(resolutionVisible, resolutionFontPx));
        rightMetaBlock.Children.Add(BuildMetadataRow(bitrateVisible, bitrateFontPx));
        rightMetaBlock.Children.Add(BuildMetadataRow(videoCodecVisible, videoCodecFontPx));
        rightMetaBlock.Children.Add(BuildMetadataRow(audioCodecVisible, audioCodecFontPx));

        metadataBlocks.Children.Add(leftMetaBlock);
        if (useTwoColumns)
        {
            metadataBlocks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metadataBlocks.Children.Add(rightMetaBlock);
            Grid.SetColumn(rightMetaBlock, 1);
        }
        else
        {
            metadataBlocks.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metadataBlocks.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metadataBlocks.Children.Add(rightMetaBlock);
            Grid.SetRow(rightMetaBlock, 1);
        }

        metadataSection.Children.Add(metadataBlocks);
        
        var formStack = new StackPanel { Spacing = 14 };
        formStack.Children.Add(layoutGrid);
        formStack.Children.Add(metadataSection);

        var settingsScrollViewer = new ScrollViewer
        {
            Content = formStack,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(220d, dialogHeight - 190d)
        };

        var buttonRow = new Grid
        {
            Margin = new Thickness(0, 24, 0, 0)
        };
        if (useTwoColumns)
        {
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        else
        {
            buttonRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            buttonRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            buttonRow.RowSpacing = 10;
        }

        var checkUpdatesButton = new Button
        {
            Content = L("Main.CheckUpdates", "Auf Updates prÃ¼fen"),
            MinWidth = 170,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var cancelButton = new Button
        {
            Content = L("Settings.Cancel", "Abbrechen"),
            MinWidth = 120
        };
        var applyButton = new Button
        {
            Content = L("Settings.Apply", "Ãœbernehmen"),
            MinWidth = 140
        };

        var rightButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        rightButtons.Children.Add(cancelButton);
        rightButtons.Children.Add(applyButton);

        buttonRow.Children.Add(checkUpdatesButton);
        buttonRow.Children.Add(rightButtons);
        if (useTwoColumns)
        {
            Grid.SetColumn(rightButtons, 1);
        }
        else
        {
            Grid.SetRow(rightButtons, 1);
        }
        var contentHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RowSpacing = 12
        };
        contentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentHost.Children.Add(settingsScrollViewer);
        contentHost.Children.Add(buttonRow);
        Grid.SetRow(buttonRow, 1);

        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot,
            Title = L("Settings.Title", "Einstellungen"),
            Content = contentHost
        };
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMinWidth"] = Math.Min(dialogWidth, 640d);
        dialog.Resources["ContentDialogMaxHeight"] = dialogHeight;

        var apply = false;
        var runUpdateCheck = false;
        checkUpdatesButton.Click += (_, _) =>
        {
            runUpdateCheck = true;
            dialog.Hide();
        };
        cancelButton.Click += (_, _) => dialog.Hide();
        applyButton.Click += (_, _) =>
        {
            apply = true;
            dialog.Hide();
        };

        await dialog.ShowAsync();
        if (runUpdateCheck)
        {
            await ShowUpdateCheckAsync();
            return;
        }

        if (!apply)
        {
            return;
        }

        settings.ColumnsText = columns.Text;
        settings.RowsText = rows.Text;
        settings.ThumbnailWidthText = width.Text;
        settings.ThumbnailHeightText = height.Text;
        settings.SpacingText = spacing.Text;
        settings.BackgroundHex = bgHex.Text;
        settings.MetadataHex = textHex.Text;
        settings.ExportFormatIndex = exportFormat.SelectedIndex;
        settings.ExportSeparateThumbnails = exportSeparate.IsChecked == true;
        settings.ShowFileName = titleVisible.IsChecked == true;
        settings.FileNameFontSize = ParseFontPx(titleFontPx.Text, settings.FileNameFontSize);
        settings.ShowDuration = durationVisible.IsChecked == true;
        settings.DurationFontSize = ParseFontPx(durationFontPx.Text, settings.DurationFontSize);
        settings.ShowTimestamp = timestampVisible.IsChecked == true;
        settings.TimestampFontSize = ParseFontPx(timestampFontPx.Text, settings.TimestampFontSize);
        settings.ShowFileSize = fileSizeVisible.IsChecked == true;
        settings.FileSizeFontSize = ParseFontPx(fileSizeFontPx.Text, settings.FileSizeFontSize);
        settings.ShowResolution = resolutionVisible.IsChecked == true;
        settings.ResolutionFontSize = ParseFontPx(resolutionFontPx.Text, settings.ResolutionFontSize);
        settings.ShowBitrate = bitrateVisible.IsChecked == true;
        settings.BitrateFontSize = ParseFontPx(bitrateFontPx.Text, settings.BitrateFontSize);
        settings.ShowVideoCodec = videoCodecVisible.IsChecked == true;
        settings.VideoCodecFontSize = ParseFontPx(videoCodecFontPx.Text, settings.VideoCodecFontSize);
        settings.ShowAudioCodec = audioCodecVisible.IsChecked == true;
        settings.AudioCodecFontSize = ParseFontPx(audioCodecFontPx.Text, settings.AudioCodecFontSize);
        settings.RenderConcurrency = (int)Math.Round(renderConcurrency.Value);
    }

    private static float ParseFontPx(string text, float fallback)
    {
        var normalized = text.Replace(',', '.');
        if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, 8f, 96f);
    }

    private static UIElement BuildMetadataRow(CheckBox checkBox, TextBox fontSizeBox)
    {
        fontSizeBox.Width = 84;
        fontSizeBox.VerticalAlignment = VerticalAlignment.Center;
        checkBox.VerticalAlignment = VerticalAlignment.Center;

        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(checkBox);
        row.Children.Add(fontSizeBox);
        var pxLabel = new TextBlock
        {
            Text = "px",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
        };
        row.Children.Add(pxLabel);
        Grid.SetColumn(fontSizeBox, 1);
        Grid.SetColumn(pxLabel, 2);
        return row;
    }

    private static UIElement BuildPairRow(Control leftControl, Control rightControl)
    {
        leftControl.Margin = new Thickness(0, 0, 8, 0);
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(leftControl);
        grid.Children.Add(rightControl);
        Grid.SetColumn(rightControl, 1);
        return grid;
    }

    private static void UpdateDimensionPlaceholders(TextBox width, TextBox height)
    {
        var hasWidth = !string.IsNullOrWhiteSpace(width.Text);
        var hasHeight = !string.IsNullOrWhiteSpace(height.Text);

        width.PlaceholderText = hasHeight && !hasWidth ? "auto" : "320";
        height.PlaceholderText = hasWidth && !hasHeight ? "auto" : "180";
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(nint parent);
        void SetFileTypes(uint cFileTypes, IntPtr filterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint iFileType);
        void Advise(IntPtr pfde, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(FileOpenOptions fos);
        void GetOptions(out FileOpenOptions fos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, uint alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(in Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, in Guid bhid, in Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [Flags]
    private enum FileOpenOptions : uint
    {
        PickFolders = 0x00000020,
        ForceFileSystem = 0x00000040,
        PathMustExist = 0x00000800
    }

    private enum ShellItemDisplayName : uint
    {
        FileSystemPath = 0x80058000
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        in Guid riid,
        out IntPtr ppv);
}


