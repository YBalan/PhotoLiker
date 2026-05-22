using System.Collections.Concurrent;
using CommunityToolkit.Maui.Storage;
using PhotoLiker.Core;

namespace PhotoLiker.Maui;

public partial class MainPage : ContentPage
{
    private const int PreviewSiblingCount = 6;

    private readonly PhotoBrowserService _service;
    private readonly List<string> _metadataLines = [];
    private readonly ConcurrentDictionary<string, byte[]> _imageCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _cacheLoadTasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Image> _visibleThumbnailImages = new(StringComparer.Ordinal);

    private CancellationTokenSource _cacheCts = new();
    private CancellationTokenSource _thumbnailCts = new();
    private string? _currentMapLink;
    private string? _currentMainImagePath;

    public MainPage()
    {
        InitializeComponent();
        _service = new PhotoBrowserService();

        _service.StatusChanged += OnStatusChanged;
        _service.ImageLoaded += OnImageLoaded;
        _service.FolderLoaded += OnFolderLoaded;

        MetadataCollection.ItemsSource = _metadataLines;
        PermissionHelpLabel.Text = GetPermissionHelpText();

#if WINDOWS
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _service.RestoreLastSession();
#endif

        UpdateNavigationButtons();
    }

    private async void OnOpenFolderClicked(object? sender, EventArgs e)
    {
        try
        {
            var folder = await PickFolderAsync();
            if (!string.IsNullOrWhiteSpace(folder))
            {
                ResetImageState();
                _service.OpenFolder(folder);
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
        UpdateLikeButton();
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

    private void OnStatusChanged(string message)
    {
        MainThread.BeginInvokeOnMainThread(() => CurrentFileLabel.Text = message);
    }

    private void OnFolderLoaded()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RenderThumbnails();
            UpdateNavigationButtons();
        });
    }

