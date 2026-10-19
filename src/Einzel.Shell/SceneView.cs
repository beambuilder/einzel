using System.Globalization;

using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Avalonia.VisualTree;

using Einzel.Commands;

using Silk.NET.OpenGL;

namespace Einzel.Shell;

/// <summary>
/// The interactive viewport: conductors, the field, and the ions, drawn with OpenGL
/// through Avalonia's own GL control.
/// </summary>
/// <remarks>
/// <para>
/// <b>Avalonia is a 2-D toolkit, so the drawing is ours.</b> WPF had Helix Toolkit and a
/// scene graph; here there is a shader, a camera and a mesh upload, and nothing more -
/// the scene has no textures, no shadows, no animation and no picking. That is the price
/// of the shell running where the engine runs, and it is small because the hard part
/// stayed where UI-1 put it: <see cref="ViewportCommand"/> extracts the conductor
/// surfaces and flies the ions, and this control draws what it is handed.
/// </para>
/// <para>
/// <b>It decides nothing about what the picture looks like.</b> Colors, layers, their order,
/// which are translucent and whether they hide what is behind them all come from
/// <see cref="ViewportPicture.Compose"/>, and the lighting in the shader is built from the
/// same constants - because <c>einzel render still</c> draws from that composition too, and a
/// still that is not the window's picture is no use to an agent trying to see what a person
/// sees. This control uploads layers and draws them in order.
/// </para>
/// <para>
/// <b>Three things a spike found the hard way, all of which look like a black window.</b>
/// A shader written <c>#version 330 core</c> fails on Windows, where Avalonia hands back
/// an OpenGL ES 3.0 context through ANGLE, and Avalonia catches the exception out of
/// <see cref="OnOpenGlInit"/> and quietly disables the control - the window opens, the
/// scene is black, and nothing says why. The control does not draw to the default
/// framebuffer, because Avalonia composites it, so every draw has to bind the one it is
/// handed. And the framebuffer is in physical pixels while <see cref="Visual.Bounds"/> is
/// in logical ones, so on a scaled display the scene lands in a corner of the surface.
/// </para>
/// </remarks>
public sealed class SceneView : OpenGlControlBase
{
    // One body, two dialects. GLSL 3.30 and GLSL ES 3.00 share layout qualifiers and
    // in/out, which is all this needs, so only the header differs.
    private const string VertexBody = """
    layout(location = 0) in vec3 aPosition;
    layout(location = 1) in vec3 aNormal;
    uniform mat4 uModelViewProjection;
    out vec3 vNormal;
    void main()
    {
        vNormal = aNormal;
        gl_Position = uModelViewProjection * vec4(aPosition, 1.0);
    }
    """;

    private readonly List<(List<Mesh> Meshes, List<Line> Lines)> _uploaded = [];
    private readonly Lock _gate = new();

    private IReadOnlyList<PictureLayer> _picture = [];
    private ViewportOutcome? _pending;

    private GL? _gl;
    private uint _program;
    private int _mvpLocation;
    private int _colorLocation;
    private int _alphaLocation;
    // Set on the render thread when the scene is first drawn and read on the UI thread by
    // the scale indicator, so every read sees a whole reference. The camera locks its own state.
    private volatile ViewportCamera? _camera;

    /// <summary>Raised whenever the camera's framing changes, from whichever thread changed it.</summary>
    /// <remarks>
    /// The scale indicator drawn over this control listens, because its length on screen is
    /// the framing's: turning to another named view, or a watched packet growing the frame,
    /// changes how many pixels a millimeter is.
    /// </remarks>
    public event EventHandler? FramingChanged;

    /// <summary>The matrix the scene is drawn with, for a viewport of this shape.</summary>
    /// <param name="aspect">Width over height.</param>
    /// <returns>Column-major 4x4 from millimeters to clip space, or null before anything is framed.</returns>
    /// <remarks>
    /// The same matrix the render pass hands the GPU, so a ruler laid out from it measures the
    /// picture it is drawn over rather than a second account of the camera.
    /// </remarks>
    public double[]? CurrentMatrix(double aspect) => _camera?.Framing.Matrix(aspect);

    /// <summary>The composed picture the viewport is drawing, layer by layer.</summary>
    /// <remarks>
    /// For the scale indicator, which places itself in the corner this picture covers least.
    /// Empty before the first draw. A whole reference is swapped in when a watched frame or the
    /// see-through toggle replaces it, so a reader on another thread sees one picture or the other.
    /// </remarks>
    public IReadOnlyList<PictureLayer> Picture => _picture;

