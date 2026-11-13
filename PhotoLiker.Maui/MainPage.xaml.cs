using CommunityToolkit.Maui.Storage;
using PhotoLiker.Core;
using PhotoLiker.Maui.Services;
using PhotoLiker.Maui.SmokeTour;

namespace PhotoLiker.Maui;

public partial class MainPage : ContentPage
{
    private const int PreviewSiblingCount = 6;
    private const int ThumbnailPixels = 144;              // twice the 72-pixel strip, for crisp thumbnails on high-DPI screens
    private const long CacheBudgetBytes = 256L * 1024 * 1024;

    private readonly PhotoBrowserService _service;
    private readonly PhotoCacheService _cache;
    private readonly List<string> _metadataLines = [];
    private readonly Dictionary<string, Image> _visibleThumbnailImages = new(StringComparer.Ordinal);
    private readonly SmokeTourOptions? _tourOptions;

    private string? _currentMapLink;
    private string? _currentMainImagePath;
    private bool _tourStarted;

    public MainPage()
    {
        InitializeComponent();
        _service = new PhotoBrowserService();
        _cache = new PhotoCacheService(CacheBudgetBytes, thumbResizer: ThumbnailResizer.ResizeAsync);
        _cache.SetThumbSize(ThumbnailPixels);

        _service.StatusChanged += OnStatusChanged;
        _service.ImageLoaded += OnImageLoaded;
        _service.FolderLoaded += OnFolderLoaded;
        _cache.Loaded += OnCacheEntryLoaded;
        _cache.ThumbReady += OnCacheThumbReady;

        MetadataCollection.ItemsSource = _metadataLines;
        PermissionHelpLabel.Text = GetPermissionHelpText();

        _tourOptions = SmokeTourOptions.Parse(Environment.GetCommandLineArgs());
        Loaded += OnPageLoadedForTour;

#if WINDOWS
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        if (_tourOptions is null)
            _service.RestoreLastSession();
#endif

        UpdateNavigationButtons();
    }

    // ── the viewer's keyboard, shared by every platform (and pressed by the smoke tour) ─────────

    /// <summary>One key of the viewer: the platform key handlers map their own codes onto <see cref="ViewerKey"/> and call this.</summary>
    internal bool HandleViewerKey(ViewerKey key)
    {
        var files = _service.Settings.Files;
        if (files.Count == 0)
            return false;

        switch (key)
        {
            case ViewerKey.Previous:
                _service.GoPrevious();
                break;
            case ViewerKey.Next:
                _service.GoNext();
                break;
            case ViewerKey.First:
                _service.LoadImage(files[0].OriginalFilePath);
                break;
            case ViewerKey.Last:
                _service.LoadImage(files[^1].OriginalFilePath);
                break;
            case ViewerKey.ToggleLike:
                _service.ToggleLike();
                UpdateLikeUi();
                break;
            case ViewerKey.PreviousMonth:
                JumpMonth(forward: false);
                break;
            case ViewerKey.NextMonth:
                JumpMonth(forward: true);
                break;
            default:
                return false;
        }

        UpdateNavigationButtons();
        return true;
    }

    /// <summary>PageUp / PageDown: the first photo of the previous / the next month, by the dates of the files.</summary>
    private void JumpMonth(bool forward)
    {
        var files = _service.Settings.Files;
        var now = DateTime.Now;
        var target = MonthNavigation.FindMonthStart(files, _service.Settings.CurrentIndex, f => ViewerDates.MonthOf(f, now), forward);
        if (target < 0)
        {
            SetStatus(forward ? "This is already the last month." : "This is already the first month.");
            return;
        }

        _service.LoadImage(files[target].OriginalFilePath);
        SetStatus($"{MonthNavigation.Describe(ViewerDates.MonthOf(files[target], now))} — photo {target + 1} of {files.Count}");
    }

    // ── buttons ─────────────────────────────────────────────────────────────────────────────────

