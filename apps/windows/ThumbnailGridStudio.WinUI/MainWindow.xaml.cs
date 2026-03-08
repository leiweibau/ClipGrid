using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using ThumbnailGridStudio.WinUI.Services;
using ThumbnailGridStudio.WinUI.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ThumbnailGridStudio.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();
    private string _lastOutputDirectory = LoadLastOutputDirectory();

    public MainWindow()
    {
        InitializeComponent();
        Title = string.Empty;
        ConfigureCustomTitleBar();
        if (Content is FrameworkElement root)
        {
            root.DataContext = ViewModel;
        }
    }

    private void ConfigureCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomTitleBarDragRegion);

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        CustomTitleBar.Padding = new Thickness(0, 0, titleBar.RightInset, 0);

        TrySetWindowIcon(appWindow);
    }

    private async void AddVideosClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".mov");
        picker.FileTypeFilter.Add(".m4v");
        picker.FileTypeFilter.Add(".avi");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".webm");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return;
        }

        await ViewModel.AddVideosAsync(files.Select(file => file.Path));
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
            var settingsPath = GetOutputSettingsPath();
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var data = JsonSerializer.Deserialize<OutputSettings>(json);
                var path = data?.LastOutputDirectory;
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    return path;
                }
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
        return Path.Combine(baseDir, "settings.json");
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
        e.AcceptedOperation = DataPackageOperation.Copy;
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
                    Title = "Update verfügbar",
                    Content = $"Neue Version: {result.Release.TagName}\nInstalliert: {result.LocalVersion}\n\nMöchtest du die Release-Seite öffnen?",
                    PrimaryButtonText = "Release öffnen",
                    CloseButtonText = "Später"
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
                    Title = "Kein Update gefunden",
                    Content = $"Installierte Version: {result.LocalVersion}",
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
                Title = "Update-Prüfung fehlgeschlagen",
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
        var columns = new TextBox { Header = "Spalten", Text = settings.ColumnsText };
        var rows = new TextBox { Header = "Zeilen", Text = settings.RowsText };
        var width = new TextBox { Header = "Thumbnail Breite", Text = settings.ThumbnailWidthText };
        var height = new TextBox { Header = "Thumbnail Höhe", Text = settings.ThumbnailHeightText };
        var spacing = new TextBox { Header = "Abstand", Text = settings.SpacingText };
        var bgHex = new TextBox { Header = "Hintergrundfarbe (HEX-Code)", Text = settings.BackgroundHex };
        var textHex = new TextBox { Header = "Schriftfarbe (HEX-Code)", Text = settings.MetadataHex };
        var exportSeparate = new CheckBox { Content = "Separate Thumbnails exportieren", IsChecked = settings.ExportSeparateThumbnails };
        var titleVisible = new CheckBox { Content = "Titel anzeigen", IsChecked = settings.ShowFileName };
        var titleFontPx = new TextBox { Text = settings.FileNameFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var durationVisible = new CheckBox { Content = "Laufzeit anzeigen", IsChecked = settings.ShowDuration };
        var durationFontPx = new TextBox { Text = settings.DurationFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var timestampVisible = new CheckBox { Content = "Timestamp anzeigen", IsChecked = settings.ShowTimestamp };
        var timestampFontPx = new TextBox { Text = settings.TimestampFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var fileSizeVisible = new CheckBox { Content = "Dateigröße anzeigen", IsChecked = settings.ShowFileSize };
        var fileSizeFontPx = new TextBox { Text = settings.FileSizeFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var resolutionVisible = new CheckBox { Content = "Auflösung anzeigen", IsChecked = settings.ShowResolution };
        var resolutionFontPx = new TextBox { Text = settings.ResolutionFontSize.ToString("0", CultureInfo.InvariantCulture), PlaceholderText = "12" };
        var renderConcurrency = new NumberBox
        {
            Header = "Parallelität",
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

        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new TextBlock { Text = "Layout", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        left.Children.Add(BuildPairRow(columns, rows));
        left.Children.Add(BuildPairRow(width, height));
        left.Children.Add(spacing);
        left.Children.Add(renderConcurrency);

        var right = new StackPanel { Spacing = 8 };
        right.Children.Add(new TextBlock { Text = "Farben & Export", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        right.Children.Add(bgHex);
        right.Children.Add(textHex);
        right.Children.Add(new TextBlock { Text = "Exportformat" });
        right.Children.Add(exportFormat);
        right.Children.Add(exportSeparate);

        var layoutGrid = new Grid
        {
            ColumnSpacing = 22,
            Width = 980
        };
        layoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layoutGrid.Children.Add(left);
        layoutGrid.Children.Add(right);
        Grid.SetColumn(right, 1);

        var metadataSection = new StackPanel { Spacing = 8 };
        metadataSection.Children.Add(new TextBlock
        {
            Text = "Metadaten im Vorschaubild",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var metadataBlocks = new Grid { ColumnSpacing = 18, Width = 980 };
        metadataBlocks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metadataBlocks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftMetaBlock = new StackPanel { Spacing = 8 };
        leftMetaBlock.Children.Add(BuildMetadataRow(titleVisible, titleFontPx));
        leftMetaBlock.Children.Add(BuildMetadataRow(durationVisible, durationFontPx));
        leftMetaBlock.Children.Add(BuildMetadataRow(timestampVisible, timestampFontPx));

        var rightMetaBlock = new StackPanel { Spacing = 8 };
        rightMetaBlock.Children.Add(BuildMetadataRow(fileSizeVisible, fileSizeFontPx));
        rightMetaBlock.Children.Add(BuildMetadataRow(resolutionVisible, resolutionFontPx));

        metadataBlocks.Children.Add(leftMetaBlock);
        metadataBlocks.Children.Add(rightMetaBlock);
        Grid.SetColumn(rightMetaBlock, 1);

        metadataSection.Children.Add(metadataBlocks);
        
        var formStack = new StackPanel { Spacing = 14, Width = 1000 };
        formStack.Children.Add(layoutGrid);
        formStack.Children.Add(metadataSection);

        var contentHost = new StackPanel { Spacing = 12 };
        contentHost.Children.Add(new ScrollViewer
        {
            Content = formStack,
            Width = 1000,
            MaxHeight = 620
        });

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 40, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "Abbrechen",
            MinWidth = 110,
            MaxWidth = 130
        };
        var applyButton = new Button
        {
            Content = "Übernehmen",
            MinWidth = 110,
            MaxWidth = 130
        };
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(applyButton);
        contentHost.Children.Add(buttonRow);

        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot,
            Title = "Einstellungen",
            Content = contentHost
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1200d;
        dialog.Resources["ContentDialogMinWidth"] = 1000d;

        var apply = false;
        cancelButton.Click += (_, _) => dialog.Hide();
        applyButton.Click += (_, _) =>
        {
            apply = true;
            dialog.Hide();
        };

        await dialog.ShowAsync();
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(255) });
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
