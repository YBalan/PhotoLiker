using PhotoLiker.Drawing.Ocr;

namespace PhotoLiker.Manuscripts.Tests
{
    public sealed class ManuscriptFileTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "ManuscriptTest_" + Guid.NewGuid().ToString("N"));

        public ManuscriptFileTests() => Directory.CreateDirectory(_folder);

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private ManuscriptSettings Sample() => new()
        {
            Kind = nameof(ScanKind.Handwriting),
            Language = nameof(ScanLanguage.Ukrainian),
            Title = "Бабусині листи",
            Author = "Оля",
            IncludePhotos = true,
            OutputFolder = Path.Combine(_folder, "out"),
            OutputFileName = "listy",
            CurrentPage = 1,
            Pages =
            [
                new ManuscriptPage
                {
                    File = Path.Combine(_folder, "page1.jpg"),
                    Rtf = @"{\rtf1\ansi Hello}",
                    Scanned = "Hello",
                    Kind = nameof(ScanKind.Handwriting),
                    Language = nameof(ScanLanguage.Ukrainian),
                    ScannedUtc = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc),
                    Lines = [StoredScanLine.From(new TextLine("Hello", 0.93f, TextRegion.FromCentre(new System.Drawing.PointF(100, 50), 80, 20, 0.05f)))],
                },
                new ManuscriptPage { File = Path.Combine(_folder, "page2.jpg"), LeftOut = true },
            ],
        };

        [Fact]
        public void TheWorkOfAFolder_ComesBackAsItWasLeft()
        {
            Assert.True(ManuscriptFile.TrySave(_folder, Sample(), out var error), error);

            var loaded = ManuscriptFile.Load(_folder)!;

            Assert.Equal("Бабусині листи", loaded.Title);
            Assert.Equal("Оля", loaded.Author);
            Assert.True(loaded.IncludePhotos);
            Assert.Equal(ScanKind.Handwriting, loaded.GetKind());
            Assert.Equal(ScanLanguage.Ukrainian, loaded.GetLanguage());
            Assert.Equal(1, loaded.CurrentPage);
            Assert.Equal(2, loaded.Pages!.Count);
            Assert.Equal(Path.Combine(_folder, "page1.jpg"), loaded.Pages[0].File);
            Assert.Equal(@"{\rtf1\ansi Hello}", loaded.Pages[0].Rtf);
            Assert.Equal("Hello", loaded.Pages[0].Scanned);
            Assert.True(loaded.Pages[0].IsScanned);
            Assert.Equal(new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc), loaded.Pages[0].ScannedUtc!.Value.ToUniversalTime());
            Assert.True(loaded.Pages[1].LeftOut);
            Assert.False(loaded.Pages[1].IsScanned);
            Assert.Equal(Path.Combine(_folder, "out"), loaded.OutputFolder);
        }

        [Fact]
        public void ThePathsInTheFile_AreRelativeToItsFolder_SoAMovedFolderStillWorks()
        {
            ManuscriptFile.TrySave(_folder, Sample(), out _);

            var text = File.ReadAllText(ManuscriptFile.GetPath(_folder));

            Assert.DoesNotContain(_folder.Replace("\\", "\\\\"), text);
            Assert.Contains("page1.jpg", text);

            // The same folder under another name finds its photos beside the file
            var moved = _folder + "_moved";
            try
            {
                Directory.Move(_folder, moved);
                var loaded = ManuscriptFile.Load(moved)!;
                Assert.Equal(Path.Combine(moved, "page1.jpg"), loaded.Pages![0].File);
            }
            finally
            {
                if (Directory.Exists(moved))
                    Directory.Move(moved, _folder);
            }
        }

        [Fact]
        public void TheLinesOfAScan_KeepTheirBoxes()
        {
            ManuscriptFile.TrySave(_folder, Sample(), out _);

            var line = ManuscriptFile.Load(_folder)!.Pages![0].Lines![0].ToLine()!;

            Assert.Equal("Hello", line.Text);
            Assert.Equal(0.93f, line.Confidence, 3);
            Assert.Equal(100f, line.Box.Centre.X, 1);
            Assert.Equal(50f, line.Box.Centre.Y, 1);
            Assert.Equal(80f, line.Box.Length, 0);
        }

        [Fact]
        public void ThePicturesOfAPage_TheOptionToFindThemWhenScanning_AndThePagesTakenOut_AreKept()
        {
            var settings = Sample();
            settings.FindPictures = true;
            settings.Pages![0].Pictures = [StoredPicture.From(new PhotoLiker.Manuscripts.Pages.PagePicture([new System.Drawing.PointF(10, 20), new System.Drawing.PointF(90, 20), new System.Drawing.PointF(50, 80)]))];
            settings.Pages[1].Pictures = []; // looked for, none found: not the same as never looked for
            settings.RemovedPages = [Path.Combine(_folder, "sub", "dup.jpg"), Path.Combine(_folder, "sub", "dup.jpg"), " "];
            ManuscriptFile.TrySave(_folder, settings, out _);

            var loaded = ManuscriptFile.Load(_folder)!;

            Assert.True(loaded.FindPictures);
            var picture = Assert.Single(loaded.Pages![0].Pictures!).ToPicture()!;
            Assert.Equal(3, picture.Outline.Length);
            Assert.Equal(new System.Drawing.PointF(50, 80), picture.Outline[2]);
            Assert.NotNull(loaded.Pages[1].Pictures);
            Assert.Empty(loaded.Pages[1].Pictures!);
            Assert.Equal([Path.Combine(_folder, "sub", "dup.jpg")], loaded.RemovedPages); // once, in full, and the blank one is gone
            Assert.DoesNotContain(_folder.Replace("\\", "\\\\"), File.ReadAllText(ManuscriptFile.GetPath(_folder))); // relative in the file
        }

        [Fact]
        public void APageThatWasNeverLookedAtForPictures_HasNullPictures_NotAnEmptyList()
        {
            ManuscriptFile.TrySave(_folder, Sample(), out _);

            var loaded = ManuscriptFile.Load(_folder)!;

            Assert.Null(loaded.Pages![0].Pictures);
            Assert.Null(loaded.FindPictures);
            Assert.Null(loaded.RemovedPages);
        }

        [Fact]
        public void ABoxThatIsNotFourCorners_IsNoLine()
        {
            Assert.Null(new StoredScanLine { Box = [1, 2, 3], Text = "x" }.ToLine());
            Assert.Null(new StoredScanLine { Box = null, Text = "x" }.ToLine());
        }

        [Fact]
        public void NoFile_NoManuscript_AndAFileThatIsNotJsonIsNone_NotACrash()
        {
            Assert.Null(ManuscriptFile.Load(_folder));
            Assert.Null(ManuscriptFile.Load(""));

            File.WriteAllText(ManuscriptFile.GetPath(_folder), "this is { not json");
            Assert.Null(ManuscriptFile.Load(_folder));
        }

        [Fact]
        public void APageWithNoPhotoName_IsDropped()
        {
            File.WriteAllText(ManuscriptFile.GetPath(_folder), """{"Pages":[{"Rtf":"x"},{"File":"a.jpg","Rtf":"y"}]}""");

            var loaded = ManuscriptFile.Load(_folder)!;

            Assert.Single(loaded.Pages!);
            Assert.EndsWith("a.jpg", loaded.Pages![0].File);
        }

        [Fact]
        public void ValuesOfAnOldOrHandMadeFile_ThatAreMissingOrWrong_LeaveTheWindowAsItIs()
        {
            File.WriteAllText(ManuscriptFile.GetPath(_folder), """{"Kind":"Calligraphy","Language":"Klingon"}""");

            var loaded = ManuscriptFile.Load(_folder)!;

            Assert.Null(loaded.GetKind());
            Assert.Null(loaded.GetLanguage());
            Assert.Null(loaded.IncludePhotos);
        }

        [Fact]
        public void ASaveIntoAFolderThatIsGone_FailsWithTheReason_AndMakesNoFolder()
        {
            var gone = Path.Combine(_folder, "nowhere");

            Assert.False(ManuscriptFile.TrySave(gone, Sample(), out var error));

            Assert.Contains("not there", error);
            Assert.False(Directory.Exists(gone));
        }

        [Fact]
        public void TheFileIsWrittenOverAnOldOne_AndNoTemporaryFileIsLeft()
        {
            ManuscriptFile.TrySave(_folder, Sample(), out _);
            var second = Sample();
            second.Title = "Second";

            Assert.True(ManuscriptFile.TrySave(_folder, second, out _));

            Assert.Equal("Second", ManuscriptFile.Load(_folder)!.Title);
            Assert.False(File.Exists(ManuscriptFile.GetPath(_folder) + ".tmp"));
        }

        [Fact]
        public void AnEmptyText_IsNotWrittenAtAll()
        {
            var settings = Sample();
            settings.Pages![0].Rtf = "   ";
            ManuscriptFile.TrySave(_folder, settings, out _);

            Assert.Null(ManuscriptFile.Load(_folder)!.Pages![0].Rtf);
        }
    }
}
