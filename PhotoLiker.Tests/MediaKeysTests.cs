using PhotoLikerUI;

namespace PhotoLiker.Tests
{
    public class MediaKeysTests
    {
        // Numbers as System.Windows.Forms.Keys has them: VolumeMute 173, VolumeDown 174, VolumeUp 175, MediaPlayPause 179, Play 250, P 80; Shift 0x10000, Control 0x20000, Alt 0x40000.

        [Theory]
        [InlineData(173)]
        [InlineData(174)]
        [InlineData(175)]
        [InlineData(175 | 0x10000)] // the volume is the system's with Shift held as well
        [InlineData(174 | 0x20000)]
        public void VolumeKeysAreVolume(int keyData) => Assert.Equal(MediaCommand.Volume, MediaKeys.FromKey(keyData));

        [Fact]
        public void PlayKeyGoesOn() => Assert.Equal(MediaCommand.Play, MediaKeys.FromKey(250));

        [Fact]
        public void PlayPauseKeyIsTheOneButton() => Assert.Equal(MediaCommand.PlayPause, MediaKeys.FromKey(179));

        [Fact]
        public void WindowsMediaCenterPlayIsCtrlShiftP() => Assert.Equal(MediaCommand.Play, MediaKeys.FromKey(0x50 | 0x20000 | 0x10000));

        [Theory]
        [InlineData(0x50)]                  // P
        [InlineData(0x50 | 0x20000)]        // Ctrl+P is Pause of Media Center: an ordinary key here, which pauses like every key
        [InlineData(0x50 | 0x10000)]        // Shift+P
        [InlineData(0x50 | 0x20000 | 0x10000 | 0x40000)] // with Alt as well
        [InlineData(0x20)]                  // Space
        [InlineData(0x27)]                  // Right
        [InlineData(0x79)]                  // F10
        [InlineData(179 | 0x20000)]         // Ctrl+Play/Pause
        [InlineData(250 | 0x10000)]         // Shift+Play
        public void OtherKeysAreNotMediaKeys(int keyData) => Assert.Equal(MediaCommand.None, MediaKeys.FromKey(keyData));

        [Theory]
        [InlineData(8, "Volume")]   // APPCOMMAND_VOLUME_MUTE
        [InlineData(9, "Volume")]   // APPCOMMAND_VOLUME_DOWN
        [InlineData(10, "Volume")]  // APPCOMMAND_VOLUME_UP
        [InlineData(14, "PlayPause")]
        [InlineData(46, "Play")]
        [InlineData(47, "Pause")]
        [InlineData(11, "None")]    // next track: not the show's
        [InlineData(13, "None")]    // stop
        [InlineData(0, "None")]
        public void AppCommands(int appCommand, string expected) => Assert.Equal(Enum.Parse<MediaCommand>(expected), MediaKeys.FromAppCommand(appCommand));

        [Theory]
        [InlineData(46, 0x0000)]  // the device bits (the high four of the high word) are not part of the command: 0x0000 = none, 0x8000 = mouse, 0x1000 = key, 0x2000 = OEM
        [InlineData(46, 0x1000)]
        [InlineData(46, 0x2000)]
        [InlineData(10, 0x8000)]
        public void TheCommandIsTheHighWordWithoutTheDeviceBits(int command, int device)
        {
            // LPARAM = (command | device) << 16 | keys held (low word)
            long lParam = (((long)(command | device)) << 16) | 0x0004;
            Assert.Equal(command, MediaKeys.AppCommandOf(lParam));
        }

        [Fact]
        public void TheCommandOfAnLParamThatIsNegativeAsAnIntIsStillRight()
        {
            // The high word of 0x802E0000 is 0x802E (device 0x8000, command 46); as a 32-bit LPARAM it is negative, and a 64-bit one that was sign-extended has ones all the way up
            Assert.Equal(46, MediaKeys.AppCommandOf(unchecked((int)0x802E0000)));
            Assert.Equal(46, MediaKeys.AppCommandOf(0x802E0000L));
        }
    }
}
