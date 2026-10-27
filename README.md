# PhotoLiker

PhotoLiker is a Windows desktop photo triage tool for quickly browsing images and marking favorites.
Open a folder, flip through it with the arrow keys, press <kbd>Space</kbd> to like a photo or a letter/number key to sort it
into a group. Likes are plain copies of the files, so nothing is hidden in a database.

And when the folder is a NAS full of old photos you can no longer scroll through, let PhotoLiker index it once in the
background and then **search it like the web**: type "birthday cake" or "children on a stage", or name a person once and
find every photo they are in. It all runs on your own computer, no photo is ever uploaded.

## Contents

- [Install](#install)
- [Happy path walkthrough](#happy-path-walkthrough)
  - [Keyboard and mouse](#keyboard-and-mouse)
  - [Toolbar](#toolbar)
- [Settings](#settings)
  - [Folder picker](#folder-picker)
  - [Changes that reload the folder](#changes-that-reload-the-folder)
  - [Order of the photos](#order-of-the-photos)
- [Groups](#groups)
- [EXIF and GPS](#exif-and-gps)
- [Dark and light theme](#dark-and-light-theme)
- [Photo search](#photo-search)
- [Settings file](#settings-file)
- [Project layout](#project-layout)
- [Development](#development)
  - [Smoke tour](#smoke-tour)
- [License](#license)

![PhotoLiker with a folder of photos open, dark theme](docs/images/01-home.png)

- **AI photo search** of a big old library, by what is in the photos ("snow", "autumn leaves") and by who is in them:
  a background indexer with a progress bar, face detection and naming, "more like this". See [Photo search](#photo-search).
- **Fast browsing** with a thumbnail strip, preloading of the neighbouring photos, and a folder scan that also works
  on network shares. The photos are listed oldest first (or newest first, or as the disk gives them) by the date their
  files were created.
- **Likes and groups**: copy the current photo to a `Liked` folder, or into `1 - Group` ... `Z - Group` folders under it.
- **Settings grid** next to the photo, with a folder picker for the liked folder. Changing which photos are listed,
  or in what order, reloads the folder at once.
- **EXIF and GPS** of the shown photo, GPS decoded to coordinates and a link to the map.
- **Dark and light theme**, remembered per folder.
- **Portable settings**: each photo folder keeps its own settings file, so the folder opens the same from any machine.

## Install

PhotoLiker needs Windows 10/11 (x64) and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

- **Installer**: build it with `installer\build-installer.ps1` (see [Development](#development)), or take `PhotoLiker-Setup` from
  the artifacts of a GitHub Actions run (see below). It installs per user, needs no administrator rights and checks for the runtime.
- **Zip**: `PhotoLikerUI.zip`, also an artifact of the GitHub Actions run.
- **GitHub Actions artifacts**: every push and pull request to `master` builds both `PhotoLikerUI` (the zip) and `PhotoLiker-Setup`
  (the installer). To get them for another branch, open *Actions > Build and Package PhotoLikerUI > Run workflow* and pick the
  branch. The artifacts are on the page of the run.
- **From source**: `dotnet run --project PhotoLikerUI -c Release -- "C:\Photos\Holiday"`. The folder argument is optional;
  without it the last folder you used is opened.

The **Register Context Menu** button of the toolbar adds *Open in Photo Liker* to the right-click menu of folders (current
user only, no administrator rights). The same button removes it again.

## Happy path walkthrough

1. Launch the app with a photo folder loaded.
2. Browse to the next image using the **Right Arrow** key.
3. Toggle like/unlike for the current image using the **Space** key.
4. Move back using the **Left Arrow** key.

| | |
|---|---|
| ![Next photo](docs/images/02-next-photo.png) | ![Liked photo](docs/images/03-liked.png) |
| **Right Arrow**: next photo. | **Space**: the photo is copied to the Liked folder. It gets a green check, in the viewer and on its thumbnail. |

![Previous photo](docs/images/04-previous-photo.png)

### Keyboard and mouse

| Input | What it does |
|---|---|
| <kbd>Right</kbd> / <kbd>Left</kbd> | Next / previous photo. Clicking a thumbnail jumps to it. |
| <kbd>Home</kbd> / <kbd>End</kbd> | The first / the last photo of the list (of the search results, while they are shown). |
| <kbd>PageDown</kbd> / <kbd>PageUp</kbd> | The first photo of the next / the previous month of the list. The list is cut into runs of photos of the same month, in the order it has (see **SortOrder**), so in a folder that is not in date order every change of month counts. A photo's month is the one of its date: the EXIF date if the folder is [indexed](#photo-search), otherwise the date its file was created. The status bar names the month. |
| <kbd>Space</kbd> | Like the photo (copy it to the Liked folder), or remove the copy again. |
| <kbd>1</kbd>-<kbd>9</kbd>, <kbd>A</kbd>-<kbd>Z</kbd> | Save the photo in a group, or take it out again (see [Groups](#groups)). |
| <kbd>0</kbd> | Fit the image to the viewer (after zooming in). |
| <kbd>Ctrl</kbd>+<kbd>C</kbd> | Copy the photo to the clipboard, as a file and as a picture: paste it into Explorer or a messenger. |
| <kbd>Ctrl</kbd>+mouse wheel | Over the photo: zoom, then drag to pan. Over the thumbnail strip: change the size of the thumbnails. |

Keys are ignored while you type into a text box: the value you are editing in a grid, the search box. A click on the
photo (or on a thumbnail, or on the empty space around the photo) takes the keyboard focus out of the text box, so the keys
work on the photo again.

### Toolbar

Open a photo folder, copy the photo, *About* (version and build date), register the folder context menu, switch the
theme, open the photo folder or the Liked folder in Explorer, load the Liked folder as a photo folder of its own, and
**Generate Video**: turn the loaded photos into a video with ffmpeg (a copy of `ffmpeg.exe` ships with the app; otherwise
the one on your `PATH` is used). You choose resolution, the time each photo is shown, codec (H.264 / H.265) and quality.

## Settings

The settings grid at the top left shows the progress, the current file and the settings of the folder. It scrolls back to
the top whenever another photo is shown:

![The settings grid, LikedFolder selected with its "..." button](docs/images/06-settings.png)

| Setting | Meaning |
|---|---|
| **LikedFolder** | Where liked photos are copied. By default the folder `Liked` inside the photo folder. Click the **...** button for a folder picker. A folder with any other name than `Liked` is kept as it is, wherever it is. |
| **GoThroughSubFolders** | Also list the photos in the subfolders. The Liked folder itself is left out. |
| **Extensions** | The file extensions that count as photos, separated by `;` or `,`, each with its dot: `.jpg; .png`. Case does not matter. |
| **SortOrder** | The order of the photos: `OldestFirst` (the default) or `NewestFirst`, by the date the file was created, or `None` for the order the disk gives (by name on most local disks). See [below](#order-of-the-photos). |
| **CacheSize** | How many photos on each side of the current one are kept decoded in memory. |
| **ThumbnailSize** | Size of the thumbnails in pixels. <kbd>Ctrl</kbd>+mouse wheel over the strip changes it. |

The rows of **Current File** and **TotalFiles** are read-only. **CurrentIndex** is the number of the shown photo, counted
from 0: type another number into it to jump to that photo (a number outside the list goes to the nearest end).

Every row has a description: select it and the pane under the grid says what it is for (drag the divider above the pane to
make it taller when a text is cut off).

### Folder picker

The **...** button of **LikedFolder** opens the Windows folder dialog, starting at the current folder. Cancelling keeps
the old value.

![The folder dialog opened from the LikedFolder row](docs/images/07-folder-picker.png)

*(The navigation pane of the dialog is blurred in this screenshot, it lists the folders of whoever took it.)*

### Changes that reload the folder

Changing **GoThroughSubFolders**, **Extensions** or **SortOrder** lists the folder again straight away: the settings are
saved first, and the photo you are looking at stays selected if it is still in the list. The other settings do not need
that: the new **LikedFolder** is used from the next like.

With **GoThroughSubFolders** on, the title of the window names the subfolder of the current photo:

![GoThroughSubFolders switched on: 8 photos instead of 5, the current one in a subfolder](docs/images/08-subfolders-on.png)

Adding `.png` to **Extensions** brings the PNG files of the folder in:

![Extensions set to .jpg; .png: 10 photos](docs/images/09-extensions.png)

### Order of the photos

**SortOrder** decides how the list is ordered, and so what <kbd>Right</kbd>, <kbd>Left</kbd>, <kbd>Home</kbd>, <kbd>End</kbd>
and <kbd>PageDown</kbd> / <kbd>PageUp</kbd> walk through:

- `OldestFirst` (the default) and `NewestFirst` go by the date each file was **created**. That date comes with the listing of
  the folder, so sorting costs nothing extra, even on a network share. (Reading the date a photo was taken from its EXIF
  data would mean opening every photo, which is what takes minutes over a network.) Photos created at the same moment are
  ordered by their path, so `NewestFirst` is exactly `OldestFirst` backwards.
- `None` does not sort: the photos come in the order the file system lists them, by name on most local disks. It is the
  quickest on a folder with very many photos.

Keep in mind that the created date is when the file appeared on that disk. A photo copied from a card or another disk
usually has the date of the copy, not of the shot, so a folder copied in one go is in the order it was copied.
A folder whose settings file has no **SortOrder** yet is listed `OldestFirst`.

## Groups

Besides liking a photo, you can sort it into groups. Press `1`-`9` or `A`-`Z` (without Ctrl/Alt/Shift) and a copy of the
current photo is saved in the folder `<key> - Group` under the Liked folder, e.g. `A - Group` or `3 - Group`. The
folders are created when they are first needed, together with the Liked folder. Pressing the same key again removes
the copy. Liking with `Space` works as before and is independent of the groups.

![A photo that is liked and in the groups 1 and A](docs/images/05-groups.png)

You can rename a group folder as long as the new name still starts with `<key> - Group`: `A - Group Family` is
used for `A` from then on. The **Current File** section of the settings grid lists every folder that holds a copy
of the current photo (**SavedInFolders**). The row shows them on one line, e.g. `[Liked]; [3 - Group]; [A - Group Family]`,
and expands into one row per folder.

A photo that is in the Liked folder or in any group gets a green check in the top-left corner of the viewer (and on its
thumbnail). The viewer lists the folders next to the check, each in brackets, e.g. `[Liked] [3 - Group] [A - Group Family]`.

The bottom-left corner of the viewer shows a legend of all group folders, under their current names, so you can see which
key saves where. The folders that hold a copy of the current photo are ticked and highlighted. A group shows up in the
legend once its folder exists, that is after the first press of its key.

`0` is not a group: it fits the image to the viewer. (It used to be `O`, which is a group like every other letter now.)

When several photos of a folder share a file name (typical with subfolders), their copies are told apart by size and
date and named `name (2).jpg`, `name (3).jpg`, ... in the Liked folder.

## EXIF and GPS

The grid below the settings shows the metadata of the shown photo in categories: Image, GPS Decoded, EXIF, Thumbnail, and
the tags PhotoLiker has no name for under Unknown. The *All Metadata* row under Debug holds everything as text. Select a row
and the pane under the grid explains the tag: what shutter speed or ISO means, what the numbers of *Flash* or *Orientation*
stand for, which date PhotoLiker takes as the date of the photo.

GPS tags are decoded into decimal and degree/minute/second coordinates, altitude, speed and the UTC time. When the photo has a
position, the **Map Link** row and the **Maps** link at the right of the status bar open it in Google Maps.

![The metadata grid with the decoded GPS data](docs/images/10-metadata-gps.png)

## Dark and light theme

The theme button of the toolbar switches between the two. The choice is stored in the settings file of the folder.

![The light theme](docs/images/11-light-theme.png)

## Photo search

For a library of thousands of old photos on a NAS, PhotoLiker can look at every photo once (**Index photos**, in the strip
above the viewer) and then answer questions about them. Nothing is uploaded: the analysis runs on this computer.

1. **Index photos** analyzes the folder and its subfolders in the background, with a progress bar and an estimate of the
   time left. You can keep browsing, stop it (the button turns into **Stop indexing**) and continue later: only new or
   changed photos are done again. The search box works as soon as the first photos are in the index.
   The first time, the AI models are downloaded (about 650 MB, you are asked first).
2. **Search** by describing what you look for: `children on a stage`, `snow`, `a dog`, `autumn leaves`. Type it into the
   search box and press <kbd>Enter</kbd> or click the magnifier icon. The results replace
   the folder in the viewer, so you browse, like and group them like any photos; <kbd>Esc</kbd> or **Close results** goes
   back. **More like this** finds photos that look like the one shown. Descriptions work in English.
3. **Dates**: put a date into the search box to look at when the photos were taken, alone or together with the rest of the
   search: `September 1` (that day in every year), `September 2009`, `1 Sep 2009`, `2009-09-01`, `2009`, `2009-2011`,
   `beach September 2009`, `Mom 2005`. A date alone lists the photos oldest first, without a limit. Month names are English;
   a short name (`sep`, `dec`) needs a day or a year with it, and a text in double quotes is searched as words only.
   The window title says how the date was understood.
4. **Faces**: switch the face icon (the smiling face in scanner brackets, last button of the strip) on to see the faces
   found in the shown photo. Click a face to say who it is (**New person...**
   or one you named before), to find all photos of that person, or to delete a wrong face (dolls, posters). Typing the name
   of a person into the search box finds their photos, best match first. A dashed yellow box with a name and a question
   mark is a guess ("looks like Mom"); click it to confirm. A face the program missed (small, turned away, dark): hold
   <kbd>Shift</kbd> and drag a box around it.

| Input | What it does |
|---|---|
| Click / right click on a face | The menu of the face: name it, find the person, delete it. |
| <kbd>Shift</kbd>+drag over the photo | Mark a face that was not found (with the face icon on). |
| <kbd>Enter</kbd> in the search box | Search. |
| <kbd>Esc</kbd> | Close the results. |

The models need the current Microsoft Visual C++ runtime (it comes with Windows updates, or as the *Visual C++
Redistributable* x64). PhotoLiker loads the one of Windows first thing when it starts: some programs (the shell extension of
TortoiseGit, for one) put an old private copy of it into every program that opens a file or folder dialog, and ONNX Runtime
does not just fail with that, it takes the whole program down (`0xC0000005` while loading the models). If an old copy is
there all the same, loading the models stops with a message that says so.

How long indexing takes depends mostly on the processor and on how fast the photos can be read: on a 6-core desktop
processor it is about three photos per second (20,000 photos: roughly two hours), a network drive can be slower. It does
not need a graphics card.

Where the date of a photo comes from: the EXIF date of the camera. If that is missing or cannot be believed, the date the file
was created is used instead (for photos copied off a camera or a card that is about the day they were copied), and the status
bar says how many of the results are dated that way. A camera date is not believed when it is before 1995 or in the future, or
when it is on January 1 before noon (an old camera that lost its clock starts again there, and writes `2001-01-01` into every
photo) and the file was not created at about that time (two days, for a time zone); a real photo of New Year's night is
kept. An index made before dates were kept gets them when you press **Update index**: that reads only the start of each file,
needs no models and takes about a minute per 2,000 photos on a network drive.

What is kept where:

- **The index** (what was found in each photo, and when it was taken) is a cache in `%LocalAppData%\PhotoLiker\index`, one file per photo folder.
  It can always be rebuilt, so deleting it only costs the time of indexing again. Your photos are only read, never changed.
  To use it on another computer (or to index on the faster one), choose **I/E > Export index...** (the first button of the
  strip: import / export), bring the `.plindex` file over, open the same photos there (whatever the drive letter) and choose
  **I/E > Import index...**.
  **Update index** then brings it up to date: photos whose size and date are the same are not analyzed again. The models
  are downloaded there on first use, or copy the `models` folder (below) along.
- **The names you give** are kept in `photo-liker-people.json` in the photo folder, next to the photos: the face examples of each
  person. It is the only new file in your folder, it is plain JSON, and a rebuilt index finds the same people again.
- **The models** are in `%LocalAppData%\PhotoLiker\models`: CLIP ViT-B/32 (OpenAI, MIT licence) to match words with photos, and
  InsightFace's SCRFD and ArcFace (`buffalo_l`) to find and recognize faces. The InsightFace models are free for
  non-commercial use only, which fits a private photo library.

## Settings file

Each photo folder keeps its settings in `photo-liker-config.json` inside the folder, so every folder remembers its own
theme, window position, extensions and the photo you stopped at. The paths in it are stored relative
to that folder (`"CurrentFolder": "."`, `"LikedFolder": "Liked"`), so the folder opens the same from any machine, drive
letter or network share. Files written by older versions, which hold full paths such as `Y:\...`, are read the same way:
the paths below the stored `CurrentFolder` are taken to be below the folder you opened, and the file is converted the next
time it is saved. A liked folder outside the photo folder, for example on another drive, keeps its full path.

```json
{
  "CurrentFolder": ".",
  "LikedFolder": "Liked",
  "GoThroughtSubFolders": true,
  "Extensions": [ ".jpg", ".png" ],
  "SortOrder": "OldestFirst",
  "CacheSize": 20,
  "ThumbnailSize": 120,
  "IsDarkTheme": true,
  "WindowLeft": 30, "WindowTop": 20, "WindowWidth": 1500, "WindowHeight": 900,
  "ScreenIndex": 0,
  "WindowState": 0,
  "CurrentFilePath": "01-beach.jpg"
}
```

`GoThroughtSubFolders` is spelled like that in the file on purpose: it is the name the first versions wrote, and folders are
shared between machines that may run different versions. The spelling `GoThroughSubFolders` (used by the MAUI app) is read
as well. `WindowState` is the number of `FormWindowState`: 0 normal, 2 maximized. `SortOrder` is written by name
(`OldestFirst`, `NewestFirst` or `None`); a name the app does not know (a typo, or one written by a newer version) reads
as `OldestFirst` and does not spoil the rest of the file.

Besides that the app keeps two small files in `%LocalAppData%\PhotoLiker`: `global-config.json` (the last folder that
was opened) and `crash.log` (the stack trace of an unexpected error). Set the environment variable `PHOTOLIKER_DATA_DIR`
to keep them somewhere else; the tests do that, so running them leaves your own files alone.

## Project layout

| Project | What it is |
|---|---|
| `PhotoLikerUI` | The Windows (WinForms) app. Everything that needs WinForms or GDI+: the window, decoding and caching of photos, the settings grid with its editors, themes, the context menu registration. |
| `PhotoLiker.Core` | The logic without a user interface: settings and their file, scanning folders, the liked and group libraries, EXIF reading and GPS decoding, the ffmpeg video service. Shared by all front ends. |
| `PhotoLiker.Search` | Photo search: model download, the indexing pipeline, CLIP text search, face detection and recognition, the SQLite index and the people file. Windows only (WPF's image decoder). |
| `PhotoLiker.Maui` | An experimental cross-platform front end on top of the Core library. It is not built by CI and needs the .NET MAUI workloads. |
| `PhotoLiker.Tests` | xUnit tests of the Core library (and of the one WinForms-free converter of the app). |
| `PhotoLiker.Search.Tests` | xUnit tests of the search library: tokenizer, image and face geometry, index, people file, search. They need no models. |
| `tools/SmokeTour` | Smoke test of the real window in dark mode that also takes the screenshots of this README, see below. |
| `installer` | Inno Setup script and `build-installer.ps1`. |
| `docs` | Screenshots and a small sample folder of photos. |

## Development

You need the .NET 10 SDK (see `global.json`) on Windows.

```powershell
dotnet build PhotoLikerUI\PhotoLikerUI.csproj          # build the app
dotnet test  PhotoLiker.Tests\PhotoLiker.Tests.csproj  # run the tests of the Core library
dotnet test  PhotoLiker.Search.Tests\PhotoLiker.Search.Tests.csproj  # run the tests of the search library
installer\build-installer.ps1                           # publish and pack the installer (needs Inno Setup 6)
```

The installer is written to `publish\installer\PhotoLiker-Setup-<version>.exe`. Inno Setup is available with
`winget install JRSoftware.InnoSetup`.

### Smoke tour

The unit tests cannot open a window, so `tools/SmokeTour` does: it generates a demo folder of photos (with EXIF and GPS
data), opens the real PhotoLiker window in **dark mode** and uses it the way a person would, checking what happens at
every step:

- the folder opens with the dark theme read from its settings file, and the window really is dark on screen;
- the arrow keys, <kbd>Home</kbd>, <kbd>End</kbd>, <kbd>PageUp</kbd>, <kbd>PageDown</kbd> (by the months of the files' dates),
  <kbd>Space</kbd> and the group keys move through the photos and copy and remove files in the Liked and group folders, a new
  photo brings the settings grid back to its top, and a number typed into **CurrentIndex** jumps to that photo;
- the strip above the viewer starts with the **I/E** button and has icons for Search and Faces;
- with real mouse clicks and key presses: after a value was typed into the settings grid, picked from a drop-down list or
  turned over by a double click, and after a click on the photo or a thumbnail while a value is still being edited, the
  arrow keys and <kbd>End</kbd> work on the photo again;
- the settings grid lists what it should, **LikedFolder** has the folder picker, and cancelling the dialog keeps the value;
- changing **GoThroughSubFolders** and **Extensions** reloads the folder with the right number of photos, and changing
  **SortOrder** lists the same photos oldest first, newest first or as the disk gives them, keeping the shown photo;
- the EXIF and GPS rows are there, the map link is offered, and every row of both grids has a description;
- the theme button switches to light and back;
- <kbd>0</kbd> fits the image to the viewer, and <kbd>O</kbd> saves into a group like any other letter;
- the settings file written on exit has relative paths, the legacy `GoThroughtSubFolders` key, the sort order and the dark theme.

```powershell
dotnet run -c Release --project tools\SmokeTour                              # the checks only
dotnet run -c Release --project tools\SmokeTour -- --screenshots docs\images # and the screenshots of this README
```

The exit code is 0 when every check passed. The window is photographed as it is on screen, and the last step clicks and
presses keys for real, so leave the mouse and the keyboard alone for the minute it takes. The demo photos go to `PhotoLikerDemo\Holiday` on the drive of the repository (change it with
`--demo-folder <path>`; `--keep` leaves it there afterwards); the tool only ever deletes a folder it made itself. Your own
settings and last folder are not touched.

## License

MIT, see [LICENSE.txt](LICENSE.txt).