    /// <summary>What to draw, as the command layer measured it.</summary>
    /// <remarks>
    /// Set before the control is first realized. A viewport that changed scene mid-flight
    /// would have to tear down its buffers on the render thread, which is the next piece
    /// of work rather than this one.
    /// </remarks>
    public ViewportOutcome? Scene { get; init; }

    /// <summary>How opaque the conductors are, from zero to one.</summary>
    /// <remarks>
    /// <b>Opaque by default.</b> Transparency draws every buried interface where conductors
    /// touch or overlap - a segmented chain reads as a heap rather than as a rod - so it is
    /// asked for where it earns its keep, which is a lens or a guide whose ion flies down the
    /// bore. Changing it recomposes the layers' rules and re-uploads nothing, because the
    /// colors do not depend on it.
    /// </remarks>
    public double ConductorOpacity
    {
        get;
        set
        {
            field = value;

            if (Scene is { } scene)
            {
                var recomposed = ViewportPicture.Compose(scene, value);

                // The density may have moved on since the scene was measured, and it is the
                // one layer a watch replaces - so it is carried over rather than reset.
                _picture = [.. recomposed.Select(layer =>
                    layer.Name == "density" && Find(_picture, "density") is { } live ? live : layer)];
            }

            RequestNextFrameRendering();
        }
    } = 1.0;

    private (double Azimuth, double Elevation) _view = ViewportPicture.Views["iso"];

    /// <summary>Where the camera sits, in degrees of azimuth and elevation.</summary>
    /// <remarks>
    /// <para>
    /// <b>Iso by default, and a cross-section is why.</b> Both zero looks straight down the
    /// extrusion axis, which for a translational solve is the one direction that shows
    /// nothing: the conductors are deliberately <em>uncapped</em> prisms, because a
    /// cross-section says the geometry repeats along z and capping it would draw an end the
    /// model does not have. Looking down that axis there are no faces to light, so the
    /// conductors disappear and only the trajectories are left - measured, on the linear ion
    /// trap, where the section view drew 300 flights and none of the 11 electrodes.
    /// </para>
    /// <para>
    /// <b>The frame is fitted to what the new view sees and keeps what a watch has grown
    /// into</b>, which <see cref="ViewportCamera"/> decides: re-measured from the scene alone,
    /// turning the camera during a watch would drop the packet that had drifted out of the box
    /// the scene opened in.
    /// </para>
    /// </remarks>
    public (double Azimuth, double Elevation) View
    {
        get => _view;
        set
        {
            _view = value;

            // Re-measuring is cheap and the upload is not: the meshes do not move when the
            // camera does, so only the framing is rebuilt. Before the first draw there is no
            // camera yet, and the one made then starts at this view.
            if (_camera is { } camera)
            {
                camera.Turn(value);
                FramingChanged?.Invoke(this, EventArgs.Empty);
                RequestNextFrameRendering();
            }
        }
    }

    /// <summary>Shows a newer frame of a run that is still going.</summary>
    /// <param name="frame">The run as it stands.</param>
    /// <remarks>
    /// <para>
    /// <b>Coalesced rather than queued, and that is the decision.</b> A viewport wants the
    /// newest packet, not every packet: a queue shows one from four frames ago with three
    /// behind it, falling further behind the longer it is watched. So a frame that arrives
    /// before the last one was drawn replaces it.
    /// </para>
    /// <para>
    /// The upload itself has to happen on the render thread, because that is where the GL
    /// context is - so this only hands the frame over and asks for a redraw.
    /// </para>
    /// </remarks>
    public void Show(ViewportOutcome frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            _pending = frame;
        }