    private async void OnImageLoaded(PhotoFile? photoFile, MetadataViewModel? metadata)
    {
        if (photoFile is null)
            return;

        var filePath = photoFile.OriginalFilePath;
        _currentMainImagePath = filePath;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            CurrentFileLabel.Text = Path.GetFileName(filePath);

            if (_imageCache.TryGetValue(filePath, out var cachedBytes))
                MainImage.Source = CreateImageSource(cachedBytes);
            else
                MainImage.Source = null;

            RenderThumbnails();
            RenderMetadata(metadata);
            UpdateLikeButton();
            UpdateNavigationButtons();
        });

        _ = LoadMainImageAsync(filePath);
        StartCacheWarmup(filePath);
    }

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
        CancelThumbnailLoads();
        _visibleThumbnailImages.Clear();
        ThumbnailContainer.Children.Clear();

        var files = _service.Settings.Files;
        if (files.Count == 0)
            return;

        var currentIndex = _service.Settings.CurrentIndex;
        if (currentIndex < 0)
            currentIndex = 0;

        var from = Math.Max(0, currentIndex - PreviewSiblingCount);
        var to = Math.Min(files.Count - 1, currentIndex + PreviewSiblingCount);
        var token = _thumbnailCts.Token;

        for (var i = from; i <= to; i++)
        {
            var filePath = files[i].OriginalFilePath;
            var image = new Image
            {
                WidthRequest = 72,
                HeightRequest = 72,
                Aspect = Aspect.AspectFill
            };

            if (_imageCache.TryGetValue(filePath, out var cachedBytes))
                image.Source = CreateImageSource(cachedBytes);

            var frame = new Border
            {
                Stroke = i == currentIndex ? Colors.DeepSkyBlue : Colors.Gray,
                StrokeThickness = i == currentIndex ? 2 : 1,
                Padding = 1,
                WidthRequest = 74,
                HeightRequest = 74,
                Content = image
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _service.LoadImage(filePath);
            frame.GestureRecognizers.Add(tap);

            _visibleThumbnailImages[filePath] = image;
            ThumbnailContainer.Children.Add(frame);

            if (image.Source is null)
                _ = LoadThumbnailAsync(filePath, token);
        }
    }

    private void UpdateLikeButton()
    {
        LikeButton.Text = _service.IsCurrentLiked ? "Unlike" : "Like";
    }

    private void UpdateNavigationButtons()
    {
        var files = _service.Settings.Files;
        var currentIndex = _service.Settings.CurrentIndex;
        var hasFiles = files.Count > 0;

        PreviousOverlayButton.IsVisible = hasFiles && currentIndex > 0;
        NextOverlayButton.IsVisible = hasFiles && currentIndex >= 0 && currentIndex < files.Count - 1;
    }

    private void ResetImageState()
    {
        _currentMainImagePath = null;
        MainImage.Source = null;
        CancelCacheWarmup();
        CancelThumbnailLoads();
        _imageCache.Clear();
        _cacheLoadTasks.Clear();
        _visibleThumbnailImages.Clear();
        ThumbnailContainer.Children.Clear();
    }

    private void StartCacheWarmup(string currentFile)
    {
        CancelCacheWarmup();
        var token = _cacheCts.Token;
        _ = Task.Run(() => WarmVisibleImageCacheAsync(currentFile, token), token);
    }

    private void CancelCacheWarmup()
    {
        _cacheCts.Cancel();
        _cacheCts.Dispose();
        _cacheCts = new CancellationTokenSource();
    }

    private void CancelThumbnailLoads()
    {
        _thumbnailCts.Cancel();
        _thumbnailCts.Dispose();
        _thumbnailCts = new CancellationTokenSource();
    }

    private async Task WarmVisibleImageCacheAsync(string currentFile, CancellationToken cancellationToken)
    {
        var files = _service.Settings.Files;
        if (files.Count == 0)
            return;

        var currentIndex = _service.Settings.CurrentIndex;
        if (currentIndex < 0)
            currentIndex = files.FindIndex(f => string.Equals(f.OriginalFilePath, currentFile, StringComparison.Ordinal));
        if (currentIndex < 0)
            return;

        var from = Math.Max(0, currentIndex - PreviewSiblingCount);
        var to = Math.Min(files.Count - 1, currentIndex + PreviewSiblingCount);
        var keepPaths = new HashSet<string>(StringComparer.Ordinal);

        for (var i = from; i <= to; i++)
            keepPaths.Add(files[i].OriginalFilePath);

        keepPaths.Add(currentFile);

        foreach (var cachedPath in _imageCache.Keys)
        {
            if (!keepPaths.Contains(cachedPath))
                _imageCache.TryRemove(cachedPath, out _);
        }

        for (var i = from; i <= to; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var filePath = files[i].OriginalFilePath;
            await GetOrLoadImageBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LoadMainImageAsync(string filePath)
    {
        var bytes = await GetOrLoadImageBytesAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        if (bytes is null)
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!string.Equals(_currentMainImagePath, filePath, StringComparison.Ordinal))
                return;

            MainImage.Source = CreateImageSource(bytes);
        });
    }

    private async Task LoadThumbnailAsync(string filePath, CancellationToken cancellationToken)
    {
        var bytes = await GetOrLoadImageBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (bytes is null || cancellationToken.IsCancellationRequested)
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!_visibleThumbnailImages.TryGetValue(filePath, out var image))
                return;

            image.Source = CreateImageSource(bytes);
        });
    }

    private async Task<byte[]?> GetOrLoadImageBytesAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_imageCache.TryGetValue(filePath, out var cachedBytes))
            return cachedBytes;

        var loadTask = _cacheLoadTasks.GetOrAdd(filePath, path => LoadImageBytesCoreAsync(path, cancellationToken));

        try
        {
            var bytes = await loadTask.ConfigureAwait(false);
            if (bytes is not null)
                _imageCache[filePath] = bytes;

            return bytes;
        }
        finally
        {
            _cacheLoadTasks.TryRemove(filePath, out _);
        }
    }

    private static async Task<byte[]?> LoadImageBytesCoreAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource CreateImageSource(byte[] bytes)
    {
        return ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
    }

    private static string GetPermissionHelpText()
    {
#if ANDROID
        return "Android: choose a folder and grant access when prompted. If access is denied, reopen the picker and allow photo folder access.";
#elif IOS || MACCATALYST
        return "Apple platforms: choose a folder and allow PhotoLiker to access it when the system prompt appears. Some folders may remain restricted by the OS.";
#elif WINDOWS
        return "Windows: choose any local photo folder. Use the keyboard Left and Right arrow keys to move between photos.";
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
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            nativeWindow.Content.KeyDown -= OnWindowsKeyDown;
    }

    private void OnWindowsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Left)
        {
            _service.GoPrevious();
            UpdateNavigationButtons();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Right)
        {
            _service.GoNext();
            UpdateNavigationButtons();
            e.Handled = true;
        }
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
