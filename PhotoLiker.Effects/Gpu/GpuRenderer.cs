using System.Runtime.InteropServices;
using PhotoLiker.Effects.Compositing;
using PhotoLiker.Effects.Transitions;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PhotoLiker.Effects.Gpu
{
    /// <summary>What <see cref="GpuRenderer.Render"/> hands over: the pixels of the picture (0xAARRGGBB, opaque) while they are readable, and how many bytes a row of them takes (not always a multiple of the width).</summary>
    public delegate void GpuPixels(IntPtr pixels, int rowPitch);

    /// <summary>
    /// The compositor and the mixer of the slideshow on the graphics chip (Direct3D 11, pixel shaders): what <see cref="FrameCompositor.Compose"/> and <see cref="TransitionMixer.Mix"/> do on the cores of the
    /// processor, for the pictures of one step of an effect, then the screen-sized result is read back into memory (the window still draws everything else, the names, the date, the boxes, with GDI+ over it). On a
    /// 4K screen the Ripple change takes 6 ms of the chip and 27 ms with the read back (a quarter of what the processor needs); light ones (a wipe, a dissolve) stay on the processor, which does them in 2 to 12 ms.
    ///
    /// The pictures of the photos are textures made once (<see cref="Warm"/> makes them on any thread, ahead of the step that needs them; <see cref="Render"/> makes what is missing) and kept for as long as the
    /// pixels they were made from are alive (<see cref="Forget"/>), within a budget of memory; the rest of a step is a few draws. Only the thread that renders may call <see cref="Render"/> and
    /// <see cref="Dispose"/>; <see cref="Warm"/> and <see cref="Forget"/> are safe from any thread. It is the same arithmetic as the processor's, and the tests put the two side by side; a step that can not be done
    /// (a lost device, a missing driver) throws, and whoever called goes on with the processor.
    /// </summary>
    public sealed class GpuRenderer : IDisposable
    {
        private const int LayerFloats = 20;

        /// <summary>Why <see cref="TryCreate"/> gave nothing, the last time it did not; null when it did.</summary>
        public static string? LastError { get; private set; }

        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly ID3D11VertexShader _vertex;
        private readonly ID3D11PixelShader _layerShader;
        private readonly ID3D11PixelShader _mixShader;
        private readonly ID3D11SamplerState _sampler;
        private readonly ID3D11BlendState _add;
        private readonly ID3D11BlendState _replace;
        private readonly ID3D11Buffer _layerBuffer;
        private readonly ID3D11Buffer _mixBuffer;

        private readonly object _gate = new();
        private readonly Dictionary<object, TextureEntry> _textures = new(ReferenceEqualityComparer.Instance);
        private long _textureBytes;
        private long _tick;
        private int _warming;                // textures that are being made by other threads (see Warm)

        private readonly Dictionary<(int Width, int Height), Targets> _targets = [];
        private bool _disposed;

        private GpuRenderer(
            ID3D11Device device, ID3D11DeviceContext context, string adapterName, bool isSoftware,
            ID3D11VertexShader vertex, ID3D11PixelShader layerShader, ID3D11PixelShader mixShader)
        {
            _device = device;
            _context = context;
            AdapterName = adapterName;
            IsSoftware = isSoftware;
            _vertex = vertex;
            _layerShader = layerShader;
            _mixShader = mixShader;
            _sampler = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp));

            var add = new BlendDescription();
            add.RenderTarget[0] = new RenderTargetBlendDescription { BlendEnable = true, SourceBlend = Blend.One, DestinationBlend = Blend.One, BlendOperation = BlendOperation.Add, SourceBlendAlpha = Blend.One, DestinationBlendAlpha = Blend.One, BlendOperationAlpha = BlendOperation.Add, RenderTargetWriteMask = ColorWriteEnable.All };
            _add = device.CreateBlendState(add);
            var replace = new BlendDescription();
            replace.RenderTarget[0] = new RenderTargetBlendDescription { BlendEnable = false, SourceBlend = Blend.One, DestinationBlend = Blend.Zero, BlendOperation = BlendOperation.Add, SourceBlendAlpha = Blend.One, DestinationBlendAlpha = Blend.Zero, BlendOperationAlpha = BlendOperation.Add, RenderTargetWriteMask = ColorWriteEnable.All };
            _replace = device.CreateBlendState(replace);

            _layerBuffer = device.CreateBuffer(new BufferDescription(LayerFloats * 4, BindFlags.ConstantBuffer, ResourceUsage.Default));
            _mixBuffer = device.CreateBuffer(new BufferDescription(32, BindFlags.ConstantBuffer, ResourceUsage.Default));
        }

        /// <summary>The graphics chip that does the work, as Windows names it.</summary>
        public string AdapterName { get; }

        /// <summary>Whether the "chip" is software (the Microsoft Basic Render Driver or WARP): no quicker than the processor, so the program does not use it unless it is asked to.</summary>
        public bool IsSoftware { get; }

        /// <summary>How much memory the textures of pictures may take (the ones that are not needed by the step that is being done are let go of when it is passed): 384 MB.</summary>
        public long MaxTextureBytes { get; set; } = 384L * 1024 * 1024;

        /// <summary>How many bytes the textures of pictures take now.</summary>
        public long TextureBytes
        {
            get { lock (_gate) return _textureBytes; }
        }

        /// <summary>How many textures of pictures there are now.</summary>
        public int TextureCount
        {
            get { lock (_gate) return _textures.Count; }
        }

        /// <summary>
        /// Starts the renderer: a Direct3D 11 device of the graphics chip (feature level 11.0 or more), and the shaders. Null when there is none (no chip, an old one, a driver that fails, shaders that do not
        /// compile: <see cref="LastError"/> says), and when the chip is a software one, unless <paramref name="allowSoftware"/> (the tests use WARP, which every Windows has).
        /// </summary>
        public static GpuRenderer? TryCreate(bool allowSoftware = false)
        {
            try
            {
                var renderer = Create(DriverType.Hardware, allowSoftware);
                if (renderer is null && allowSoftware)
                    renderer = Create(DriverType.Warp, allowSoftware);
                return renderer;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LastError = ex.Message;
                return null;
            }
        }

        private static GpuRenderer? Create(DriverType driver, bool allowSoftware)
        {
            var result = D3D11.D3D11CreateDevice(null, driver, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0 }, out ID3D11Device? device, out ID3D11DeviceContext? context);
            if (result.Failure || device is null || context is null)
            {
                LastError = $"no Direct3D 11 device ({driver}): {result}";
                return null;
            }

            try
            {
                string name;
                bool software;
                using (var dxgi = device.QueryInterface<IDXGIDevice>())
                using (var adapter = dxgi.GetAdapter())
                using (var adapter1 = adapter.QueryInterface<IDXGIAdapter1>())
                {
                    var description = adapter1.Description1;
                    name = description.Description;
                    software = driver == DriverType.Warp || description.VendorId == 0x1414 || description.Flags.HasFlag(AdapterFlags.Software);
                }

                if (software && !allowSoftware)
                {
                    LastError = $"the adapter is a software one ({name})";
                    device.Dispose();
                    context.Dispose();
                    return null;
                }

                var vertex = device.CreateVertexShader(Compiler.Compile(GpuShaders.Layer, "VS", "gpu-layer.hlsl", "vs_5_0").ToArray());
                var layer = device.CreatePixelShader(Compiler.Compile(GpuShaders.Layer, "PS", "gpu-layer.hlsl", "ps_5_0").ToArray());
                var mix = device.CreatePixelShader(Compiler.Compile(GpuShaders.Mix, "PS", "gpu-mix.hlsl", "ps_5_0").ToArray());
                LastError = null;
                return new GpuRenderer(device, context, name, software, vertex, layer, mix);
            }
            catch
            {
                device.Dispose();
                context.Dispose();
                throw;
            }
        }

        // ── the textures of pictures ───────────────────────────────────────

        /// <summary>
        /// Makes the texture of a picture ahead (the pixels as the processor has them: 0xAARRGGBB, <paramref name="width"/> by <paramref name="height"/>), so the step that needs it does not wait for the 5 to
        /// 17 ms it takes to copy them to the chip's memory (two pictures of full HD and of 4K). Safe from any thread; nothing is made when the textures have used their memory (the step that renders lets go of
        /// the old ones). The pixels must not change while the texture is kept.
        /// </summary>
        public void Warm(uint[] pixels, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            lock (_gate)
            {
                if (_disposed || _textures.ContainsKey(pixels) || _textureBytes + (long)width * height * 4 > MaxTextureBytes)
                    return;

                _warming++; // Dispose waits for this to be made before it lets go of the device
            }

            // The copy to the chip's memory is made outside the lock, so the thread that renders does not wait for it
            try
            {
                Keep(pixels, MakePicture(pixels, width, height), (long)width * height * 4);
            }
            catch (ObjectDisposedException)
            {
                // the renderer was disposed while the texture was being made: nothing is wanted
            }
            finally
            {
                lock (_gate)
                {
                    _warming--;
                    Monitor.PulseAll(_gate);
                }
            }
        }

        /// <summary>Lets go of the texture made from these pixels (the picture is gone). Safe from any thread; the pixels may be a picture or a depth map.</summary>
        public void Forget(object pixels)
        {
            lock (_gate)
            {
                if (_textures.Remove(pixels, out var entry))
                {
                    _textureBytes -= entry.Bytes;
                    entry.Dispose();
                }
            }
        }

        private ID3D11ShaderResourceView PictureView(uint[] pixels, int width, int height)
        {
            if (Found(pixels, (long)width * height * 4) is { } found)
                return found.View;

            return Keep(pixels, MakePicture(pixels, width, height), (long)width * height * 4).View;
        }

        private ID3D11ShaderResourceView DepthView(byte[] depth, int width, int height)
        {
            if (Found(depth, (long)width * height) is { } found)
                return found.View;

            var description = new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Format.R8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource,
            };
            var handle = GCHandle.Alloc(depth, GCHandleType.Pinned);
            try
            {
                return Keep(depth, _device.CreateTexture2D(description, new SubresourceData(handle.AddrOfPinnedObject(), (uint)width)), (long)width * height).View;
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>The texture made from these pixels, marked as used by the step that is being done; if there is none, room is made for one of <paramref name="needed"/> bytes and null is returned.</summary>
        private TextureEntry? Found(object key, long needed)
        {
            lock (_gate)
            {
                if (_textures.TryGetValue(key, out var found))
                {
                    found.Used = _tick;
                    return found;
                }

                Trim(needed);
                return null;
            }
        }

        /// <summary>Makes the texture of a picture (not yet kept).</summary>
        private ID3D11Texture2D MakePicture(uint[] pixels, int width, int height)
        {
            var description = new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource,
            };
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                return _device.CreateTexture2D(description, new SubresourceData(handle.AddrOfPinnedObject(), (uint)(width * 4)));
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Keeps a texture for these pixels; if another thread made one meanwhile, that one stays and this is let go of.</summary>
        private TextureEntry Keep(object key, ID3D11Texture2D texture, long bytes)
        {
            var view = _device.CreateShaderResourceView(texture);
            var entry = new TextureEntry(texture, view, bytes);
            lock (_gate)
            {
                if (_disposed)
                {
                    entry.Dispose();
                    throw new ObjectDisposedException(nameof(GpuRenderer));
                }

                if (_textures.TryGetValue(key, out var existing))
                {
                    entry.Dispose(); // another thread was quicker: its texture stays
                    existing.Used = _tick;
                    return existing;
                }

                entry.Used = _tick;
                _textures[key] = entry;
                _textureBytes += bytes;
                return entry;
            }
        }

        /// <summary>Lets go of the textures that were used longest ago (and not by the step that is being done) until <paramref name="needed"/> more bytes fit in the budget. Called with the lock held, by the thread that renders.</summary>
        private void Trim(long needed)
        {
            if (_textureBytes + needed <= MaxTextureBytes)
                return;

            foreach (var (key, entry) in _textures.Where(pair => pair.Value.Used < _tick).OrderBy(pair => pair.Value.Used).ToList())
            {
                _textures.Remove(key);
                _textureBytes -= entry.Bytes;
                entry.Dispose();
                if (_textureBytes + needed <= MaxTextureBytes)
                    return;
            }
        }

        private sealed class TextureEntry(ID3D11Texture2D texture, ID3D11ShaderResourceView view, long bytes) : IDisposable
        {
            public ID3D11ShaderResourceView View { get; } = view;
            public long Bytes { get; } = bytes;
            public long Used { get; set; }

            public void Dispose()
            {
                View.Dispose();
                texture.Dispose();
            }
        }

        // ── the targets ────────────────────────────────────────────────────

        /// <summary>The surfaces a step is drawn on, the size of the screen: the two pictures of a change (with the smaller copies of them the blur takes from), the result, and the place it is read back from.</summary>
        private sealed class Targets : IDisposable
        {
            public required int Width { get; init; }
            public required int Height { get; init; }
            public required Surface From { get; init; }
            public required Surface To { get; init; }
            public required Surface Out { get; init; }
            public required ID3D11Texture2D Staging { get; init; }

            public void Dispose()
            {
                From.Dispose();
                To.Dispose();
                Out.Dispose();
                Staging.Dispose();
            }
        }

        private sealed class Surface(ID3D11Texture2D texture, ID3D11RenderTargetView target, ID3D11ShaderResourceView view) : IDisposable
        {
            public ID3D11Texture2D Texture { get; } = texture;
            public ID3D11RenderTargetView Target { get; } = target;
            public ID3D11ShaderResourceView View { get; } = view;

            public void Dispose()
            {
                View.Dispose();
                Target.Dispose();
                Texture.Dispose();
            }
        }

        private Surface MakeSurface(int width, int height, bool mips)
        {
            var description = new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = mips ? 0u : 1u, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                MiscFlags = mips ? ResourceOptionFlags.GenerateMips : ResourceOptionFlags.None,
            };
            var texture = _device.CreateTexture2D(description);
            return new Surface(texture, _device.CreateRenderTargetView(texture), _device.CreateShaderResourceView(texture));
        }

        /// <summary>The surfaces for this size of screen: made once, and kept for the next step (monitors of different sizes in one show each keep theirs; the most that are kept is <see cref="MostSizes"/>).</summary>
        private Targets EnsureTargets(int width, int height)
        {
            var key = (width, height);
            if (_targets.TryGetValue(key, out var have))
                return have;

            while (_targets.Count >= MostSizes)
            {
                var oldest = _targets.Keys.First();
                _targets[oldest].Dispose();
                _targets.Remove(oldest);
            }

            var staging = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, BindFlags = BindFlags.None, CPUAccessFlags = CpuAccessFlags.Read,
            });
            return _targets[key] = new Targets
            {
                Width = width, Height = height, From = MakeSurface(width, height, mips: true), To = MakeSurface(width, height, mips: true),
                Out = MakeSurface(width, height, mips: false), Staging = staging,
            };
        }

        /// <summary>How many sizes of screen have their surfaces kept at once.</summary>
        private const int MostSizes = 3;

        // ── a step ─────────────────────────────────────────────────────────

        /// <summary>
        /// Does one step of an effect: the <paramref name="layers"/> put together as <see cref="FrameCompositor.Compose"/> does it (their weights adding up), or, when <paramref name="transition"/> is one
        /// that <see cref="SlideshowEffects.UsesMixer"/> and there are two layers (the photo that goes, the one that comes: each is put together alone, wholly, and the two are mixed as
        /// <see cref="TransitionMixer.Mix"/> does it <paramref name="progress"/> of the way through). The picture is then given to <paramref name="consume"/>, which must not keep the pointer.
        /// </summary>
        public void Render(int width, int height, ReadOnlySpan<CompositeLayer> layers, SlideshowTransition transition, double progress, GpuPixels consume)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(consume);
            ObjectDisposedException.ThrowIf(_disposed, this);

            lock (_gate)
                _tick++;
            var targets = EnsureTargets(width, height);

            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vertex);
            _context.RSSetViewport(0, 0, width, height);
            _context.PSSetSamplers(0, [_sampler]);

            if (SlideshowEffects.UsesMixer(transition) && layers.Length == 2)
            {
                var p = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0, 1);
                if (p <= 0)
                {
                    Compose(targets.Out, layers[..1], width, height, wholly: true);
                }
                else if (p >= 1)
                {
                    Compose(targets.Out, layers[1..], width, height, wholly: true);
                }
                else
                {
                    Compose(targets.From, layers[..1], width, height, wholly: true);
                    Compose(targets.To, layers[1..], width, height, wholly: true);
                    if (transition == SlideshowTransition.BlurDissolve)
                    {
                        _context.GenerateMips(targets.From.View);
                        _context.GenerateMips(targets.To.View);
                    }
                    Mix(targets, transition, p, width, height);
                }
            }
            else
            {
                Compose(targets.Out, layers, width, height, wholly: false);
            }

            _context.CopyResource(targets.Staging, targets.Out.Texture);
            var mapped = _context.Map(targets.Staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                consume(mapped.DataPointer, (int)mapped.RowPitch);
            }
            finally
            {
                _context.Unmap(targets.Staging, 0);
            }
        }

        /// <summary>The same step, with the picture copied into <paramref name="destination"/> (<paramref name="width"/> by <paramref name="height"/> pixels, row after row).</summary>
        public void Render(int width, int height, ReadOnlySpan<CompositeLayer> layers, SlideshowTransition transition, double progress, uint[] destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, width * height, nameof(destination));

            var row = new int[width];
            Render(width, height, layers, transition, progress, (pixels, pitch) =>
            {
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels + y * pitch, row, 0, width);
                    Buffer.BlockCopy(row, 0, destination, y * width * 4, width * 4);
                }
            });
        }

        private void Compose(Surface target, ReadOnlySpan<CompositeLayer> layers, int width, int height, bool wholly)
        {
            _context.PSSetShaderResources(0, [null!, null!]);
            _context.OMSetRenderTargets(target.Target);
            _context.ClearRenderTargetView(target.Target, new Color4(0f, 0f, 0f, 1f));
            _context.OMSetBlendState(_add);
            _context.PSSetShader(_layerShader);
            _context.PSSetConstantBuffers(0, [_layerBuffer]);

            var values = new float[LayerFloats];
            foreach (var layer in layers)
            {
                var placed = wholly ? layer with { Weight = FrameCompositor.FullWeight } : layer;
                if (!Describe(placed, width, height, values, out var picture, out var depth))
                    continue;

                _context.UpdateSubresource(values, _layerBuffer);
                _context.PSSetShaderResources(0, [picture, depth]);
                _context.Draw(3, 0);
            }
        }

        /// <summary>Works out what the shader needs of a layer (the same decisions as <see cref="FrameCompositor"/> makes); false when nothing of it shows.</summary>
        private bool Describe(in CompositeLayer layer, int width, int height, float[] values, out ID3D11ShaderResourceView picture, out ID3D11ShaderResourceView depthView)
        {
            picture = null!;
            depthView = null!;

            var weight = Math.Clamp(layer.Weight, 0, FrameCompositor.FullWeight);
            if (weight == 0 || layer.Width <= 0 || layer.Height <= 0 || layer.Pixels.Length < layer.Width * layer.Height ||
                double.IsNaN(layer.Scale) || layer.Scale <= 0 || double.IsNaN(layer.CenterX) || double.IsNaN(layer.CenterY))
                return false;

            var scale = layer.Scale;
            var mode = 0;
            DepthShift? deep = null;
            if (layer.Depth is { } depth)
            {
                if (depth.DepthWidth > 0 && depth.DepthHeight > 0 && depth.Depth.Length >= depth.DepthWidth * depth.DepthHeight
                    && !double.IsNaN(depth.ShiftX) && !double.IsNaN(depth.ShiftY) && !double.IsNaN(depth.Focus))
                {
                    mode = 2;
                    deep = depth;
                }
                else
                {
                    scale = 1; // a depth that does not fit leaves the picture as it is
                }
            }
            else if (Math.Abs(layer.Rotation) > 1e-9 && double.IsFinite(layer.Rotation))
            {
                mode = 3;
            }
            else if (Math.Abs(scale - 1) >= 1e-9)
            {
                mode = 1;
            }

            var offsetX = Math.Floor(layer.CenterX - layer.Width / 2.0 + 0.5);
            var offsetY = Math.Floor(layer.CenterY - layer.Height / 2.0 + 0.5);
            Array.Clear(values);
            values[0] = width; values[1] = height; values[2] = layer.Width; values[3] = layer.Height;
            values[4] = (float)layer.CenterX; values[5] = (float)layer.CenterY; values[6] = (float)(1 / scale); values[7] = weight / (float)FrameCompositor.FullWeight;
            values[8] = (float)Math.Cos(layer.Rotation); values[9] = (float)Math.Sin(layer.Rotation); values[10] = mode;
            values[12] = (float)offsetX; values[13] = (float)offsetY;
            if (deep is { } d)
            {
                values[16] = (float)d.ShiftX; values[17] = (float)d.ShiftY; values[18] = (float)d.Focus; values[19] = (float)d.Radial;
                depthView = DepthView(d.Depth, d.DepthWidth, d.DepthHeight);
            }

            picture = PictureView(layer.Pixels, layer.Width, layer.Height);
            return true;
        }

        private void Mix(Targets targets, SlideshowTransition transition, double progress, int width, int height)
        {
            var e = (float)SlideshowEffects.Ease(progress);
            float kind = transition switch
            {
                SlideshowTransition.Wipe => 1, SlideshowTransition.Iris => 2, SlideshowTransition.Pixelate => 3, SlideshowTransition.Ripple => 4,
                SlideshowTransition.Cube => 5, SlideshowTransition.BlurDissolve => 6, SlideshowTransition.DipBlack => 7, SlideshowTransition.DipWhite => 8, _ => 0,
            };

            float level = 0, blur = 0, share = 0, block = 1;
            switch (transition)
            {
                case SlideshowTransition.Pixelate:
                {
                    var largest = Math.Max(8f, height * TransitionMixer.MaxBlockShare);
                    block = Math.Max(1f, MathF.Round(1 + (largest - 1) * MathF.Sin(MathF.PI * e)));
                    break;
                }
                case SlideshowTransition.BlurDissolve:
                {
                    var factor = Math.Max(4, MathF.Round(height * TransitionMixer.BlurShare));
                    level = MathF.Log2(factor) + 0.5f;
                    blur = MathF.Pow(MathF.Sin(MathF.PI * e), 0.8f);
                    var t = Math.Clamp((e - 0.3f) / 0.4f, 0f, 1f);
                    share = t * t * (3 - 2 * t);
                    break;
                }
            }

            _context.PSSetShaderResources(0, [null!, null!]);
            _context.OMSetRenderTargets(targets.Out.Target);
            _context.OMSetBlendState(_replace);
            _context.PSSetShader(_mixShader);
            _context.PSSetConstantBuffers(0, [_mixBuffer]);
            _context.UpdateSubresource(new float[] { width, height, e, kind, level, blur, share, block }, _mixBuffer);
            _context.PSSetShaderResources(0, [targets.From.View, targets.To.View]);
            _context.Draw(3, 0);
            _context.PSSetShaderResources(0, [null!, null!]);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_gate)
            {
                _disposed = true;
                for (var wait = 0; _warming > 0 && wait < 50; wait++)
                    Monitor.Wait(_gate, 100); // a texture that is being made on another thread is let to finish: a device that is let go of under a call is not a thing to risk
                foreach (var entry in _textures.Values)
                    entry.Dispose();
                _textures.Clear();
                _textureBytes = 0;
            }

            foreach (var targets in _targets.Values)
                targets.Dispose();
            _targets.Clear();
            _layerBuffer.Dispose();
            _mixBuffer.Dispose();
            _add.Dispose();
            _replace.Dispose();
            _sampler.Dispose();
            _layerShader.Dispose();
            _mixShader.Dispose();
            _vertex.Dispose();
            _context.Dispose();
            _device.Dispose();
        }
    }
}
