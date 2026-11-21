# Changelog

What changed in each release of PhotoLiker: one sentence per feature or fix, newest release first.

The same sentences are used in two places. The GitHub release of a version shows its own section as its notes, and the About box lists the sections
of every version newer than yours when you press **Check for updates**, so you can see what you get before you install it.

Writing it (`installer\publish-release.ps1` and the tests check this):

- While you work, add sentences under `## Unreleased`. To release version 2.1.0, rename that heading to `## 2.1.0 - 2026-11-30` (the date of the release) and commit it; the release is refused if its version has no section here.
- One sentence per line, a line that starts with `- `, beginning with a verb (Added, Fixed, Changed), ending with a full stop, at most 160 characters. Plain text only: the About box shows the line as it is, so no `**bold**`, backticks or links.
- A release that changes nothing you can see (only tests or build scripts) still gets one line saying so.

Releases before 4.0.1, and the ones since that changed only tests, are not listed; [the releases page](https://github.com/YBalan/PhotoLiker/releases) has them all.

## Unreleased

- Added Make a book: the current, liked or grouped photos become a PDF or EPUB book, with your text above and below each photo and thumbnails you drag into order.
- Added Save As that converts a photo to JPEG, PNG, BMP, GIF or TIFF, by the file type you pick.

## 4.1.12 - 2026-11-20

- Added slideshow borders next to Polaroid: a gilt Frame, a Film strip, a perforated Stamp, a neon Glow and a Shadow; the B key goes through them.

## 4.1.11 - 2026-11-19

- Added the EffectRun slideshow setting: how many photos a random effect stays for, from 1 to 100, or Random.

## 4.1.10 - 2026-11-19

- Added the slideshow effects Tilt, DollyZoom, PushUp, PushDown, CrossZoom, Wipe, Iris, Pixelate, Ripple, Cube, BlurDissolve, DipToBlack and DipToWhite.
- Added the slideshow looks Vignette, Grain, OldPhoto and Season.
- Added a Random value for the Look, Border and Collage settings of the slideshow, and the R key for it.
- Added the Gpu setting: the graphics chip draws the heavy slideshow effects instead of the processor.

## 4.0.10 - 2026-11-15

- Added File modified as a source in the date dialog, for copied archives whose modified date is the right one.

## 4.0.9 - 2026-11-15

- Fixed a video with no recorded date showing no date in the slideshow: it now shows the date of its file.

## 4.0.7 - 2026-11-15

- Fixed the slideshow not showing the date of a video it had not read yet: the video is read when its turn comes.

## 4.0.3 - 2026-11-14

- Added slideshow looks, colour grades that include your own .cube files, switched with the L key.
- Added the Portrait effect and the Refocus window, which blurs the background of a photo and saves a copy.
- Added a Ken Burns slideshow effect that follows the faces in the photo.
- Added the Polaroid border (B key) and Collage pages, which put several photos of one event on the screen.

## 4.0.2 - 2026-11-13

- Added the date a photo was taken in its bottom-right corner while the Properties panel is folded.
- Added a progress bar to Export and Import of the search index.
- Changed the Recorded date of a video to be shown the way the date of a photo is.

## 4.0.1 - 2026-11-12

- Added the Properties panel, which docks left, right or at the bottom, floats or folds away, and a toolbar rail down the side.
- Changed unnamed faces to show without a question mark.