    private async void OnOpenFolderClicked(object? sender, EventArgs e)
    {
        try
        {
            var folder = await PickFolderAsync();
            if (!string.IsNullOrWhiteSpace(folder))
            {
                OpenFolderCore(folder);
                return;
            }

            await DisplayAlertAsync("Open folder", GetFolderPickerCancelledMessage(), "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Open folder", $"{GetFolderPickerErrorPrefix()} {ex.Message}", "OK");
        }
    }

    private void OnPreviousClicked(object? sender, EventArgs e)
    {
        _service.GoPrevious();
        UpdateNavigationButtons();
    }

    private void OnNextClicked(object? sender, EventArgs e)
    {
        _service.GoNext();
        UpdateNavigationButtons();
    }

    private void OnLikeClicked(object? sender, EventArgs e)
    {
        _service.ToggleLike();
        UpdateLikeUi();
    }

    private async void OnOpenMapClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentMapLink))
            return;

        try
        {
            await Launcher.OpenAsync(_currentMapLink);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Map", ex.Message, "OK");
        }
    }

    private void OpenFolderCore(string folder)
    {
        ResetImageState();
        _service.OpenFolder(folder);
    }

    // ── the service speaks ──────────────────────────────────────────────────────────────────────

    private void OnStatusChanged(string message) => SetStatus(message);

    private void OnFolderLoaded()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RenderThumbnails();
            UpdateNavigationButtons();
        });
    }

    private void OnImageLoaded(PhotoFile? photoFile, MetadataViewModel? metadata)
    {
        if (photoFile is null)
        {
            // A like / unlike of a photo that is not in the list: only the indicator can change
            MainThread.BeginInvokeOnMainThread(UpdateLikeUi);
            return;
        }

        var filePath = photoFile.OriginalFilePath;
        // Like / unlike re-announces the shown photo without metadata: the viewer only refreshes its indicators then
        var isLikeRefresh = metadata is null && string.Equals(filePath, _currentMainImagePath, StringComparison.Ordinal);
        _currentMainImagePath = filePath;

        if (!isLikeRefresh)
            _cache.SetWindow(ViewerWindowPlanner.NearestFirst(_service.Settings.Files, _service.Settings.CurrentIndex, PreviewSiblingCount));

        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateCurrentFileLabel();
            RenderThumbnails();
            if (!isLikeRefresh)
                RenderMetadata(metadata);
            UpdateLikeUi();
            UpdateNavigationButtons();

            // The last shown photo stays on screen until the new one is ready: no blank flash while it loads
            if (_cache.TryGet(filePath, out var entry))
                MainImage.Source = CreateImageSource(entry.Full);
        });

        if (!isLikeRefresh)
            _ = ShowMainImageAsync(filePath);
    }

    private async Task ShowMainImageAsync(string filePath)
    {
        var entry = await _cache.GetOrLoadAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        if (entry is null)
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (string.Equals(_currentMainImagePath, filePath, StringComparison.Ordinal))
                MainImage.Source = CreateImageSource(entry.Full);
        });
    }

    // ── the cache speaks (on pool threads) ──────────────────────────────────────────────────────

    private void OnCacheEntryLoaded(string path, PhotoCacheEntry entry)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (string.Equals(path, _currentMainImagePath, StringComparison.Ordinal) && MainImage.Source is null)
                MainImage.Source = CreateImageSource(entry.Full);

            if (_visibleThumbnailImages.TryGetValue(path, out var image) && image.Source is null)
                image.Source = CreateImageSource(entry.ThumbOrFull);
        });
    }

    private void OnCacheThumbReady(string path, PhotoCacheEntry entry)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_visibleThumbnailImages.TryGetValue(path, out var image))
                image.Source = CreateImageSource(entry.ThumbOrFull);
        });
    }

    // ── rendering ───────────────────────────────────────────────────────────────────────────────

    private void RenderMetadata(MetadataViewModel? metadata)
    {
        _metadataLines.Clear();

        if (metadata is not null)
        {
            foreach (var group in metadata.GetGroups())
            {
                _metadataLines.Add($"[{group.Title}]");
                foreach (var item in group.Items)
                    _metadataLines.Add($"{item.Name}: {item.Value}");
            }

            _currentMapLink = metadata.MapLink;
        }
        else
        {
            _currentMapLink = null;
        }

        MetadataCollection.ItemsSource = null;
        MetadataCollection.ItemsSource = _metadataLines;
        MapButton.IsVisible = !string.IsNullOrWhiteSpace(_currentMapLink);
    }

    private void RenderThumbnails()
    {
        _visibleThumbnailImages.Clear();
        ThumbnailContainer.Children.Clear();

        var files = _service.Settings.Files;
        if (files.Count == 0)
            return;

        var currentIndex = Math.Clamp(_service.Settings.CurrentIndex, 0, files.Count - 1);
        var (from, to) = ViewerWindowPlanner.StripRange(files.Count, currentIndex, PreviewSiblingCount);

        for (var i = from; i <= to; i++)
        {
            var photo = files[i];
            var filePath = photo.OriginalFilePath;
            var image = new Image
            {
                WidthRequest = 72,
                HeightRequest = 72,
                Aspect = Aspect.AspectFill
            };

            if (_cache.TryGet(filePath, out var entry))
                image.Source = CreateImageSource(entry.ThumbOrFull);

            var content = new Grid();
            content.Children.Add(image);
            if (!string.IsNullOrEmpty(photo.LikedFilePath))
            {
                content.Children.Add(new Label
                {
                    Text = "♥",
                    TextColor = Colors.Crimson,
                    FontSize = 14,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Start,
                    Margin = new Thickness(0, 0, 3, 0),
                });
            }

            var frame = new Border
            {
                Stroke = i == currentIndex ? Colors.DeepSkyBlue : Colors.Gray,
                StrokeThickness = i == currentIndex ? 2 : 1,
                Padding = 1,
                WidthRequest = 74,
                HeightRequest = 74,
                Content = content
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _service.LoadImage(filePath);
            frame.GestureRecognizers.Add(tap);

            _visibleThumbnailImages[filePath] = image;
            ThumbnailContainer.Children.Add(frame);
        }
    }

    private void UpdateCurrentFileLabel()
    {
        var files = _service.Settings.Files;
        var index = _service.Settings.CurrentIndex;
        CurrentFileLabel.Text = index >= 0 && index < files.Count
            ? $"{Path.GetFileName(files[index].OriginalFilePath)} — {index + 1} / {files.Count}"
            : string.Empty;
    }

    private void UpdateLikeUi()
    {
        var liked = _service.IsCurrentLiked;
        LikeButton.Text = liked ? "Unlike" : "Like";
        LikedBadge.IsVisible = liked;
    }

    private void UpdateNavigationButtons()
    {
        var files = _service.Settings.Files;
        var currentIndex = _service.Settings.CurrentIndex;
        var hasFiles = files.Count > 0;

        PreviousOverlayButton.IsVisible = hasFiles && currentIndex > 0;
        NextOverlayButton.IsVisible = hasFiles && currentIndex >= 0 && currentIndex < files.Count - 1;
    }

    private void SetStatus(string message) =>
        MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = message);

    private void ResetImageState()
    {
        _currentMainImagePath = null;
        MainImage.Source = null;
        LikedBadge.IsVisible = false;
        _cache.Clear();
        _visibleThumbnailImages.Clear();
        ThumbnailContainer.Children.Clear();
    }

    private static ImageSource CreateImageSource(byte[] bytes)
    {
        return ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
    }

    // ── the smoke tour (see tools\maui-smoke-tour.ps1): drives this very page like a person would ─

    internal PhotoBrowserService Service => _service;
    internal string StatusText => StatusLabel.Text ?? string.Empty;
    internal string CurrentFileText => CurrentFileLabel.Text ?? string.Empty;
    internal bool IsMainImageShown => MainImage.Source is not null;
    internal bool IsLikedBadgeVisible => LikedBadge.IsVisible;
    internal int VisibleThumbnailCount => ThumbnailContainer.Children.Count;
    internal bool IsPreviousArrowVisible => PreviousOverlayButton.IsVisible;
    internal bool IsNextArrowVisible => NextOverlayButton.IsVisible;

    internal void OpenFolderForTour(string folder) => OpenFolderCore(folder);

    private void OnPageLoadedForTour(object? sender, EventArgs e)
    {
        if (_tourOptions is null || _tourStarted)
            return;

        _tourStarted = true;
        _ = new MauiSmokeTour(this, _tourOptions).RunAsync();
    }

    // ── texts ───────────────────────────────────────────────────────────────────────────────────

    private static string GetPermissionHelpText()
    {
#if ANDROID
        return "Android: choose a folder and grant access when prompted. If access is denied, reopen the picker and allow photo folder access.";
#elif IOS || MACCATALYST
        return "Apple platforms: choose a folder and allow PhotoLiker to access it when the system prompt appears. Some folders may remain restricted by the OS.";
#elif WINDOWS
        return "Windows: choose any local photo folder. Keys: Left / Right photo, Home / End first / last, PageUp / PageDown month, Space like.";
#else
        return "Choose a photo folder and allow access if your device asks for permission.";
#endif
    }

    private static string GetFolderPickerCancelledMessage()
    {
#if ANDROID
        return "No folder was selected. Make sure to grant access to the chosen photo folder when Android prompts you.";
#elif IOS || MACCATALYST
        return "No folder was selected. Choose a folder and approve access when Apple asks for permission.";
#else
        return "No folder was selected.";
#endif
    }

    private static string GetFolderPickerErrorPrefix()
    {
#if ANDROID
        return "Android could not open the folder picker.";
#elif IOS || MACCATALYST
        return "The Apple folder picker could not be opened.";
#elif WINDOWS
        return "Windows could not open the folder picker.";
#else
        return "The folder picker could not be opened.";
#endif
    }

    private static Task<string?> PickFolderAsync()
    {
#if WINDOWS
        return PickFolderOnWindowsAsync();
#else
        return PickFolderWithToolkitAsync();
#endif
    }

