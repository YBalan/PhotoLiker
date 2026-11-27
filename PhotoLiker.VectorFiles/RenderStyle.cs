namespace PhotoLiker.VectorFiles
{
    /// <summary>The colours and the quality of a picture of a model. A colour is 0xRRGGBB.</summary>
    public sealed record RenderStyle
    {
        /// <summary>What is behind the model.</summary>
        public int Background { get; init; } = 0x202428;

        /// <summary>The colour of the surface where the light falls straight on it; the rest of it is darker.</summary>
        public int Surface { get; init; } = 0x8FA8C4;

        /// <summary>The colour of the edges of the triangles, when <see cref="Wireframe"/> is on.</summary>
        public int Edges { get; init; } = 0x1A1D21;

        /// <summary>The edges of the triangles are drawn over the surface (only those that can be seen, not the ones behind it). A model of more than 200,000 triangles gets the edges of some of them.</summary>
        public bool Wireframe { get; init; }

        /// <summary>Each pixel is the average of this many by this many that are drawn (1 to 4), which smooths the edges; the picture takes that much more time and memory.</summary>
        public int Supersample { get; init; } = 1;

        /// <summary>The colours for a dark window and for a light one.</summary>
        public static RenderStyle ForTheme(bool dark) => dark
            ? new RenderStyle()
            : new RenderStyle { Background = 0xEEF0F2, Surface = 0x6F8FB3, Edges = 0x2B3138 };
    }
}
