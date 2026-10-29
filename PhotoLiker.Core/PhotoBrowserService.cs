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
            _settings.ApplyDefaultLikedFolder();

            _settings.Files = PhotoScanner.Scan(folder, _settings.GoThroughSubFolders, _settings.Extensions, _settings.LikedFolder, _settings.SortOrder);

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
                    _settings.LikedFile = File.Exists(pf.LikedFilePath)
                        ? pf.LikedFilePath
                        : FindLikedCopy(pf) ?? string.Empty;
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

                var photo = Describe(filePath) ?? throw new FileNotFoundException(null, filePath);
                var fileName = Path.GetFileName(filePath);

                var library = LikedLibrary.Scan(likedFolder);
                if (library.Find(photo) is not null)
                {
                    // Already exists → treat as unlike
                    UnlikePhoto(filePath);
                    return;
                }

                var dest = library.GetNewCopyPath(photo);
                LikedLibrary.CopyPhoto(filePath, dest);

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
                // The copy of this photo, not whatever the current photo's copy is
                var likedPath = Describe(filePath) is { } photo ? FindLikedCopy(photo) : null;
                if (likedPath is not null && File.Exists(likedPath))
                    File.Delete(likedPath);
                StatusChanged?.Invoke(string.Format(AppStrings.StatusFileRemoved, Path.GetFileName(likedPath ?? filePath)));

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

        /// <summary>The photo from the list, or described by the file system if it isn't in the list; null if the file is gone.</summary>
        private PhotoFile? Describe(string filePath)
        {
            var idx = _settings.Files.IndexOf(filePath);
            if (idx >= 0)
                return _settings.Files[idx];

            var info = new FileInfo(filePath);
            return info.Exists
                ? new PhotoFile(filePath, string.Empty)
                {
                    Length = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    CreationTimeUtc = info.CreationTimeUtc,
                }
                : null;
        }

        // A fresh listing each time: the folder is also changed from outside while the photos are browsed
        private string? FindLikedCopy(PhotoFile photo) =>
            string.IsNullOrWhiteSpace(_settings.LikedFolder) ? null : LikedLibrary.Scan(_settings.LikedFolder).Find(photo);

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
    }
}
