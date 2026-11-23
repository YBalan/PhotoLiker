namespace PhotoLiker.Drawing
{
    /// <summary>
    /// A photo being drawn on: the steps taken so far (strokes, notes, crops), which can be taken back one by one and brought back. The photo is never changed; the picture the person sees is the
    /// photo with the steps replayed over it, so Undo costs the drawing of the steps that stay, not a copy of the picture for each one that was taken.
    /// </summary>
    public sealed class DrawingSession
    {
        private readonly Bitmap32 _original;
        private readonly List<DrawStep> _steps = [];
        private readonly Stack<DrawStep> _undone = new();
        private Bitmap32 _picture;
        private bool _pictureIsOriginal = true;

        public DrawingSession(Bitmap32 photo)
        {
            ArgumentNullException.ThrowIfNull(photo);
            _original = photo;
            _picture = photo;
        }

        /// <summary>The picture as it is now: the photo with the steps on it. Do not draw on it; take a step.</summary>
        public Bitmap32 Picture => _picture;

        public int Width => _picture.Width;

        public int Height => _picture.Height;

        /// <summary>The steps that are in effect, the first one first.</summary>
        public IReadOnlyList<DrawStep> Steps => _steps;

        public bool CanUndo => _steps.Count > 0;

        public bool CanRedo => _undone.Count > 0;

        /// <summary>Whether the picture differs from the photo (any step is in effect).</summary>
        public bool IsChanged => _steps.Count > 0;

        /// <summary>Takes a step: draws it on the picture and forgets the steps that were taken back (a new step ends the way back to them).</summary>
        /// <exception cref="ArgumentException">A crop that has nothing of the picture in it.</exception>
        public void Add(DrawStep step)
        {
            ArgumentNullException.ThrowIfNull(step);

            // The photo itself is not drawn on: the first step makes the picture a copy of it
            var next = _pictureIsOriginal && step is not (CropStep or RotateStep) ? _picture.Clone() : _picture; // a crop and a turn make a picture of their own
            var result = Painter.Apply(next, step);

            _picture = result;
            _pictureIsOriginal = false;
            _steps.Add(step);
            _undone.Clear();
        }

        /// <summary>Takes the last step back. False when there is none.</summary>
        public bool Undo()
        {
            if (_steps.Count == 0)
                return false;

            _undone.Push(_steps[^1]);
            _steps.RemoveAt(_steps.Count - 1);
            Replay();
            return true;
        }

        /// <summary>Brings back the step that was taken back last. False when there is none.</summary>
        public bool Redo()
        {
            if (_undone.Count == 0)
                return false;

            var step = _undone.Pop();
            var next = _pictureIsOriginal && step is not (CropStep or RotateStep) ? _picture.Clone() : _picture;
            _picture = Painter.Apply(next, step);
            _pictureIsOriginal = false;
            _steps.Add(step);
            return true;
        }

        /// <summary>Takes back everything: the picture is the photo again. The steps can be brought back one by one with Redo.</summary>
        public void UndoAll()
        {
            while (Undo())
            {
            }
        }

        private void Replay()
        {
            if (_steps.Count == 0)
            {
                _picture = _original;
                _pictureIsOriginal = true;
                return;
            }

            var picture = _original.Clone();
            foreach (var step in _steps)
                picture = Painter.Apply(picture, step);
            _picture = picture;
            _pictureIsOriginal = false;
        }
    }
}
