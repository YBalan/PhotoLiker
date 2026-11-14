using System.Reflection;

namespace PhotoLikerUI
{
    /// <summary>
    /// A tip for the rows of a PropertyGrid that need one. The grid has none of its own for a row (it only shows the value in full when the cell is too narrow for it), but there are values that are shown short on
    /// purpose, such as the name of a file where the setting is its path: the tip of the row says the whole of it. The row under the mouse is found in the grid's own list of rows (<c>PropertyGridView</c>, which is
    /// not part of the public interface: when it is not as expected there is no tip, and nothing else happens), and the text of one tip of the grid is changed as the mouse goes from row to row, so that the usual
    /// delay and place of a tip apply.
    /// </summary>
    internal sealed class GridValueTips : IDisposable
    {
        private const int ValuePart = 2;   // the part of a row GetRectangle is asked for: the value, which is as high as the row

        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly PropertyGrid _grid;
        private readonly Func<GridItem, string?> _tipOf;
        private readonly Control? _view;
        private readonly ToolTip _tip = new() { InitialDelay = 500, AutoPopDelay = 20_000, ShowAlways = true };

        /// <param name="tipOf">The tip of a row, or null for a row that has none.</param>
        public GridValueTips(PropertyGrid grid, Func<GridItem, string?> tipOf)
        {
            _grid = grid;
            _tipOf = tipOf;
            _view = grid.Controls.Cast<Control>().FirstOrDefault(control => control.GetType().Name == "PropertyGridView");
            if (_view is null)
                return;

            _view.MouseMove += (_, e) => Update(e.Location);
            _view.MouseLeave += (_, _) => Set(null);
        }

        /// <summary>The tip that is set now (empty for none).</summary>
        public string Text => _view is null ? string.Empty : _tip.GetToolTip(_view) ?? string.Empty;

        /// <summary>The mouse is at this place of the list of rows (the coordinates of the list, not of the grid): the tip is that of the row it is over.</summary>
        public void Update(Point at) => Set(ItemAt(at) is { } item ? _tipOf(item) : null);

        private void Set(string? text)
        {
            if (_view is null)
                return;

            var value = text ?? string.Empty;
            if (_tip.GetToolTip(_view) != value)
                _tip.SetToolTip(_view, value);
        }

        /// <summary>The row of the grid at a place of its list of rows; null when there is none there, or the grid is not made as this expects.</summary>
        public GridItem? ItemAt(Point at)
        {
            if (_view is null)
                return null;

            try
            {
                var type = _view.GetType();
                var rectangleOf = type.GetMethods(Flags).FirstOrDefault(method => method.Name == "GetRectangle" && method.GetParameters().Length == 2);
                var rowAt = type.GetMethod("GetGridEntryFromRow", Flags);
                if (rectangleOf is null || rowAt is null)
                    return null;

                // The rows that are on the list, from the top: as many as fit in it, and one for a row that is cut at the bottom
                var rows = _view.Height / Math.Max(1, _grid.Font.Height) + 2;
                for (var row = 0; row < rows; row++)
                {
                    var rectangle = (Rectangle)rectangleOf.Invoke(_view, [row, ValuePart])!;
                    if (rectangle.Height > 0 && at.Y >= rectangle.Top && at.Y < rectangle.Bottom)
                        return rowAt.Invoke(_view, [row]) as GridItem;
                }
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidCastException or ArgumentException or MethodAccessException)
            {
                // the grid is not as this expects: no tip
            }
            return null;
        }

        /// <summary>The rectangle (in the coordinates of the grid) of the value of a row, as the grid draws it; null when it can not be had. For a test to point at a row.</summary>
        public Rectangle? ValueCellOf(GridItem item)
        {
            if (_view is null)
                return null;

            try
            {
                var type = _view.GetType();
                var rowOf = type.GetMethod("GetRowFromGridEntry", Flags);
                var rectangleOf = type.GetMethods(Flags).FirstOrDefault(method => method.Name == "GetRectangle" && method.GetParameters().Length == 2);
                if (rowOf is null || rectangleOf is null)
                    return null;

                var row = (int)rowOf.Invoke(_view, [item])!;
                return row < 0 ? null : (Rectangle)rectangleOf.Invoke(_view, [row, ValuePart])!;
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidCastException or ArgumentException or MethodAccessException)
            {
                return null;
            }
        }

        public void Dispose() => _tip.Dispose();
    }
}
