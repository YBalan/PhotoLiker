namespace PhotoLiker.VectorFiles
{
    /// <summary>
    /// Where the camera is: it goes round the middle of the model and looks at it, as the camera of a CAD program does. The model stands with its Z axis up, as a CAD program writes it.
    /// <see cref="Yaw"/> and <see cref="Pitch"/> are degrees: yaw 0, pitch 0 is the front (looking along +Y, X to the right, Z up); a positive yaw takes the camera to the right
    /// of the model, a positive pitch above it (pitch 90 is looking straight down, X to the right, Y up). <see cref="Zoom"/> is 1 when the whole model just fits the picture.
    /// <see cref="PanX"/> and <see cref="PanY"/> move the model in the picture, as fractions of its shorter side (positive: right, up), so they mean the same at any size.
    /// </summary>
    public readonly record struct MeshView(double Yaw, double Pitch, double Zoom = 1, double PanX = 0, double PanY = 0)
    {
        public const double MinZoom = 0.1;
        public const double MaxZoom = 40;

        /// <summary>From the front right, a little above: the view a model is first shown in.</summary>
        public static MeshView Isometric => new(35, 28);

        public static MeshView Front => new(0, 0);
        public static MeshView Back => new(180, 0);
        public static MeshView Left => new(-90, 0);
        public static MeshView Right => new(90, 0);
        public static MeshView Top => new(0, 90);
        public static MeshView Bottom => new(0, -90);

        /// <summary>The camera moved round the model: a drag of the mouse to the right turns the model to the right (the camera goes left), a drag down tilts its top towards the viewer.</summary>
        public MeshView Orbited(double degreesRight, double degreesDown) =>
            this with { Yaw = NormalizeYaw(Yaw - degreesRight), Pitch = Math.Clamp(Pitch + degreesDown, -90, 90) };

        /// <summary>Nearer (more than 1) or farther (less than 1), by that factor, within <see cref="MinZoom"/> and <see cref="MaxZoom"/>.</summary>
        public MeshView Zoomed(double factor) => this with { Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom) };

        /// <summary>The model moved in the picture, by fractions of the shorter side of it (see <see cref="PanX"/>).</summary>
        public MeshView Panned(double right, double up) => this with { PanX = PanX + right, PanY = PanY + up };

        /// <summary>The same place of the camera with the model back in the middle at the size that fits.</summary>
        public MeshView Centered => this with { Zoom = 1, PanX = 0, PanY = 0 };

        /// <summary>The camera's three directions written in the model's axes: where the right of the picture, the top of it and the viewer are.</summary>
        public CameraBasis Basis
        {
            get
            {
                var yaw = Yaw * Math.PI / 180;
                var pitch = Pitch * Math.PI / 180;
                double sinYaw = Math.Sin(yaw), cosYaw = Math.Cos(yaw), sinPitch = Math.Sin(pitch), cosPitch = Math.Cos(pitch);
                return new CameraBasis(
                    RightX: cosYaw, RightY: sinYaw, RightZ: 0,
                    UpX: -sinPitch * sinYaw, UpY: sinPitch * cosYaw, UpZ: cosPitch,
                    ToX: cosPitch * sinYaw, ToY: -cosPitch * cosYaw, ToZ: sinPitch);
            }
        }

        private static double NormalizeYaw(double yaw)
        {
            var wrapped = yaw % 360;
            if (wrapped > 180) wrapped -= 360;
            if (wrapped <= -180) wrapped += 360;
            return wrapped;
        }
    }

    /// <summary>
    /// The directions of a camera, each as a unit vector in the axes of the model: <c>Right</c> is where the right of the picture points, <c>Up</c> the top of it, and <c>To</c> the way
    /// to the viewer (a point that is nearer has a greater number along it). Used to turn a point of the model into a place in the picture: right = dot(point, Right), up = dot(point, Up).
    /// </summary>
    public readonly record struct CameraBasis(double RightX, double RightY, double RightZ, double UpX, double UpY, double UpZ, double ToX, double ToY, double ToZ)
    {
        /// <summary>A point as the picture sees it: how far to the right of the middle, how far above it, and how far towards the viewer.</summary>
        public (double Right, double Up, double To) Project(double x, double y, double z) =>
            (x * RightX + y * RightY + z * RightZ, x * UpX + y * UpY + z * UpZ, x * ToX + y * ToY + z * ToZ);
    }
}
