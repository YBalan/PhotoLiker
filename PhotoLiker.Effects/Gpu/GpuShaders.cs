namespace PhotoLiker.Effects.Gpu
{
    /// <summary>
    /// The shaders (HLSL, compiled when the GPU renderer starts: 15 to 40 ms) of <see cref="GpuRenderer"/>. They are the same arithmetic as <see cref="Compositing.FrameCompositor"/> and
    /// <see cref="Transitions.TransitionMixer"/>, written for the pixels of a screen: a pixel here is the pixel of the screen the pixel shader runs for (<c>SV_Position</c>, whose middle is at .5), and a
    /// picture is a texture of 0xAARRGGBB pixels (B8G8R8A8, the order they have in memory). The tests put the results of the two side by side.
    /// </summary>
    internal static class GpuShaders
    {
        /// <summary>A triangle that covers the whole target: no vertex buffer, the corners come from the number of the vertex.</summary>
        private const string Vertex = @"
struct V { float4 pos : SV_Position; };

V VS(uint id : SV_VertexID)
{
    float2 uv = float2((id << 1) & 2, id & 2);
    V o;
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}
";

        /// <summary>One layer of a composition (see <see cref="Compositing.CompositeLayer"/>), added to what the target has (the blend adds, the target is cleared to black first).</summary>
        public const string Layer = Vertex + @"
cbuffer LayerParams : register(b0)
{
    float4 SizeA;    // xy: size of the target, zw: size of the picture
    float4 PlaceA;   // xy: where the middle of the picture is on the screen, z: 1 / magnification, w: how much of it shows (0 to 1)
    float4 TurnA;    // x: cosine of the turn, y: sine, z: mode (0 as it is, 1 magnified, 2 moved by its depth, 3 turned)
    float4 OffsetA;  // xy: the place of the picture's first pixel on the screen (whole pixels: modes 0 and 2)
    float4 DeepA;    // x: shift sideways, y: shift down, z: the depth that stays, w: shift along the line from the middle
};

Texture2D Picture : register(t0);
Texture2D DepthMap : register(t1);
SamplerState Linear : register(s0);

float4 PS(V i) : SV_Target
{
    float2 src = SizeA.zw;
    int mode = (int)TurnA.z;
    float3 c;
    float coverage = 1;

    if (mode == 0)
    {
        // Only moved: the pixels as they are
        int2 t = int2(i.pos.xy) - int2(OffsetA.xy);
        if (t.x < 0 || t.y < 0 || t.x >= (int)src.x || t.y >= (int)src.y)
            discard;
        c = Picture.Load(int3(t, 0)).rgb;
    }
    else if (mode == 1)
    {
        // Magnified: a pixel of the screen is covered when its middle is inside the picture
        float2 s = (i.pos.xy - PlaceA.xy) * PlaceA.z + src * 0.5;
        if (s.x < 0 || s.y < 0 || s.x >= src.x || s.y >= src.y)
            discard;
        c = Picture.SampleLevel(Linear, s / src, 0).rgb;
    }
    else if (mode == 3)
    {
        // Turned (and perhaps magnified) about its middle: the turn is undone for the pixel; an edge less than a pixel inside is only partly there
        float2 d = i.pos.xy - PlaceA.xy;
        float2 s = float2(d.x * TurnA.x + d.y * TurnA.y, d.y * TurnA.x - d.x * TurnA.y) * PlaceA.z + src * 0.5;
        float inside = min(min(s.x, src.x - s.x), min(s.y, src.y - s.y)) / PlaceA.z;
        if (inside <= -0.5)
            discard;
        coverage = saturate(inside + 0.5);
        c = Picture.SampleLevel(Linear, clamp(s, 0.5, src - 0.5) / src, 0).rgb;
    }
    else
    {
        // Moved by its depth: stays where it is, what it shows moves (see Compositing.DepthShift)
        float2 pic = i.pos.xy - OffsetA.xy;
        if (pic.x < 0 || pic.y < 0 || pic.x >= src.x || pic.y >= src.y)
            discard;

        float2 mid = src * 0.5;
        float2 q = mid + (pic - mid) * PlaceA.z;
        float2 move = float2(0, 0);
        [unroll] for (int k = 0; k < 2; k++)
        {
            float m = DepthMap.SampleLevel(Linear, saturate((q + move) / src), 0).r - DeepA.z;
            move = DeepA.xy * m + DeepA.w * m * (q - mid);
        }
        c = Picture.SampleLevel(Linear, clamp(q + move, 0.5, src - 0.5) / src, 0).rgb;
    }

    return float4(c * PlaceA.w * coverage, 1);
}
";

        /// <summary>The change from one picture to the other, every pixel of it worked out from the two (see <see cref="Transitions.TransitionMixer"/>).</summary>
        public const string Mix = Vertex + @"
cbuffer MixParams : register(b0)
{
    float4 MA;   // xy: size of the screen, z: how far the change has come (eased), w: which change
    float4 MB;   // x: the mip level of the blur, y: how blurred (0 to 1), z: how much of the next picture (0 to 1), w: the size of a square of the mosaic
};

Texture2D FromTex : register(t0);
Texture2D ToTex : register(t1);
SamplerState Linear : register(s0);

static const float PI = 3.14159265;

float4 Pick(float2 p, float2 size, float share)
{
    float2 uv = p / size;
    return lerp(FromTex.SampleLevel(Linear, uv, 0), ToTex.SampleLevel(Linear, uv, 0), share);
}

float4 PS(V i) : SV_Target
{
    float2 p = i.pos.xy;
    float2 size = MA.xy;
    float e = MA.z;
    int kind = (int)MA.w;
    float2 center = size * 0.5;
    float2 uv = p / size;
    float4 from = FromTex.SampleLevel(Linear, uv, 0);
    float4 to = ToTex.SampleLevel(Linear, uv, 0);
    float4 result = from;

    if (kind == 0)
    {
        // A dissolve
        result = lerp(from, to, e);
    }
    else if (kind == 1)
    {
        // Wipe: the next picture comes in from the left behind a soft edge
        float soft = 0.15;
        float front = e * (1 + soft);
        result = lerp(from, to, smoothstep(0, 1, saturate((front - p.x / size.x) / soft)));
    }
    else if (kind == 2)
    {
        // Iris: a circle that opens from the middle
        float corner = length(center);
        float soft = max(1, corner * 0.12);
        float radius = e * (corner + soft);
        result = lerp(from, to, smoothstep(0, 1, saturate((radius - length(p - center)) / soft)));
    }
    else if (kind == 3)
    {
        // Pixelate: squares of the colour of their middle
        int block = max(1, (int)MB.w);
        int2 cell = min(int2(size) - 1, (int2(p) / block) * block + block / 2);
        float share = smoothstep(0, 1, saturate((e - 0.35) / 0.3));
        result = lerp(FromTex.Load(int3(cell, 0)), ToTex.Load(int3(cell, 0)), share);
    }
    else if (kind == 4)
    {
        // Ripple: waves run out from the middle and bend both pictures
        float2 d = p - center;
        float dist = length(d);
        float amplitude = 0.011 * size.y * sin(PI * e);
        float wavelength = max(4, 0.12 * size.y);
        float wave = amplitude * sin(2 * PI * (dist / wavelength - 2.5 * e));
        float2 q = p + (dist < 0.001 ? float2(0, 0) : d / dist * wave);
        result = Pick(q, size, smoothstep(0, 1, e));
    }
    else if (kind == 5)
    {
        // Cube: the two pictures are the front and the right face of a box that turns a quarter
        float angle = e * PI * 0.5;
        float cs = cos(angle);
        float sn = sin(angle);
        float focal = 2.2;
        float depth = focal + 1;
        float half = size.x * 0.5;
        float aspect = size.y / size.x;
        float xn = (p.x - half) / half;
        float yn = (p.y - size.y * 0.5) / half;
        float frontShade = saturate(1 - 0.45 * e);
        float rightShade = saturate(0.55 + 0.45 * e);
        float4 colour = float4(0, 0, 0, 1);
        float nearest = 1e30;

        float facing = xn * sn + focal * cs;
        if (facing > 1e-6)
        {
            float t = (depth * cs - 1) / facing;
            float lx = t * (xn * cs - focal * sn) + depth * sn;
            float ly = t * yn;
            if (t > 0 && lx >= -1 && lx <= 1 && ly >= -aspect && ly <= aspect)
            {
                nearest = t;
                colour = float4(FromTex.SampleLevel(Linear, float2((lx + 1) * 0.5, (ly / aspect + 1) * 0.5), 0).rgb * frontShade, 1);
            }
        }

        float across = xn * cs - focal * sn;
        if (across < -1e-6)
        {
            float t = (1 - depth * sn) / across;
            float lz = t * (xn * sn + focal * cs) - depth * cs;
            float ly = t * yn;
            if (t > 0 && t < nearest && lz >= -1 && lz <= 1 && ly >= -aspect && ly <= aspect)
                colour = float4(ToTex.SampleLevel(Linear, float2((lz + 1) * 0.5, (ly / aspect + 1) * 0.5), 0).rgb * rightShade, 1);
        }
        result = colour;
    }
    else if (kind == 6)
    {
        // BlurDissolve: both pictures blurred (a smaller mip level of each, spread over the screen again) most half way, and the one dissolves into the other
        float4 a = lerp(from, FromTex.SampleLevel(Linear, uv, MB.x), MB.y);
        float4 b = lerp(to, ToTex.SampleLevel(Linear, uv, MB.x), MB.y);
        result = lerp(a, b, MB.z);
    }
    else
    {
        // Dips: to black (7) or white (8) and out of it
        float4 colour = kind == 7 ? float4(0, 0, 0, 1) : float4(1, 1, 1, 1);
        result = e < 0.5 ? lerp(colour, from, 1 - 2 * e) : lerp(colour, to, 2 * e - 1);
    }

    return float4(result.rgb, 1);
}
";
    }
}
