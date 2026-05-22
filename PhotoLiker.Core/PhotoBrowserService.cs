namespace PhotoLiker.Core
{
    /// <summary>
    /// Platform-agnostic photo browsing and liking logic.
    /// UI projects wire up the events and call the public methods.
    /// </summary>
    public class PhotoBrowserService
    {
        private Settings _settings = new();
        private readonly GlobalConfig _globalConfig = GlobalConfig.Load();

        public Settings Settings => _settings;
        public GlobalConfig GlobalConfig => _globalConfig;

        // ── events ─────────────────────────────────────────────────────────
        public event Action<string>? StatusChanged;
        public event Action<PhotoFile?, MetadataViewModel?>? ImageLoaded;
        public event Action? FolderLoaded;

        // ── folder ─────────────────────────────────────────────────────────

        public void OpenFolder(string folder)
        {
            _settings.Save();
            _settings = Settings.Load(folder);
            _settings.CurrentFolder = folder;

            if (string.IsNullOrWhiteSpace(_settings.LikedFolder) ||
                string.Equals(Path.GetFileName(_settings.LikedFolder), AppStrings.DefaultLikedFolderName, StringComparison.OrdinalIgnoreCase))
                _settings.LikedFolder = Path.Combine(folder, AppStrings.DefaultLikedFolderName);

            _settings.Files = GetAllFiles(folder, _settings.GoThroughSubFolders)
                .Select(f => new PhotoFile(f, string.Empty))
                .ToList();

            _globalConfig.LastFolder = folder;
            _globalConfig.Save();

            StatusChanged?.Invoke(string.Format(AppStrings.StatusFolderLoaded, _settings.Files.Count, folder));
            FolderLoaded?.Invoke();

            LoadFirstPhoto(_settings.CurrentFilePath);
        }

        public void RestoreLastSession()
        {
            var lastFolder = _globalConfig.LastFolder;
            if (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
                OpenFolder(lastFolder);
        }

        // ── navigation ─────────────────────────────────────────────────────

        public void LoadImage(string filePath)
        {
            try
            {
                _settings.CurrentFilePath = filePath;
                _settings.CurrentFileName = Path.GetFileName(filePath);
                _settings.CurrentIndex    = _settings.Files.IndexOf(filePath);

                if (_settings.CurrentIndex >= 0)
                {
                    var pf = _settings.Files[_settings.CurrentIndex];
                    var suggested = Path.Combine(_settings.LikedFolder, Path.GetFileName(pf.OriginalFilePath));
                    _settings.LikedFile = File.Exists(pf.LikedFilePath)
                        ? pf.LikedFilePath
                        : File.Exists(suggested) ? suggested : string.Empty;
                }

                MetadataViewModel? meta = null;
                try { meta = new MetadataViewModel(new FriendlyImageMetadata(filePath)); }
                catch { /* metadata optional */ }

                var photoFile = _settings.CurrentIndex >= 0 ? _settings.Files[_settings.CurrentIndex] : null;
                ImageLoaded?.Invoke(photoFile, meta);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(string.Format(AppStrings.StatusImageLoadError, ex.Message));
            }
        }

        public void GoNext()
        {
            var idx = _settings.CurrentIndex;
            if (idx < _settings.Files.Count - 1)
                LoadImage(_settings.Files[idx + 1].OriginalFilePath);
        }

        public void GoPrevious()
        {
            var idx = _settings.CurrentIndex;
            if (idx > 0)
                LoadImage(_settings.Files[idx - 1].OriginalFilePath);
        }

        // ── like / unlike ──────────────────────────────────────────────────

        public bool IsCurrentLiked =>
            !string.IsNullOrEmpty(_settings.LikedFile) && File.Exists(_settings.LikedFile);

        public void ToggleLike()
        {
            if (string.IsNullOrEmpty(_settings.CurrentFilePath)) return;
            if (IsCurrentLiked)
                UnlikePhoto(_settings.CurrentFilePath);
            else
                LikePhoto(_settings.CurrentFilePath);
        }

        public void LikePhoto(string filePath)
        {
            try
            {
                var likedFolder = _settings.LikedFolder;
                if (string.IsNullOrWhiteSpace(likedFolder))
                    return; // caller must set LikedFolder first

                if (!Directory.Exists(likedFolder))
                    Directory.CreateDirectory(likedFolder);

                var fileName = Path.GetFileName(filePath);
                var dest     = Path.Combine(likedFolder, fileName);

                if (File.Exists(dest))
                {
                    // Already exists → treat as unlike
                    UnlikePhoto(filePath);
                    return;
                }

                File.Copy(filePath, dest, overwrite: true);

                var idx = _settings.Files.IndexOf(filePath);
                if (idx >= 0) _settings.Files[idx].LikedFilePath = dest;

                _settings.LikedFile = dest;
                StatusChanged?.Invoke(string.Format(AppStrings.StatusFileCopied, fileName));

                // Notify UI to refresh like indicator
                ImageLoaded?.Invoke(idx >= 0 ? _settings.Files[idx] : null, null);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(string.Format(AppStrings.StatusLikeError, ex.Message));
            }
        }

        public void UnlikePhoto(string filePath)
        {
            try
            {
                var likedPath = _settings.LikedFile;
                if (File.Exists(likedPath))
                    File.Delete(likedPath);
                StatusChanged?.Invoke(string.Format(AppStrings.StatusFileRemoved, Path.GetFileName(likedPath)));

                var idx = _settings.Files.IndexOf(filePath);
                if (idx >= 0) _settings.Files[idx].LikedFilePath = string.Empty;

                _settings.LikedFile = string.Empty;
                ImageLoaded?.Invoke(idx >= 0 ? _settings.Files[idx] : null, null);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(string.Format(AppStrings.StatusUnlikeError, ex.Message));
            }
        }

        // ── helpers ────────────────────────────────────────────────────────

        private void LoadFirstPhoto(string currentFile)
        {
            var file = _settings.Files
                .FirstOrDefault(f => string.Equals(f.OriginalFilePath, currentFile, StringComparison.Ordinal))
                ?? _settings.Files.FirstOrDefault();

            if (file is not null)
                LoadImage(file.OriginalFilePath);
            else
                StatusChanged?.Invoke(AppStrings.StatusNoPhotosFound);
        }

        private IEnumerable<string> GetAllFiles(string folder, bool recursive)
        {
            return new DirectoryInfo(folder)
                .GetFiles("*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(fi => _settings.Extensions.Contains(fi.Extension.ToLowerInvariant()))
                .Select(fi => fi.FullName);
        }
    }
}