        RequestNextFrameRendering();
    }

    /// <inheritdoc />
    protected override void OnOpenGlInit(GlInterface gl)
    {
        _gl = GL.GetApi(gl.GetProcAddress);

        _program = Link(_gl, Header(GlVersion));
        _mvpLocation = _gl.GetUniformLocation(_program, "uModelViewProjection");
        _colorLocation = _gl.GetUniformLocation(_program, "uColor");
        _alphaLocation = _gl.GetUniformLocation(_program, "uAlpha");

        if (Scene is not { } scene)
        {
            return;
        }

        _picture = ViewportPicture.Compose(scene, ConductorOpacity);

        foreach (var layer in _picture)
        {
            _uploaded.Add(Upload(_gl, layer));
        }

        _camera = new ViewportCamera(scene, _view);
        FramingChanged?.Invoke(this, EventArgs.Empty);

        _gl.Enable(EnableCap.DepthTest);
    }

    /// <inheritdoc />
    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is not { } api)
        {
            return;
        }

        ViewportOutcome? arrived;

        lock (_gate)
        {
            // Cleared BEFORE the frame is read, so the race can post twice and can never
            // drop the last one.
            arrived = _pending;
            _pending = null;
        }

        if (arrived is { } frame)
        {
            Arrive(api, frame);
        }

        var scaling = (this.GetVisualRoot() as IRenderRoot)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));

        // Avalonia composites this control, so it hands over a framebuffer object and
        // every draw has to name it. Drawing without binding it gives a frame that is
        // black even where the clear color should be, which reads as a renderer that
        // drew nothing at all.
        api.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        api.Viewport(0, 0, (uint)width, (uint)height);

        var (gr, gg, gb) = ColorRamp.Ground;
        api.ClearColor((float)gr, (float)gg, (float)gb, 1.0f);
        api.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        api.Enable(EnableCap.DepthTest);
        api.UseProgram(_program);

        // Rebuilt per frame because it depends on the window's shape; the expensive half,
        // measuring the instrument, was done once.
        var mvp = _camera is { } camera
            ? camera.Framing.Project((double)width / height)
            : Framing.Identity();

        fixed (float* m = mvp)
        {
            api.UniformMatrix4(_mvpLocation, 1, false, m);
        }

        var picture = _picture;

        for (var i = 0; i < picture.Count && i < _uploaded.Count; i++)
        {
            Draw(api, picture[i], _uploaded[i]);
        }
    }

    /// <inheritdoc />
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is not { } api)
        {
            return;
        }

        foreach (var layer in _uploaded)
        {
            Release(api, layer);
        }

        _uploaded.Clear();

        if (_program != 0)
        {
            api.DeleteProgram(_program);
            _program = 0;
        }

        _gl = null;
    }

    /// <summary>The <c>#version</c> line, and whatever the dialect makes mandatory.</summary>
    /// <remarks>
    /// ES additionally requires a declared default precision for floats, which desktop GL
    /// ignores rather than refuses - so it could be emitted unconditionally. It is emitted
    /// only for ES because a header that says what the context is is worth more than one
    /// that is merely accepted.
    /// </remarks>
    private static string Header(GlVersion version) =>
        version.Type == GlProfileType.OpenGLES
            ? "#version 300 es\nprecision highp float;\n\n"
            : "#version 330 core\n\n";

    /// <summary>The fragment shader, with the lighting the rasterizer uses written into it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Built from <see cref="ViewportPicture.Light"/> and <see cref="ViewportPicture.Ambient"/>
    /// rather than written out</b>, so a still and the window cannot light the same surface
    /// differently: a number typed into GLSL is a second copy of a decision.
    /// </para>
    /// <para>
    /// Two-sided lambert with the ambient term, and a zero normal gets the ambient alone - GLSL
    /// leaves the normalization of a zero vector undefined, which is not a direction.
    /// </para>
    /// </remarks>
    private static string FragmentBody()
    {
        var (lx, ly, lz) = ViewportPicture.Light;
        var ambient = ViewportPicture.Ambient;

        return $$"""
            in vec3 vNormal;
            uniform vec3 uColor;
            uniform float uAlpha;
            out vec4 fragColor;
            void main()
            {
                vec3 light = normalize(vec3({{Float(lx)}}, {{Float(ly)}}, {{Float(lz)}}));
                float size = length(vNormal);
                float lambert = size > 0.0 ? abs(dot(vNormal / size, light)) : 0.0;
                fragColor = vec4(uColor * ({{Float(ambient)}} + {{Float(1.0 - ambient)}} * lambert), uAlpha);
            }
            """;
    }

    /// <summary>A GLSL float literal.</summary>
    /// <remarks>
    /// Always with a decimal point, because GLSL ES does not convert an integer in float
    /// arithmetic - a constant that happened to be whole would print as <c>1</c>, and
    /// <c>1 * lambert</c> is a compile error that Avalonia reports as a black window.
    /// </remarks>
    private static string Float(double value) =>
        value.ToString("0.0###############", CultureInfo.InvariantCulture);

    private static PictureLayer? Find(IReadOnlyList<PictureLayer> picture, string name) =>
        picture.FirstOrDefault(layer => layer.Name == name);

    private static (List<Mesh> Meshes, List<Line> Lines) Upload(GL api, PictureLayer layer) =>
        ([.. layer.Meshes.Select(m => Mesh.Upload(api, m))], [.. layer.Lines.Select(l => Line.Upload(api, l))]);

    private static void Release(GL api, (List<Mesh> Meshes, List<Line> Lines) layer)
    {
        foreach (var mesh in layer.Meshes)
        {
            api.DeleteVertexArray(mesh.Vao);
            api.DeleteBuffer(mesh.Vbo);
            api.DeleteBuffer(mesh.Ebo);
        }

        foreach (var line in layer.Lines)
        {
            api.DeleteVertexArray(line.Vao);
            api.DeleteBuffer(line.Vbo);
        }
    }

    /// <summary>Takes in a frame of a run that is still going.</summary>
    /// <remarks>
    /// <para>
    /// Only the density is rebuilt. The sequencer refuses a stage that moves an electrode, so
    /// the conductors are identical at every instant by construction - and re-uploading a
    /// trap's whole mesh every frame is what makes a watch stutter.
    /// </para>
    /// <para>
    /// The frame only grows. A packet that drifts out of the box it was framed in leaves an
    /// empty viewport, and re-measuring per frame would make the camera breathe with the
    /// packet instead of letting the packet move.
    /// </para>
    /// </remarks>
    private void Arrive(GL api, ViewportOutcome frame)
    {
        var density = ViewportPicture.DensityLayer(frame);
        var index = _picture.ToList().FindIndex(layer => layer.Name == "density");

        if (index < 0 || index >= _uploaded.Count)
        {
            return;
        }

        Release(api, _uploaded[index]);
        _uploaded[index] = Upload(api, density);

        var layers = _picture.ToArray();
        layers[index] = density;
        _picture = layers;

        if (_camera is { } camera)
        {
            camera.Take(frame);
            FramingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Draws one layer by the rules the composition gave it.</summary>
    private unsafe void Draw(GL api, PictureLayer layer, (List<Mesh> Meshes, List<Line> Lines) uploaded)
    {
        var translucent = layer.Alpha < 1.0;

        if (translucent)
        {
            api.Enable(EnableCap.Blend);
            api.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        if (!layer.WritesDepth)
        {
            api.DepthMask(false);
        }

        api.Uniform1(_alphaLocation, (float)layer.Alpha);

        foreach (var line in uploaded.Lines)
        {
            api.Uniform3(_colorLocation, line.R, line.G, line.B);
            api.BindVertexArray(line.Vao);
            api.DrawArrays(PrimitiveType.LineStrip, 0, (uint)line.Count);
        }

        foreach (var mesh in uploaded.Meshes)
        {
            api.Uniform3(_colorLocation, mesh.R, mesh.G, mesh.B);
            api.BindVertexArray(mesh.Vao);
            api.DrawElements(PrimitiveType.Triangles, (uint)mesh.Count, DrawElementsType.UnsignedInt, null);
        }

        if (!layer.WritesDepth)
        {
            api.DepthMask(true);
        }

        if (translucent)
        {
            api.Disable(EnableCap.Blend);
        }
    }

    private static uint Link(GL api, string header)
    {
        var vertex = Compile(api, ShaderType.VertexShader, header + VertexBody);
        var fragment = Compile(api, ShaderType.FragmentShader, header + FragmentBody());

        var program = api.CreateProgram();
        api.AttachShader(program, vertex);
        api.AttachShader(program, fragment);
        api.LinkProgram(program);

        api.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);

        if (linked == 0)
        {
            throw new InvalidOperationException(
                "the viewport's shader program did not link: " + api.GetProgramInfoLog(program));
        }

        api.DetachShader(program, vertex);
        api.DetachShader(program, fragment);
        api.DeleteShader(vertex);
        api.DeleteShader(fragment);

        return program;
    }

    private static uint Compile(GL api, ShaderType type, string source)
    {
        var shader = api.CreateShader(type);
        api.ShaderSource(shader, source);
        api.CompileShader(shader);

        api.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);

        if (compiled == 0)
        {
            // Named rather than swallowed. Avalonia catches what comes out of
            // OnOpenGlInit and disables the control, so a silent failure here is a black
            // window with no explanation anywhere - which is exactly how the dialect
            // mismatch presented.
            throw new InvalidOperationException(
                $"the viewport's {type} did not compile: {api.GetShaderInfoLog(shader)}");
        }

        return shader;
    }
}