#if WINDOWS
    private void OnLoaded(object? sender, EventArgs e)
    {
        if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
        {
            nativeWindow.Content.KeyDown -= OnWindowsKeyDown;
            nativeWindow.Content.KeyDown += OnWindowsKeyDown;
        }

        // The buttons must not swallow the keyboard: Space likes the photo, it never re-clicks the last-used button
        foreach (var button in new[] { LikeButton, MapButton, PreviousOverlayButton, NextOverlayButton })
        {
            if (button.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Button native)
            {
                native.IsTabStop = false;
                native.AllowFocusOnInteraction = false;
            }
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            nativeWindow.Content.KeyDown -= OnWindowsKeyDown;
    }

    private void OnWindowsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key switch
        {
            Windows.System.VirtualKey.Left => ViewerKey.Previous,
            Windows.System.VirtualKey.Right => ViewerKey.Next,
            Windows.System.VirtualKey.Home => ViewerKey.First,
            Windows.System.VirtualKey.End => ViewerKey.Last,
            Windows.System.VirtualKey.Space => ViewerKey.ToggleLike,
            Windows.System.VirtualKey.PageUp => ViewerKey.PreviousMonth,
            Windows.System.VirtualKey.PageDown => ViewerKey.NextMonth,
            _ => (ViewerKey?)null,
        };

        if (key is { } viewerKey && HandleViewerKey(viewerKey))
            e.Handled = true;
    }

    private static async Task<string?> PickFolderOnWindowsAsync()
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return null;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
#else
    private static async Task<string?> PickFolderWithToolkitAsync()
    {
        var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
        if (!result.IsSuccessful || result.Folder is null)
            return null;

        return result.Folder.Path;
    }
#endif
}
