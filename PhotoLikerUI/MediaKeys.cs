namespace PhotoLikerUI
{
    /// <summary>What a media key (of a keyboard, or of a remote control such as the one of Windows Media Center) asks for, as far as a slideshow cares.</summary>
    internal enum MediaCommand
    {
        /// <summary>Not a media key: it is an ordinary key.</summary>
        None,

        /// <summary>Volume up, volume down or mute. It belongs to the system (it changes the sound of the computer), not to the slideshow, so the show takes no notice of it.</summary>
        Volume,

        /// <summary>Play: goes on if the show is paused, and does nothing if it runs.</summary>
        Play,

        /// <summary>Pause: stops the show, and does nothing if it is paused.</summary>
        Pause,

        /// <summary>One button for both: stops a show that runs and goes on with one that is paused.</summary>
        PlayPause,
    }

    /// <summary>
    /// Which media command a key, or a WM_APPCOMMAND message, stands for. A remote control reaches a program in one of two ways, and which one depends on its driver and on the key: as
    /// a key press (<c>WM_KEYDOWN</c> with the code of a media key, or the key combination that Windows Media Center gave the button: Play = Ctrl+Shift+P), or as an application command
    /// (<c>WM_APPCOMMAND</c>). Both are understood here. The codes are those of <c>winuser.h</c>; the modifier bits are those of <c>System.Windows.Forms.Keys</c>. The file has no
    /// WinForms in it so that it can be tested.
    /// </summary>
    internal static class MediaKeys
    {
        private const int KeyCodeMask = 0xFFFF;
        private const int ModifierMask = unchecked((int)0xFFFF0000);
        private const int Shift = 0x10000;
        private const int Control = 0x20000;

        private const int VkVolumeMute = 0xAD;
        private const int VkVolumeDown = 0xAE;
        private const int VkVolumeUp = 0xAF;
        private const int VkMediaPlayPause = 0xB3;
        private const int VkPlay = 0xFA;
        private const int VkP = 0x50;

        private const int AppCommandVolumeMute = 8;
        private const int AppCommandVolumeDown = 9;
        private const int AppCommandVolumeUp = 10;
        private const int AppCommandMediaPlayPause = 14;
        private const int AppCommandMediaPlay = 46;
        private const int AppCommandMediaPause = 47;

        /// <summary>The Windows message that carries an application command.</summary>
        public const int WmAppCommand = 0x0319;

        /// <param name="keyData">The key as <c>System.Windows.Forms.Keys</c> gives it: the key code, with the bits of Shift, Control and Alt on top.</param>
        public static MediaCommand FromKey(int keyData)
        {
            var code = keyData & KeyCodeMask;
            var modifiers = keyData & ModifierMask;

            switch (code)
            {
                case VkVolumeMute:
                case VkVolumeDown:
                case VkVolumeUp:
                    return MediaCommand.Volume; // with Shift or Ctrl as well: the sound is the system's whatever is held with it
                case VkMediaPlayPause when modifiers == 0:
                    return MediaCommand.PlayPause;
                case VkPlay when modifiers == 0:
                    return MediaCommand.Play;
                case VkP when modifiers == (Control | Shift): // the Play button of Windows Media Center remotes, which they send as this key combination
                    return MediaCommand.Play;
                default:
                    return MediaCommand.None;
            }
        }

        public static MediaCommand FromAppCommand(int appCommand) => appCommand switch
        {
            AppCommandVolumeMute or AppCommandVolumeDown or AppCommandVolumeUp => MediaCommand.Volume,
            AppCommandMediaPlay => MediaCommand.Play,
            AppCommandMediaPause => MediaCommand.Pause,
            AppCommandMediaPlayPause => MediaCommand.PlayPause,
            _ => MediaCommand.None,
        };

        /// <summary>The application command in the LParam of a WM_APPCOMMAND message (GET_APPCOMMAND_LPARAM: the high word, without the four bits that say which device it came from).</summary>
        public static int AppCommandOf(long lParam) => (int)((lParam >> 16) & 0x0FFF);
    }
}
