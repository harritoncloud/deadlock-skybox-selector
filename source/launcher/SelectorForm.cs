using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

/// <summary>
/// Keeps the fixed-size interface inside the current screen. The layout is authored for
/// 1200x790 logical pixels and scaled by <see cref="AutoScaleMode.Dpi"/>; on a small or
/// high-DPI display the scaled window can become taller than the work area, and because
/// MinimumSize equals MaximumSize the user cannot resize it back. The effective scale is
/// therefore capped to whatever fits, and font point sizes follow the same cap so text
/// stays proportional to its containers.
/// </summary>
internal static class UiScale
{
    private const float MinimumScale = 0.6F;
    private static float systemScale = 1F;
    private static float effectiveScale = 1F;

    /// <summary>Multiplier applied to every font point size requested from <see cref="UiTheme"/>.</summary>
    public static float FontFactor
    {
        get { return systemScale <= 0F ? 1F : effectiveScale / systemScale; }
    }

    /// <summary>
    /// Value to assign to <see cref="ContainerControl.AutoScaleDimensions"/> so that WinForms
    /// scales bounds by <see cref="effectiveScale"/> instead of the raw DPI ratio.
    /// </summary>
    public static SizeF AutoScaleDimensions
    {
        get
        {
            float dimension = 96F * systemScale / Math.Max(0.01F, effectiveScale);
            return new SizeF(dimension, dimension);
        }
    }

    /// <summary>
    /// Measures the system DPI and the work area, then caps the scale so a window of
    /// <paramref name="logicalWidth"/> x <paramref name="logicalHeight"/> still fits.
    /// Configurations that already fit are left untouched (cap equals the DPI ratio).
    /// </summary>
    public static void Configure(int logicalWidth, int logicalHeight)
    {
        try
        {
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                systemScale = Math.Max(0.5F, graphics.DpiY / 96F);
        }
        catch
        {
            systemScale = 1F;
        }

        effectiveScale = systemScale;
        try
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            if (workingArea.Width > 0 && workingArea.Height > 0 && logicalWidth > 0 && logicalHeight > 0)
            {
                float fits = Math.Min(
                    (float)workingArea.Width / logicalWidth,
                    (float)workingArea.Height / logicalHeight);
                effectiveScale = Math.Max(MinimumScale, Math.Min(systemScale, fits));
            }
        }
        catch
        {
            effectiveScale = systemScale;
        }
    }
}

/// <summary>Bytes of a file compiled into the launcher, or null when the resource is not there.</summary>
internal static class EmbeddedPayload
{
    public static byte[] Read(string resourceName)
    {
        using (Stream input = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName))
        {
            if (input == null)
                return null;
            using (MemoryStream output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}

/// <summary>
/// Registers a typeface that ships inside the executable, for this process only.
///
/// Nothing is written to the font directory and no installer runs, so a machine that already has
/// its own copy of the face, an older version of it, or none at all all behave identically - there
/// is no system-wide state to collide with. The pair of calls is deliberate and neither one is
/// sufficient on its own: <c>AddFontMemResourceEx</c> makes the face visible to GDI, which is the
/// path <see cref="TextRenderer"/> and therefore every WinForms <see cref="Label"/> takes, while
/// <see cref="PrivateFontCollection.AddMemoryFont"/> is what produces a GDI+
/// <see cref="FontFamily"/> to hand to a <see cref="Font"/>. With only the collection, labels
/// silently fall back to a substitute face.
/// </summary>
internal static class EmbeddedFont
{
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);

    private static readonly PrivateFontCollection Collection = new PrivateFontCollection();

    /// <summary>
    /// Loads a TrueType resource and returns the family it added, or null when anything at all goes
    /// wrong. Failure is a fallback rather than a fault: a missing or unparseable face should leave
    /// the interface in a system font, not stop the application from opening.
    /// </summary>
    public static FontFamily Load(string resourceName)
    {
        try
        {
            HashSet<string> before = new HashSet<string>();
            foreach (FontFamily existing in Collection.Families)
                before.Add(existing.Name);

            if (!Register(resourceName))
                return null;

            foreach (FontFamily family in Collection.Families)
            {
                if (!before.Contains(family.Name))
                    return family;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    /// <summary>
    /// Hands one font resource to GDI and to GDI+. Both are needed and neither is sufficient: see the
    /// note on the class.
    /// </summary>
    private static bool Register(string resourceName)
    {
        byte[] data = EmbeddedPayload.Read(resourceName);
        if (data == null || data.Length == 0)
            return false;

        IntPtr buffer = Marshal.AllocCoTaskMem(data.Length);
        Marshal.Copy(data, 0, buffer, data.Length);
        uint registered;
        if (AddFontMemResourceEx(buffer, (uint)data.Length, IntPtr.Zero, out registered) == IntPtr.Zero ||
            registered == 0)
        {
            Marshal.FreeCoTaskMem(buffer);
            return false;
        }

        // The buffer is never freed. GDI and GDI+ both read the face out of it for as long as it
        // stays registered, which is the whole run, so this is the lifetime of the process rather
        // than a leak: one allocation of the size of the font file, made once at startup.
        Collection.AddMemoryFont(buffer, data.Length);
        return true;
    }
}

internal static class UiTheme
{
    // Deadlock's own HUD sits on a near-black olive and spends its colour on brass, so the surfaces
    // here are pushed darker and warmer than a neutral dark theme would be: every step of the ramp
    // keeps a little more red than green-blue, which is what stops the panels reading as grey once
    // the brass accents land on top of them.
    public static readonly Color Background = Color.FromArgb(14, 19, 18);
    public static readonly Color BackgroundSoft = Color.FromArgb(21, 29, 27);
    public static readonly Color Sidebar = Color.FromArgb(13, 19, 18);
    public static readonly Color Surface = Color.FromArgb(26, 35, 32);
    public static readonly Color SurfaceRaised = Color.FromArgb(35, 47, 43);
    public static readonly Color SurfaceHover = Color.FromArgb(47, 63, 56);
    public static readonly Color Border = Color.FromArgb(94, 106, 86);
    public static readonly Color BorderSoft = Color.FromArgb(46, 60, 54);
    public static readonly Color Text = Color.FromArgb(247, 234, 200);
    public static readonly Color TextMuted = Color.FromArgb(180, 167, 133);
    public static readonly Color TextDim = Color.FromArgb(126, 116, 91);
    public static readonly Color Accent = Color.FromArgb(232, 161, 40);
    public static readonly Color AccentHover = Color.FromArgb(253, 206, 106);
    /// <summary>Dull side of the brass ramp, for rules and markers that must not shout.</summary>
    public static readonly Color Brass = Color.FromArgb(146, 106, 42);
    public static readonly Color Violet = Color.FromArgb(190, 94, 54);
    public static readonly Color Cyan = Color.FromArgb(86, 176, 156);
    public static readonly Color Success = Color.FromArgb(120, 186, 118);
    public static readonly Color Warning = Color.FromArgb(235, 172, 55);
    public static readonly Color Danger = Color.FromArgb(204, 80, 62);

    // Typography. One face for the whole interface - Titan One, a single ultra-heavy display cut that
    // ships inside the executable and is registered per process, so a new machine needs nothing
    // installed and cannot collide with a copy of its own (see EmbeddedFont). Nothing here separates a
    // heading from a caption by family or by weight, because the family holds exactly one weight: size,
    // colour and tracking carry the whole hierarchy.
    //
    // That single weight is also why the style bit must stay off it. The face is already heavier than a
    // synthesised bold could make it, and GDI+ answers a cut a family does not have by smearing the
    // master sideways until the counters close, which at the 7-10 pt this window uses most turns the
    // letters into blocks. IsWeightedFamily is what keeps Bold from ever reaching it.
    //
    // The system chain behind it is the fallback for a machine where the resource fails to register. It
    // is resolved once against the installed collection and the first real match wins, because GDI+
    // substitutes Microsoft Sans Serif for a family it cannot find instead of failing, and a face
    // missing from a particular Windows build would quietly ruin the layout.
    private static readonly FontFamily EmbeddedFamily = EmbeddedFont.Load(
        "SkyboxSelector.Payload.TitanOne-Regular.ttf");
    private static readonly FontFamily TextFamily = EmbeddedFamily ?? ResolveFamily(
        "Segoe UI Black", "Arial Black", "Franklin Gothic Heavy", "Impact", "Segoe UI", "Arial");
    // Data stays monospaced whatever the interface face is. The convar list and the install path are
    // there to be compared against a file on disk, character by character, and a proportional face
    // would take exactly that away.
    private static readonly FontFamily MonoFamily = ResolveFamily(
        "Cascadia Mono", "Consolas", "Lucida Console", "Courier New");

    /// <summary>
    /// Interface text. The requested style is dropped on a face that already carries its weight, so a
    /// caller asking for bold gets the drawn face rather than a smeared copy of it.
    /// </summary>
    public static Font Font(float size, FontStyle style)
    {
        return Create(TextFamily, size, IsWeightedFamily(TextFamily) ? FontStyle.Regular : style);
    }

    /// <summary>
    /// Headings, buttons, card titles - anything that carries the tone of the interface. It is the same
    /// family and the same weight as the body text: with one cut in the file, what marks a heading is
    /// how big it is set and how far apart its capitals stand, not how heavy it is drawn.
    /// </summary>
    public static Font Heavy(float size)
    {
        return Font(size, FontStyle.Bold);
    }

    /// <summary>Monospaced face for values that are data rather than prose, such as the game path.</summary>
    public static Font Mono(float size)
    {
        return Create(MonoFamily, size, FontStyle.Regular);
    }

    /// <summary>
    /// Adds letter spacing to the short upper-case labels. GDI+ cannot track text, so the
    /// characters are separated by thin spaces, which is what gives set capitals their air.
    /// </summary>
    public static string Track(string text)
    {
        if (String.IsNullOrEmpty(text))
            return text;
        StringBuilder tracked = new StringBuilder(text.Length * 2);
        foreach (char character in text)
        {
            if (tracked.Length > 0)
                tracked.Append('\u2009');   // thin space, escaped so the source stays plain ASCII
            tracked.Append(character);
        }
        return tracked.ToString();
    }

    private static Font Create(FontFamily family, float size, FontStyle style)
    {
        float scaled = Math.Max(5F, size * UiScale.FontFactor);
        try
        {
            if (!family.IsStyleAvailable(style))
                style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
            return new Font(family, scaled, style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, scaled, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    /// <summary>
    /// True when the family already carries its weight, so the style bit must not be set on top of it.
    /// The embedded face is the main case: Titan One holds one ultra-heavy cut and there is nothing
    /// heavier in the file for GDI+ to reach, so a request for bold could only be answered by faking it.
    /// </summary>
    private static bool IsWeightedFamily(FontFamily family)
    {
        if (EmbeddedFamily != null && family.Name == EmbeddedFamily.Name)
            return true;
        return family.Name.IndexOf("semibold", StringComparison.OrdinalIgnoreCase) >= 0 ||
            family.Name.IndexOf("semilight", StringComparison.OrdinalIgnoreCase) >= 0 ||
            family.Name.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0 ||
            family.Name.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0 ||
            family.Name.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0 ||
            family.Name.IndexOf("impact", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static FontFamily ResolveFamily(params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            try
            {
                FontFamily family = new FontFamily(candidate);
                if (family.IsStyleAvailable(FontStyle.Regular) || family.IsStyleAvailable(FontStyle.Bold))
                    return family;
                family.Dispose();
            }
            catch (ArgumentException)
            {
            }
        }
        return FontFamily.GenericSansSerif;
    }

    public static Color Mix(Color from, Color to, float amount)
    {
        amount = Math.Max(0F, Math.Min(1F, amount));
        return Color.FromArgb(
            (int)(from.A + ((to.A - from.A) * amount)),
            (int)(from.R + ((to.R - from.R) * amount)),
            (int)(from.G + ((to.G - from.G) * amount)),
            (int)(from.B + ((to.B - from.B) * amount)));
    }

    public static Color Alpha(Color color, int alpha)
    {
        return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
    }

    public static GraphicsPath RoundedPath(RectangleF rectangle, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float diameter = Math.Max(1F, radius * 2F);
        if (radius <= 0F)
        {
            path.AddRectangle(rectangle);
            path.CloseFigure();
            return path;
        }

        RectangleF arc = new RectangleF(rectangle.X, rectangle.Y, diameter, diameter);
        path.AddArc(arc, 180F, 90F);
        arc.X = rectangle.Right - diameter;
        path.AddArc(arc, 270F, 90F);
        arc.Y = rectangle.Bottom - diameter;
        path.AddArc(arc, 0F, 90F);
        arc.X = rectangle.X;
        path.AddArc(arc, 90F, 90F);
        path.CloseFigure();
        return path;
    }

    public static float EaseOutCubic(float value)
    {
        value = Math.Max(0F, Math.Min(1F, value));
        float inverse = 1F - value;
        return 1F - (inverse * inverse * inverse);
    }

    /// <summary>
    /// Fakes a drop shadow with a few concentric strokes. Everything that needs one paints on an
    /// opaque parent, so a blurred bitmap per frame would cost far more than the three passes it
    /// takes to read as a soft edge.
    /// </summary>
    public static void DrawSoftShadow(Graphics graphics, RectangleF bounds, float radius, int layers, int strength)
    {
        for (int layer = layers; layer >= 1; layer--)
        {
            RectangleF spread = RectangleF.Inflate(bounds, layer, layer);
            if (spread.Width <= 1F || spread.Height <= 1F)
                continue;
            using (GraphicsPath path = RoundedPath(spread, radius + layer))
            using (Pen pen = new Pen(Color.FromArgb(Math.Max(1, strength / layer), 0, 0, 0), 1.6F))
                graphics.DrawPath(pen, path);
        }
    }
}

internal sealed class DashboardCanvas : Panel
{
    private Bitmap backgroundCache;

    public DashboardCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (backgroundCache == null || backgroundCache.Size != bounds.Size)
            RebuildBackgroundCache(bounds.Size);
        e.Graphics.DrawImageUnscaled(backgroundCache, Point.Empty);
    }

    private void RebuildBackgroundCache(Size size)
    {
        if (backgroundCache != null)
            backgroundCache.Dispose();
        backgroundCache = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        Rectangle bounds = new Rectangle(Point.Empty, size);
        using (Graphics graphics = Graphics.FromImage(backgroundCache))
        {
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds,
                Color.FromArgb(44, 55, 47),
                UiTheme.Background,
                132F))
                graphics.FillRectangle(background, bounds);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawGlow(graphics, new PointF(bounds.Width * 0.12F, bounds.Height * 0.08F), 360F, UiTheme.Accent, 58);
            DrawGlow(graphics, new PointF(bounds.Width * 0.82F, bounds.Height * 0.78F), 440F, UiTheme.Violet, 40);
            DrawEdgeGlow(graphics, bounds);
        }
    }

    private static void DrawEdgeGlow(Graphics graphics, Rectangle bounds)
    {
        Color edge = UiTheme.Mix(UiTheme.Accent, UiTheme.Violet, 0.35F);
        for (int inset = 10; inset >= 1; inset--)
        {
            int alpha = 10 + ((11 - inset) * 6);
            RectangleF glowBounds = new RectangleF(
                inset + 0.5F,
                inset + 0.5F,
                Math.Max(1F, bounds.Width - (inset * 2F) - 1F),
                Math.Max(1F, bounds.Height - (inset * 2F) - 1F));
            using (GraphicsPath path = UiTheme.RoundedPath(glowBounds, Math.Max(10F, 18F - inset)))
            using (Pen glow = new Pen(UiTheme.Alpha(edge, alpha), 1.15F))
                graphics.DrawPath(glow, path);
        }

        RectangleF borderBounds = new RectangleF(0.75F, 0.75F, bounds.Width - 2F, bounds.Height - 2F);
        using (GraphicsPath borderPath = UiTheme.RoundedPath(borderBounds, 17F))
        using (Pen border = new Pen(UiTheme.Alpha(UiTheme.AccentHover, 150), 1.25F))
            graphics.DrawPath(border, borderPath);
    }

    private static void DrawGlow(Graphics graphics, PointF center, float radius, Color color, int alpha)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            path.AddEllipse(center.X - radius, center.Y - radius, radius * 2F, radius * 2F);
            using (PathGradientBrush glow = new PathGradientBrush(path))
            {
                glow.CenterColor = Color.FromArgb(alpha, color);
                glow.SurroundColors = new[] { Color.FromArgb(0, color) };
                graphics.FillPath(glow, path);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && backgroundCache != null)
        {
            backgroundCache.Dispose();
            backgroundCache = null;
        }
        base.Dispose(disposing);
    }
}

internal class RoundedPanel : Panel
{
    private bool clipChildren;

    public Color FillColor { get; set; }
    public Color BorderColor { get; set; }

    /// <summary>Top colour of a vertical fill. <see cref="Color.Empty"/> keeps the fill flat.</summary>
    public Color GradientColor { get; set; }
    public int CornerRadius { get; set; }
    public bool DrawTopGlow { get; set; }
    public bool ClipChildren
    {
        get { return clipChildren; }
        set
        {
            clipChildren = value;
            UpdateClipRegion();
        }
    }

    public RoundedPanel()
    {
        FillColor = UiTheme.Surface;
        BorderColor = UiTheme.BorderSoft;
        CornerRadius = 18;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateClipRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateClipRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF rectangle = new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F);
        using (GraphicsPath path = UiTheme.RoundedPath(rectangle, CornerRadius))
        using (Brush fill = CreateFillBrush(rectangle))
        using (Pen border = new Pen(BorderColor, 1F))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        if (DrawTopGlow)
        {
            Rectangle glowBounds = new Rectangle(16, 0, Math.Max(1, Width - 32), 2);
            using (LinearGradientBrush glow = new LinearGradientBrush(
                glowBounds,
                Color.Transparent,
                UiTheme.Accent,
                0F))
            {
                ColorBlend blend = new ColorBlend();
                blend.Colors = new[] { Color.Transparent, UiTheme.Accent, UiTheme.Violet, Color.Transparent };
                blend.Positions = new[] { 0F, 0.28F, 0.72F, 1F };
                glow.InterpolationColors = blend;
                e.Graphics.FillRectangle(glow, glowBounds);
            }
        }
    }

    private Brush CreateFillBrush(RectangleF bounds)
    {
        if (GradientColor.A == 0)
            return new SolidBrush(FillColor);
        // One pixel of overhang top and bottom: a gradient brush tiles, and without the overhang
        // the seam between tiles lands on the panel's own top edge as a hard line.
        return new LinearGradientBrush(
            new RectangleF(bounds.X, bounds.Y - 1F, Math.Max(1F, bounds.Width), Math.Max(1F, bounds.Height) + 2F),
            GradientColor,
            FillColor,
            LinearGradientMode.Vertical);
    }

    /// <summary>
    /// Colour of the panel background at a given row. Children that paint their own background
    /// use it so a gradient panel does not leave a flat rectangle around every control.
    /// </summary>
    public Color FillAt(int y)
    {
        if (GradientColor.A == 0 || Height <= 0)
            return FillColor;
        return UiTheme.Mix(GradientColor, FillColor, (float)y / Height);
    }

    private void UpdateClipRegion()
    {
        if (!clipChildren || Width <= 0 || Height <= 0)
        {
            if (!clipChildren && Region != null)
            {
                Region previous = Region;
                Region = null;
                previous.Dispose();
            }
            return;
        }

        using (GraphicsPath path = UiTheme.RoundedPath(
            new RectangleF(0, 0, Width, Height), CornerRadius))
        {
            Region previous = Region;
            Region = new Region(path);
            if (previous != null)
                previous.Dispose();
        }
    }
}

/// <summary>
/// Body copy that sets its own leading. The display face the interface runs on declares a line box
/// barely taller than its capitals - correct for a headline, unreadable for a paragraph - and
/// WinForms exposes no line spacing on a <see cref="Label"/>, so the rows are wrapped here and
/// painted at a pitch this control chooses instead of the one the face asks for.
///
/// Wrapping runs when the text, the face or the width changes and never on a paint: the gallery is
/// benchmarked for handle growth over hundreds of redraws, and measuring a paragraph word by word
/// inside OnPaint is exactly the kind of work that budget exists to keep out.
/// </summary>
internal sealed class ProseLabel : Label
{
    private static readonly Size Unbounded = new Size(4096, 512);

    private const TextFormatFlags MeasureFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    private string[] rows = new string[0];
    private int leading = 5;

    public ProseLabel()
    {
        AutoSize = false;
        BackColor = Color.Transparent;
        UseMnemonic = false;
    }

    /// <summary>Pixels added between rows, on top of the line height the face declares.</summary>
    public int Leading
    {
        get { return leading; }
        set
        {
            if (leading == value)
                return;
            leading = value;
            Invalidate();
        }
    }

    /// <summary>Height the wrapped rows occupy, with no leading hanging off the last one.</summary>
    public int MeasuredHeight
    {
        get { return rows.Length == 0 ? 0 : (rows.Length * (Font.Height + leading)) - leading; }
    }

    /// <summary>
    /// Fixes the column width, wraps to it, then shrinks the box onto the rows that resulted. The
    /// two steps cannot be collapsed: the row count is not known until the width is.
    /// </summary>
    public void LayoutColumn(int width)
    {
        Size = new Size(width, Math.Max(1, Font.Height));
        Height = Math.Max(Font.Height, MeasuredHeight);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Reflow();
        base.OnTextChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        Reflow();
        base.OnFontChanged(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        Reflow();
        base.OnSizeChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int pitch = Font.Height + leading;
        int top = 0;
        foreach (string row in rows)
        {
            TextRenderer.DrawText(e.Graphics, row, Font, new Point(0, top), ForeColor, MeasureFlags);
            top += pitch;
        }
    }

    /// <summary>Greedy word wrap at the current width. A word wider than the column keeps its row.</summary>
    private void Reflow()
    {
        int width = Math.Max(1, ClientSize.Width);
        List<string> wrapped = new List<string>();
        string[] paragraphs = (Text ?? String.Empty).Replace("\r", String.Empty).Split('\n');
        foreach (string paragraph in paragraphs)
        {
            string row = String.Empty;
            foreach (string word in paragraph.Split(' '))
            {
                if (word.Length == 0)
                    continue;
                string candidate = row.Length == 0 ? word : row + " " + word;
                if (row.Length != 0 &&
                    TextRenderer.MeasureText(candidate, Font, Unbounded, MeasureFlags).Width > width)
                {
                    wrapped.Add(row);
                    row = word;
                    continue;
                }
                row = candidate;
            }
            wrapped.Add(row);
        }

        rows = wrapped.ToArray();
        Invalidate();
    }
}

/// <summary>
/// The application icon, decoded straight out of the embedded .ico instead of going through
/// <see cref="Icon.ToBitmap"/>. Every frame in the file is a PNG and the managed Icon path routes
/// those through <c>Bitmap.FromHicon</c>, which is the classic way to lose an alpha channel; the
/// icon is a rounded tile whose entire edge is alpha, so it would pick up a black fringe on the dark
/// rail. Reading the frame the file already stores keeps that edge exact and takes no icon handle.
/// </summary>
internal static class AppIcon
{
    /// <summary>
    /// The best frame for a box of <paramref name="size"/> pixels, resampled only when no frame is
    /// that size. Returns null when the resource is missing or malformed, so the caller can fall
    /// back to drawing something of its own.
    /// </summary>
    public static Bitmap Load(string resourceName, int size)
    {
        try
        {
            byte[] data = EmbeddedPayload.Read(resourceName);
            if (data == null || data.Length < 6 || size <= 0)
                return null;

            int frames = BitConverter.ToUInt16(data, 4);
            int bestOffset = -1;
            int bestLength = 0;
            int bestSide = 0;
            for (int index = 0; index < frames; index++)
            {
                int entry = 6 + (index * 16);
                if (entry + 16 > data.Length)
                    break;
                // A zero in the width byte means 256: the directory field is a single byte.
                int side = data[entry] == 0 ? 256 : data[entry];
                long length = BitConverter.ToUInt32(data, entry + 8);
                long offset = BitConverter.ToUInt32(data, entry + 12);
                if (length <= 0 || offset < 6 || offset + length > data.Length)
                    continue;
                if (!IsPng(data, (int)offset))
                    continue;
                if (bestOffset >= 0 && !Better(side, bestSide, size))
                    continue;
                bestOffset = (int)offset;
                bestLength = (int)length;
                bestSide = side;
            }
            if (bestOffset < 0)
                return null;

            using (MemoryStream frame = new MemoryStream(data, bestOffset, bestLength, false))
            using (Image source = Image.FromStream(frame))
            {
                Bitmap scaled = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(scaled))
                {
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, new Rectangle(0, 0, size, size));
                }
                return scaled;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    /// <summary>Prefers the smallest frame that still covers the box, and otherwise the largest.</summary>
    private static bool Better(int candidate, int current, int size)
    {
        bool candidateCovers = candidate >= size;
        if (candidateCovers != (current >= size))
            return candidateCovers;
        return candidateCovers ? candidate < current : candidate > current;
    }

    private static bool IsPng(byte[] data, int offset)
    {
        return offset + 8 <= data.Length &&
            data[offset] == 0x89 && data[offset + 1] == 0x50 &&
            data[offset + 2] == 0x4E && data[offset + 3] == 0x47;
    }
}

/// <summary>
/// The brand mark at the top of the rail: the application icon itself, drawn at whatever size the
/// box ends up. The bitmap is decoded against the control's real <see cref="Control.ClientSize"/>
/// rather than the size assigned in the constructor, because that one is logical and WinForms scales
/// it afterwards; the result is cached and rebuilt only when the box changes, never on a paint, since
/// the gallery is benchmarked for handle growth over hundreds of redraws.
///
/// The artwork already carries its own rounded tile with a brass edge, so this control paints no fill
/// and no border of its own - a second frame around the first read as a smudge at 40 px. It falls
/// back to the wordmark letter if the resource cannot be decoded, so a bad icon costs a nice touch
/// rather than the corner of the window.
/// </summary>
internal sealed class IconBadge : Control
{
    private readonly string resourceName;
    private readonly Font fallbackFont;
    private Bitmap frame;
    private Size frameSize;

    public IconBadge(string resourceName, Font fallbackFont)
    {
        this.resourceName = resourceName;
        this.fallbackFont = fallbackFont;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        TabStop = false;
    }

    /// <summary>Letter drawn when the icon resource is missing. Keeps the corner from going blank.</summary>
    public string FallbackText { get; set; }

    /// <summary>Colour of the fallback letter.</summary>
    public Color FallbackColor { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        RoundedPanel panel = Parent as RoundedPanel;
        if (panel == null)
        {
            base.OnPaintBackground(e);
            return;
        }

        // The rail is a vertical gradient, so a transparent child has to sample the exact row it sits
        // on instead of the panel's nominal fill.
        using (SolidBrush brush = new SolidBrush(panel.FillAt(Top)))
            e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int side = Math.Min(ClientSize.Width, ClientSize.Height);
        if (side <= 0)
            return;

        if (frame == null || frameSize != ClientSize)
        {
            if (frame != null)
                frame.Dispose();
            frame = AppIcon.Load(resourceName, side);
            frameSize = ClientSize;
        }

        if (frame != null)
        {
            e.Graphics.DrawImageUnscaled(frame,
                new Point((ClientSize.Width - side) / 2, (ClientSize.Height - side) / 2));
            return;
        }

        if (String.IsNullOrEmpty(FallbackText) || fallbackFont == null)
            return;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        TextRenderer.DrawText(e.Graphics, FallbackText, fallbackFont,
            new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), FallbackColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && frame != null)
        {
            frame.Dispose();
            frame = null;
        }
        base.Dispose(disposing);
    }
}

internal sealed class SkyboxScrollHost : Control
{
    private static readonly Color ScrollBackground = Color.FromArgb(27, 39, 37);
    private const int AnimationFrameMessage = 0x8000 + 0x421;
    private const uint TimePeriodic = 0x0001;
    private const uint TimeKillSynchronous = 0x0100;
    private static readonly long AnimationFrameTicks = Math.Max(1L, Stopwatch.Frequency / 144L);
    private readonly DoubleBufferedFlowPanel content;
    private readonly Timer animationTimer;
    private readonly Stopwatch animationClock;
    private readonly MultimediaTimerCallback multimediaTimerCallback;
    private bool dragging;
    private bool highResolutionTimerActive;
    private bool thumbHovered;
    private int dragOffset;
    private int animationFramePending;
    private bool layoutPending;
    private double scrollTargetTop;
    private double scrollPosition;
    private double scrollVelocity;
    private float thumbHoverAmount;
    private long nextAnimationFrame;
    private uint multimediaTimerId;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void MultimediaTimerCallback(
        uint timerId,
        uint message,
        UIntPtr user,
        UIntPtr parameter1,
        UIntPtr parameter2);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeSetEvent")]
    private static extern uint TimeSetEvent(
        uint delay,
        uint resolution,
        MultimediaTimerCallback callback,
        UIntPtr user,
        uint eventType);

    [DllImport("winmm.dll", EntryPoint = "timeKillEvent")]
    private static extern uint TimeKillEvent(uint timerId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    public DoubleBufferedFlowPanel Content
    {
        get { return content; }
    }

    public bool IsInViewport(Control control)
    {
        if (control == null || control.Parent != content || !control.Visible)
            return false;
        Rectangle bounds = new Rectangle(
            control.Left,
            control.Top + content.Top,
            control.Width,
            control.Height);
        return bounds.IntersectsWith(ClientRectangle);
    }

    /// <summary>
    /// Scrolls the smallest distance that brings a child fully into view. Used by keyboard
    /// navigation, which can otherwise move the selection to a card nobody can see.
    /// </summary>
    public void EnsureVisible(Control control)
    {
        if (control == null || control.Parent != content || !control.Visible || !CanScroll)
            return;

        double target = scrollTargetTop;
        double top = control.Top - content.Padding.Top;
        double bottom = control.Bottom + control.Margin.Bottom;
        if (top + target < 0D)
            target = -top;
        else if (bottom + target > ClientSize.Height)
            target = ClientSize.Height - bottom;

        target = ClampScrollTop(target);
        if (Math.Abs(target - scrollTargetTop) < 0.5D)
            return;
        scrollTargetTop = target;
        StartAnimation();
    }

    public SkyboxScrollHost()
    {
        SetStyle(ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.Opaque |
            ControlStyles.ResizeRedraw, true);
        BackColor = ScrollBackground;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, true);
        content = new DoubleBufferedFlowPanel();
        content.BackColor = ScrollBackground;
        content.FlowDirection = FlowDirection.LeftToRight;
        content.Location = Point.Empty;
        // Left inset trimmed against the card's own internal text inset so a card title lands on the
        // page's 24 px text axis. 1018 - 5 - 12 still leaves room for five 196 px columns.
        content.Padding = new Padding(5, 9, 12, 12);
        content.WrapContents = true;
        Controls.Add(content);

        animationClock = new Stopwatch();
        multimediaTimerCallback = OnMultimediaTimer;
        animationTimer = new Timer();
        animationTimer.Interval = 7;
        animationTimer.Tick += delegate { AnimateFrame(); };

        Resize += delegate { LayoutContent(); };
        content.ControlAdded += delegate { QueueLayoutContent(); };
        content.ControlRemoved += delegate { QueueLayoutContent(); };
        content.Layout += delegate { UpdateContentHeight(); };
        MouseWheel += OnMouseWheel;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += delegate { dragging = false; Capture = false; };
        MouseLeave += delegate
        {
            if (!dragging)
            {
                thumbHovered = false;
                StartAnimation();
            }
        };
        content.MouseWheel += OnMouseWheel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        layoutPending = false;
        LayoutContent();
        UpdateRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedRegion();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == AnimationFrameMessage)
        {
            System.Threading.Interlocked.Exchange(ref animationFramePending, 0);
            if (multimediaTimerId != 0 || animationTimer.Enabled)
                AnimateFrame();
            return;
        }
        base.WndProc(ref message);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.Style |= 0x02000000;
            return parameters;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(ScrollBackground);
    }

    public void ScrollToTop()
    {
        scrollTargetTop = 0;
        SetScrollTop(0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(ScrollBackground);
        if (!CanScroll)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF gutter = new RectangleF(Width - 17F, 5F, 13F, Math.Max(1F, Height - 10F));
        using (GraphicsPath gutterPath = UiTheme.RoundedPath(gutter, 6.5F))
        using (SolidBrush gutterBrush = new SolidBrush(Color.FromArgb(23, 34, 32)))
            e.Graphics.FillPath(gutterBrush, gutterPath);

        RectangleF track = GetTrackRectangle();
        RectangleF thumb = GetThumbRectangle();
        using (GraphicsPath trackPath = UiTheme.RoundedPath(track, track.Width / 2F))
        using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(61, 82, 73)))
            e.Graphics.FillPath(trackBrush, trackPath);

        Color thumbColor = UiTheme.Mix(Color.FromArgb(170, 122, 50), UiTheme.AccentHover, thumbHoverAmount * 0.72F);
        using (GraphicsPath thumbPath = UiTheme.RoundedPath(thumb, thumb.Width / 2F))
        using (SolidBrush thumbBrush = new SolidBrush(thumbColor))
            e.Graphics.FillPath(thumbBrush, thumbPath);
    }

    private void LayoutContent()
    {
        content.Width = Math.Max(1, ClientSize.Width - 20);
        UpdateContentHeight();
    }

    private void QueueLayoutContent()
    {
        if (layoutPending)
            return;
        layoutPending = true;
        if (!IsHandleCreated)
            return;
        BeginInvoke(new MethodInvoker(delegate
        {
            if (IsDisposed || Disposing)
                return;
            layoutPending = false;
            LayoutContent();
        }));
    }

    private void UpdateContentHeight()
    {
        int bottom = 0;
        foreach (Control control in content.Controls)
        {
            if (control.Visible)
                bottom = Math.Max(bottom, control.Bottom + control.Margin.Bottom);
        }
        int height = Math.Max(ClientSize.Height, bottom + content.Padding.Bottom);
        if (content.Height != height)
            content.Height = height;
        ClampScroll();
        Invalidate();
    }

    private void OnMouseWheel(object sender, MouseEventArgs e)
    {
        if (!CanScroll)
            return;
        scrollTargetTop = ClampScrollTop(scrollTargetTop + (e.Delta * 0.80D));
        StartAnimation();
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !CanScroll)
            return;
        RectangleF thumb = GetThumbRectangle();
        if (thumb.Contains(e.Location))
        {
            dragging = true;
            dragOffset = e.Y - (int)thumb.Y;
            Capture = true;
            return;
        }

        RectangleF track = GetTrackRectangle();
        RectangleF scrollHitArea = new RectangleF(Width - 20F, 0F, 20F, Height);
        if (scrollHitArea.Contains(e.Location))
        {
            ScrollThumbTo(e.Y - ((int)thumb.Height / 2));
            dragging = true;
            dragOffset = (int)(thumb.Height / 2F);
            Capture = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!CanScroll)
            return;
        if (dragging)
            ScrollThumbTo(e.Y - dragOffset);
        bool hover = GetThumbRectangle().Contains(e.Location);
        if (hover != thumbHovered)
        {
            thumbHovered = hover;
            StartAnimation();
        }
    }

    private void ScrollThumbTo(int thumbTop)
    {
        RectangleF track = GetTrackRectangle();
        RectangleF thumb = GetThumbRectangle();
        float travel = Math.Max(1F, track.Height - thumb.Height);
        float position = Math.Max(0F, Math.Min(travel, thumbTop - track.Y));
        float ratio = position / travel;
        scrollTargetTop = ClampScrollTop(-(ratio * MaxScroll));
        SetScrollTop(scrollTargetTop);
    }

    private void ClampScroll()
    {
        scrollTargetTop = ClampScrollTop(scrollTargetTop);
        SetScrollTop(content.Top);
    }

    private double ClampScrollTop(double top)
    {
        return CanScroll ? Math.Max(-MaxScroll, Math.Min(0, top)) : 0;
    }

    private void SetScrollTop(double top)
    {
        double clamped = ClampScrollTop(top);
        scrollPosition = clamped;
        scrollVelocity = 0D;
        RenderScrollTop((int)Math.Round(clamped));
    }

    private bool RenderScrollTop(int top)
    {
        int clamped = (int)Math.Round(ClampScrollTop(top));
        if (content.Top == clamped)
            return false;
        content.Top = clamped;
        Invalidate(new Rectangle(Math.Max(0, Width - 14), 0, Math.Min(14, Width), Height), false);
        return true;
    }

    private static double SmoothDamp(
        double current,
        double target,
        ref double velocity,
        double smoothTime,
        double deltaTime)
    {
        double omega = 2D / Math.Max(0.01D, smoothTime);
        double step = omega * deltaTime;
        double decay = 1D / (1D + step + (0.48D * step * step) + (0.235D * step * step * step));
        double change = current - target;
        double temporary = (velocity + (omega * change)) * deltaTime;
        velocity = (velocity - (omega * temporary)) * decay;
        return target + ((change + temporary) * decay);
    }

    private void AnimateFrame()
    {
        double frameSeconds = animationClock.IsRunning
            ? animationClock.Elapsed.TotalSeconds
            : (1D / 144D);
        animationClock.Restart();
        frameSeconds = Math.Max(1D / 500D, Math.Min(1D / 30D, frameSeconds));

        float hoverBlend = (float)(1D - Math.Exp(-22D * frameSeconds));
        thumbHoverAmount += ((thumbHovered ? 1F : 0F) - thumbHoverAmount) * hoverBlend;

        double distance = scrollTargetTop - scrollPosition;
        if (Math.Abs(distance) <= 0.2D && Math.Abs(scrollVelocity) <= 2D)
        {
            scrollPosition = scrollTargetTop;
            scrollVelocity = 0D;
        }
        else
        {
            scrollPosition = SmoothDamp(
                scrollPosition,
                scrollTargetTop,
                ref scrollVelocity,
                0.10D,
                frameSeconds);
        }
        RenderScrollTop((int)Math.Round(scrollPosition));

        Invalidate(new Rectangle(Math.Max(0, Width - 20), 0, Math.Min(20, Width), Height), false);
        if (Math.Abs(thumbHoverAmount - (thumbHovered ? 1F : 0F)) < 0.01F &&
            Math.Abs(scrollTargetTop - scrollPosition) <= 0.2D &&
            Math.Abs(scrollVelocity) <= 2D)
            StopAnimation();
    }

    private void OnMultimediaTimer(
        uint timerId,
        uint message,
        UIntPtr user,
        UIntPtr parameter1,
        UIntPtr parameter2)
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
            return;

        long now = Stopwatch.GetTimestamp();
        while (true)
        {
            long scheduled = System.Threading.Interlocked.Read(ref nextAnimationFrame);
            if (scheduled > now)
                return;
            long next = scheduled <= 0L ? now + AnimationFrameTicks : scheduled + AnimationFrameTicks;
            if (next <= now)
                next = now + AnimationFrameTicks;
            if (System.Threading.Interlocked.CompareExchange(ref nextAnimationFrame, next, scheduled) == scheduled)
                break;
        }

        if (System.Threading.Interlocked.Exchange(ref animationFramePending, 1) != 0)
            return;
        if (!PostMessage(Handle, AnimationFrameMessage, IntPtr.Zero, IntPtr.Zero))
            System.Threading.Interlocked.Exchange(ref animationFramePending, 0);
    }

    private void StartAnimation()
    {
        if (multimediaTimerId != 0 || animationTimer.Enabled)
            return;
        if (!highResolutionTimerActive)
        {
            highResolutionTimerActive = TimeBeginPeriod(1) == 0;
        }
        animationClock.Restart();
        System.Threading.Interlocked.Exchange(
            ref nextAnimationFrame,
            Stopwatch.GetTimestamp() + AnimationFrameTicks);
        multimediaTimerId = TimeSetEvent(
            1,
            1,
            multimediaTimerCallback,
            UIntPtr.Zero,
            TimePeriodic | TimeKillSynchronous);
        if (multimediaTimerId == 0)
            animationTimer.Start();
    }

    private void StopAnimation()
    {
        uint timerId = multimediaTimerId;
        multimediaTimerId = 0;
        if (timerId != 0)
            TimeKillEvent(timerId);
        animationTimer.Stop();
        animationClock.Reset();
        System.Threading.Interlocked.Exchange(ref animationFramePending, 0);
        System.Threading.Interlocked.Exchange(ref nextAnimationFrame, 0L);
        if (highResolutionTimerActive)
        {
            TimeEndPeriod(1);
            highResolutionTimerActive = false;
        }
    }

    private RectangleF GetTrackRectangle()
    {
        return new RectangleF(Width - 12F, 10F, 4F, Math.Max(1F, Height - 20F));
    }

    private RectangleF GetThumbRectangle()
    {
        RectangleF track = GetTrackRectangle();
        float ratio = Math.Min(1F, (float)ClientSize.Height / Math.Max(1, content.Height));
        float height = Math.Max(44F, track.Height * ratio);
        float progress = MaxScroll == 0 ? 0F : (float)(-content.Top) / MaxScroll;
        float top = track.Y + ((track.Height - height) * progress);
        return new RectangleF(track.X - 1.5F, top, track.Width + 3F, height);
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
            return;
        using (GraphicsPath path = UiTheme.RoundedPath(
            new RectangleF(0, 0, Width, Height), 12F))
        {
            Region previous = Region;
            Region = new Region(path);
            if (previous != null)
                previous.Dispose();
        }
    }

    private bool CanScroll
    {
        get { return content.Height > ClientSize.Height; }
    }

    private int MaxScroll
    {
        get { return Math.Max(0, content.Height - ClientSize.Height); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAnimation();
            animationTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class CircularLoader : Control
{
    private readonly Timer timer;
    private float angle;

    public CircularLoader()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.BackgroundSoft;
        Size = new Size(74, 74);
        timer = new Timer();
        timer.Interval = 15;
        timer.Tick += delegate
        {
            angle = (angle + 5.8F) % 360F;
            Invalidate();
        };
        timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        float stroke = Math.Max(5F, Math.Min(Width, Height) * 0.085F);
        RectangleF ring = new RectangleF(
            stroke,
            stroke,
            Math.Max(1F, Width - (stroke * 2F)),
            Math.Max(1F, Height - (stroke * 2F)));

        using (Pen track = new Pen(UiTheme.Alpha(UiTheme.Border, 90), stroke))
            e.Graphics.DrawEllipse(track, ring);
        using (Pen glow = new Pen(UiTheme.Alpha(UiTheme.AccentHover, 50), stroke + 5F))
        using (Pen arc = new Pen(UiTheme.AccentHover, stroke))
        {
            glow.StartCap = LineCap.Round;
            glow.EndCap = LineCap.Round;
            arc.StartCap = LineCap.Round;
            arc.EndCap = LineCap.Round;
            e.Graphics.DrawArc(glow, ring, angle, 104F);
            e.Graphics.DrawArc(arc, ring, angle, 104F);
        }

        RectangleF core = new RectangleF(Width / 2F - 4F, Height / 2F - 4F, 8F, 8F);
        using (SolidBrush coreBrush = new SolidBrush(UiTheme.Cyan))
            e.Graphics.FillEllipse(coreBrush, core);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            timer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class PermissionRequestForm : Form
{
    private readonly Timer requestTimer;
    private readonly Timer fadeTimer;

    public PermissionRequestForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.BackgroundSoft;
        ClientSize = new Size(320, 230);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0D;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Deadlock Skybox Selector";
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        RoundedPanel frame = new RoundedPanel();
        frame.BorderColor = UiTheme.Border;
        frame.CornerRadius = 24;
        frame.Dock = DockStyle.Fill;
        frame.DrawTopGlow = true;
        frame.FillColor = UiTheme.BackgroundSoft;
        Controls.Add(frame);

        CircularLoader loader = new CircularLoader();
        loader.Location = new Point((ClientSize.Width - loader.Width) / 2, 34);
        frame.Controls.Add(loader);

        Label title = new Label();
        title.BackColor = Color.Transparent;
        title.Font = UiTheme.Heavy(14F);
        title.ForeColor = UiTheme.Text;
        title.Location = new Point(18, 125);
        title.Size = new Size(284, 32);
        title.Text = "Preparing secure access";
        title.TextAlign = ContentAlignment.MiddleCenter;
        frame.Controls.Add(title);

        Label detail = new Label();
        detail.BackColor = Color.Transparent;
        detail.Font = UiTheme.Font(8F, FontStyle.Regular);
        detail.ForeColor = UiTheme.TextMuted;
        detail.Location = new Point(20, 164);
        detail.Size = new Size(280, 38);
        detail.Text = "Approve the Windows permission request to continue";
        detail.TextAlign = ContentAlignment.TopCenter;
        frame.Controls.Add(detail);

        fadeTimer = new Timer();
        fadeTimer.Interval = 15;
        fadeTimer.Tick += delegate
        {
            Opacity = Math.Min(1D, Opacity + 0.16D);
            if (Opacity >= 1D)
                fadeTimer.Stop();
        };

        requestTimer = new Timer();
        requestTimer.Interval = 260;
        requestTimer.Tick += delegate
        {
            requestTimer.Stop();
            Close();
        };
        Shown += delegate
        {
            UpdateRoundedRegion(24);
            fadeTimer.Start();
            requestTimer.Start();
        };
        Resize += delegate { UpdateRoundedRegion(24); };
    }

    private void UpdateRoundedRegion(int radius)
    {
        using (GraphicsPath path = UiTheme.RoundedPath(
            new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height)), radius))
        {
            Region previous = Region;
            Region = new Region(path);
            if (previous != null)
                previous.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            requestTimer.Dispose();
            fadeTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class FirstRunInstallForm : Form
{
    private readonly Timer fadeTimer;

    public FirstRunInstallForm(string deadlockRoot)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.BackgroundSoft;
        ClientSize = new Size(500, 380);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0D;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Deadlock Skybox Selector";
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        RoundedPanel frame = CreateStartupFrame();
        Controls.Add(frame);

        CircularLoader loader = new CircularLoader();
        loader.Location = new Point((ClientSize.Width - loader.Width) / 2, 38);
        frame.Controls.Add(loader);

        Label title = CreateCenteredLabel("First-time setup", 16F, UiTheme.Text, 124, 34);
        frame.Controls.Add(title);

        Label detail = CreateCenteredLabel(
            "Install the verified skybox library and required GameInfo component?",
            9F,
            UiTheme.TextMuted,
            162,
            44);
        frame.Controls.Add(detail);

        Label path = CreateCenteredLabel(deadlockRoot, 8F, UiTheme.TextDim, 210, 42);
        path.AutoEllipsis = true;
        frame.Controls.Add(path);

        Label permission = CreateCenteredLabel(
            "Windows will ask for administrator permission before any files are changed.",
            8F,
            UiTheme.TextDim,
            251,
            28);
        frame.Controls.Add(permission);

        ActionButton cancel = new ActionButton();
        cancel.Location = new Point(88, 305);
        cancel.Size = new Size(146, 44);
        cancel.Text = "Cancel";
        cancel.Tone = ActionButtonTone.Restore;
        cancel.Click += delegate
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        frame.Controls.Add(cancel);

        ActionButton install = new ActionButton();
        install.Location = new Point(266, 305);
        install.Size = new Size(146, 44);
        install.Text = "Install";
        install.Tone = ActionButtonTone.Apply;
        install.Click += delegate
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        frame.Controls.Add(install);

        KeyPreview = true;
        KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        Shown += delegate { UpdateRoundedRegion(24); };
        Resize += delegate { UpdateRoundedRegion(24); };

        fadeTimer = new Timer();
        fadeTimer.Interval = 15;
        fadeTimer.Tick += delegate
        {
            Opacity = Math.Min(1D, Opacity + 0.10D);
            if (Opacity >= 1D)
                fadeTimer.Stop();
        };
        Shown += delegate { fadeTimer.Start(); };
    }

    private RoundedPanel CreateStartupFrame()
    {
        RoundedPanel frame = new RoundedPanel();
        frame.BorderColor = UiTheme.Border;
        frame.CornerRadius = 24;
        frame.Dock = DockStyle.Fill;
        frame.DrawTopGlow = true;
        frame.FillColor = UiTheme.BackgroundSoft;
        return frame;
    }

    private Label CreateCenteredLabel(string text, float size, Color color, int top, int height)
    {
        Label label = new Label();
        label.BackColor = Color.Transparent;
        label.Font = size >= 14F ? UiTheme.Heavy(size) : UiTheme.Font(size, FontStyle.Regular);
        label.ForeColor = color;
        label.Location = new Point(28, top);
        label.Size = new Size(ClientSize.Width - 56, height);
        label.Text = text;
        label.TextAlign = ContentAlignment.TopCenter;
        return label;
    }

    private void UpdateRoundedRegion(int radius)
    {
        using (GraphicsPath path = UiTheme.RoundedPath(
            new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height)), radius))
        {
            Region previous = Region;
            Region = new Region(path);
            if (previous != null)
                previous.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            fadeTimer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class PreparationForm : Form
{
    private readonly Action work;
    private readonly BackgroundWorker worker;
    private readonly Timer fadeTimer;
    private readonly Stopwatch visibleTime;
    private Timer closeDelay;
    private bool mayClose;

    public Exception WorkError { get; private set; }

    public PreparationForm(Action work, string titleText, string detailText)
    {
        this.work = work;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.BackgroundSoft;
        ClientSize = new Size(380, 280);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0D;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Deadlock Skybox Selector";
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        RoundedPanel frame = new RoundedPanel();
        frame.BorderColor = UiTheme.Border;
        frame.CornerRadius = 24;
        frame.Dock = DockStyle.Fill;
        frame.DrawTopGlow = true;
        frame.FillColor = UiTheme.BackgroundSoft;
        Controls.Add(frame);

        CircularLoader loader = new CircularLoader();
        loader.Location = new Point((ClientSize.Width - loader.Width) / 2, 42);
        frame.Controls.Add(loader);

        Label title = new Label();
        title.Font = UiTheme.Heavy(17F);
        title.ForeColor = UiTheme.Text;
        title.Location = new Point(20, 134);
        title.Size = new Size(340, 34);
        title.Text = titleText;
        title.TextAlign = ContentAlignment.MiddleCenter;
        title.BackColor = Color.Transparent;
        frame.Controls.Add(title);

        Label detail = new Label();
        detail.Font = UiTheme.Font(9F, FontStyle.Regular);
        detail.ForeColor = UiTheme.TextMuted;
        detail.Location = new Point(24, 173);
        detail.Size = new Size(332, 42);
        detail.Text = detailText;
        detail.TextAlign = ContentAlignment.TopCenter;
        detail.BackColor = Color.Transparent;
        frame.Controls.Add(detail);

        Label author = new Label();
        author.Font = UiTheme.Heavy(7F);
        author.ForeColor = UiTheme.TextDim;
        author.Location = new Point(20, 236);
        author.Size = new Size(340, 20);
        author.Text = UiTheme.Track("MADE BY HARRITON");
        author.TextAlign = ContentAlignment.MiddleCenter;
        author.BackColor = Color.Transparent;
        frame.Controls.Add(author);

        worker = new BackgroundWorker();
        worker.DoWork += delegate { this.work(); };
        worker.RunWorkerCompleted += OnWorkCompleted;
        visibleTime = new Stopwatch();
        fadeTimer = new Timer();
        fadeTimer.Interval = 15;
        fadeTimer.Tick += delegate
        {
            Opacity = Math.Min(1D, Opacity + 0.09D);
            if (Opacity >= 1D)
                fadeTimer.Stop();
        };
        Shown += delegate
        {
            UpdateRoundedRegion(24);
            visibleTime.Start();
            fadeTimer.Start();
            worker.RunWorkerAsync();
        };
        Resize += delegate { UpdateRoundedRegion(24); };
        FormClosing += OnFormClosing;
    }

    private void OnWorkCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        WorkError = e.Error;
        int delay = Math.Max(0, 420 - (int)visibleTime.ElapsedMilliseconds);
        if (delay == 0)
        {
            FinishAndClose();
            return;
        }

        closeDelay = new Timer();
        closeDelay.Interval = delay;
        closeDelay.Tick += delegate
        {
            closeDelay.Stop();
            closeDelay.Dispose();
            FinishAndClose();
        };
        closeDelay.Start();
    }

    private void FinishAndClose()
    {
        mayClose = true;
        Close();
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (!mayClose && worker.IsBusy)
            e.Cancel = true;
    }

    private void UpdateRoundedRegion(int radius)
    {
        using (GraphicsPath path = UiTheme.RoundedPath(
            new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height)), radius))
        {
            Region previous = Region;
            Region = new Region(path);
            if (previous != null)
                previous.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            fadeTimer.Dispose();
            if (closeDelay != null)
                closeDelay.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// A borderless modal that shows one preview as large as the screen allows. The gallery cards are
/// small by necessity, so the only way to judge a skybox before applying it is a full-size look.
/// Any click, any key and losing focus close it, because a window with no chrome must never be
/// able to trap the user.
/// </summary>
internal sealed class PreviewForm : Form
{
    private readonly Image image;

    public PreviewForm(string title, string imagePath)
    {
        image = Image.FromFile(imagePath);
        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        float fit = Math.Min(
            Math.Min(work.Width * 0.82F / image.Width, work.Height * 0.82F / image.Height),
            1F);
        int width = Math.Max(320, (int)Math.Round(image.Width * fit));
        int height = Math.Max(200, (int)Math.Round(image.Height * fit));

        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiTheme.BackgroundSoft;
        ClientSize = new Size(width, height + 46);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = title;

        PictureBox canvas = new PictureBox();
        canvas.BackColor = UiTheme.Background;
        canvas.Bounds = new Rectangle(0, 0, width, height);
        canvas.Image = image;
        canvas.SizeMode = PictureBoxSizeMode.Zoom;
        Controls.Add(canvas);

        Label caption = new Label();
        caption.BackColor = UiTheme.BackgroundSoft;
        caption.Bounds = new Rectangle(0, height, width, 46);
        caption.Font = UiTheme.Font(9.5F, FontStyle.Regular);
        caption.ForeColor = UiTheme.TextMuted;
        caption.Text = title + "   \u00B7   click anywhere to close";
        caption.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(caption);

        canvas.Click += delegate { Close(); };
        caption.Click += delegate { Close(); };
        Click += delegate { Close(); };
        KeyDown += delegate { Close(); };
        Deactivate += delegate { Close(); };
        Shown += delegate { UpdateRoundedRegion(); };
    }

    private void UpdateRoundedRegion()
    {
        using (GraphicsPath path = UiTheme.RoundedPath(
            new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height)), 16F))
        {
            Region previous = Region;
            Region = new Region(path);
            if (previous != null)
                previous.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && image != null)
            image.Dispose();
    }
}

internal sealed class SkyboxManifest
{
    public int formatVersion { get; set; }
    public SkyboxVariant[] variants { get; set; }
    public SkyboxOverride baseVeilOverride { get; set; }
    public SkyboxOverride factorySmokeOverride { get; set; }
    public SkyboxOverride hideNamesOverride { get; set; }
    public SkyboxOverride hidePickupBookOverride { get; set; }
    public SkyboxOverride hidePlayerMarkersOverride { get; set; }
    public SkyboxOverride hideGravesMarkersOverride { get; set; }
    public SkyboxOverride hideCombinedMarkersOverride { get; set; }
    public SkyboxOverride hideHealthLinesOverride { get; set; }
    public SkyboxOverride classicAbilityFillOverride { get; set; }
    public SkyboxOverride colorFixOverride { get; set; }
}

internal sealed class SkyboxOverride
{
    public string entry { get; set; }
    public long bytes { get; set; }
    public string sha256 { get; set; }
}

internal sealed class SkyboxVariant
{
    public string id { get; set; }
    public string category { get; set; }
    public string displayName { get; set; }
    public string preview { get; set; }
    public string entry { get; set; }
    public long bytes { get; set; }
    public string sha256 { get; set; }
    public string legacySha256 { get; set; }
}

internal static class SkyboxNames
{
    /// <summary>
    /// Resolves the label of a variant. The names live in source\skyboxes.json and are copied into
    /// the shipped manifest by tools\sync-skybox-names.ps1, which tools\verify.ps1 enforces, so the
    /// interface keeps no table of its own to drift out of date. An id with no name degrades to a
    /// readable form of itself instead of a raw identifier.
    /// </summary>
    public static string Get(SkyboxVariant variant)
    {
        if (variant == null)
            return "Original Deadlock";
        if (!String.IsNullOrWhiteSpace(variant.displayName))
            return variant.displayName.Trim();
        return Humanize(variant.id);
    }

    private static string Humanize(string id)
    {
        if (String.IsNullOrWhiteSpace(id))
            return "Skybox";
        StringBuilder text = new StringBuilder();
        foreach (string part in id.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (text.Length > 0)
                text.Append(' ');
            text.Append(Char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                text.Append(part.Substring(1));
        }
        return text.Length == 0 ? "Skybox" : text.ToString();
    }
}

internal sealed class OperationResult
{
    public int ExitCode;
    public string Output;

    public bool Success
    {
        get { return ExitCode == 0; }
    }
}

internal sealed class SelectorStatus
{
    public string CurrentSelection = "vanilla";
    public bool NeedsUpdate;
    public bool BaseVeilHidden;
    public bool BaseVeilSlotOccupied;
    public bool FactorySmokeHidden;
    public bool FactorySmokeSlotOccupied;
    public bool NamesHidden;
    public bool NamesSlotOccupied;
    public bool BookHidden;
    public bool BookSlotOccupied;
    public bool PlayerMarkersHidden;
    public bool GravesMarkersHidden;
    public bool PlayerMarkersSlotOccupied;
    public bool HealthLinesHidden;
    public bool HealthLinesSlotOccupied;
    public bool ClassicFillEnabled;
    public bool ClassicFillNeedsUpdate;
    public bool ClassicFillSlotOccupied;
    public bool ColorFixEnabled;
    public bool ColorFixSlotOccupied;
    public bool AddonsMounted;
    public bool UnknownFiles;
    public string Detail;
}

/// <summary>
/// Remembers the SHA-256 of the active skybox package together with the file identity it was
/// computed from, so the status read can skip re-hashing an unchanged package.
/// </summary>
internal sealed class StatusHashEntry
{
    public string path { get; set; }
    public long length { get; set; }
    public long creationUtcTicks { get; set; }
    public long lastWriteUtcTicks { get; set; }
    public string sha256 { get; set; }

    public bool Matches(StatusHashEntry other)
    {
        return other != null &&
            String.Equals(path, other.path, StringComparison.OrdinalIgnoreCase) &&
            length == other.length &&
            creationUtcTicks == other.creationUtcTicks &&
            lastWriteUtcTicks == other.lastWriteUtcTicks;
    }
}

internal sealed class SelectorForm : Form
{
    public const int LayoutWidth = 1200;
    public const int LayoutHeight = 790;
    // One text axis for the whole content column. Every panel edge sits at x = 0 of the column and
    // every piece of copy inside one starts this far in, so the page title, the status line, the tab
    // labels, the card titles and the body text on the other three pages all share a single edge.
    private const int Gutter = 24;
    // Extra pixels between rows of body copy. The display face the interface runs on declares a line
    // box only a few pixels taller than its capitals, which reads as a solid block once a paragraph
    // is more than two rows deep, so prose sets its leading explicitly. See ProseLabel.
    private const int ProseLeading = 6;
    private const string StatusHashFileName = ".status-hash-v1.json";

    // Painted text needs a font that outlives the paint call. Creating one per WM_PAINT would hand
    // GDI a fresh HFONT on every frame and leave it to the finalizer to give back.
    private static readonly Font PageTitleFont = UiTheme.Heavy(15.5F);
    private static readonly Font PageSubtitleFont = UiTheme.Font(8.5F, FontStyle.Regular);
    private static readonly Font BadgeFont = UiTheme.Heavy(15F);

    // Line box of a page title, measured once in the face that paints it. The heading row and the
    // brass tick are both derived from it, so a change of face or of point size cannot leave the
    // title clipped at the bottom of its row or the marker beside it out of register.
    private static readonly int PageTitleHeight =
        TextRenderer.MeasureText("SKYBOXES", PageTitleFont, new Size(600, 90),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;

    /// <summary>Heading row of a page that carries a title and a line of explanation under it.</summary>
    private const int PageHeaderHeight = 58;

    /// <summary>Heading row of a page whose title stands on its own. See BuildPageHeader.</summary>
    private const int CompactPageHeaderHeight = 46;

    // Measured Label text insets, keyed by face. See TextInset.
    private static readonly Dictionary<string, int> TextInsets = new Dictionary<string, int>();

    // Every helper is launched hidden, so a hung child process would leave the interface in
    // "Working" forever. The selector budget also has to cover the wait the script itself
    // performs for Deadlock to close.
    private const int GameInfoTimeoutMilliseconds = 60000;
    private const int SelectorTimeoutMilliseconds = 240000;

    private readonly string runtimeRoot;
    private readonly string deadlockRoot;
    private readonly string cacheRoot;
    private readonly string assetHash;
    private readonly Dictionary<string, SkyboxCard> cards;
    private readonly Dictionary<string, SkyboxVariant> variantsByHash;
    private readonly List<SkyboxVariant> variants;
    private readonly List<SkyboxCard> revealCards;
    private readonly Timer entranceTimer;
    private readonly Timer revealTimer;

    private FlowLayoutPanel cardGrid;
    private SkyboxScrollHost cardScroll;
    private DashboardCanvas dashboard;
    private Panel titleBar;
    private Label statusTitle;
    private Label statusDetail;
    private Label selectionTitle;
    private Label selectionDetail;
    private StatusDot statusDot;
    private ActionButton installButton;
    private ActionButton applyButton;
    private ActionButton restoreButton;
    private FixToggle veilSwitch;
    private FixToggle smokeSwitch;
    private FixToggle namesSwitch;
    private FixToggle bookSwitch;
    private FixToggle playerMarkersSwitch;
    private FixToggle gravesMarkersSwitch;
    private FixToggle healthLinesSwitch;
    private FixToggle classicFillSwitch;
    private FixToggle colorFixSwitch;
    private Label veilStateLabel;
    private Label smokeStateLabel;
    private Label namesStateLabel;
    private Label bookStateLabel;
    private Label playerMarkersStateLabel;
    private Label gravesMarkersStateLabel;
    private Label healthLinesStateLabel;
    private Label classicFillStateLabel;
    private Label colorFixStateLabel;
    private TextBox searchBox;
    private Label emptyLabel;
    private ToolTip cardTips;
    private ToolTip railTips;
    private Panel pageHost;
    private Panel[] pages;
    private RailButton[] railButtons;
    private int activePage;
    private string searchQuery = "";
    private SkyboxVariant selectedVariant;
    private string currentSelection = "vanilla";
    private bool currentNeedsUpdate;
    private bool baseVeilHidden;
    private bool baseVeilSlotOccupied;
    private string baseVeilOverrideHash = "";
    private bool factorySmokeHidden;
    private bool factorySmokeSlotOccupied;
    private string factorySmokeOverrideHash = "";
    private bool namesHidden;
    private bool namesSlotOccupied;
    private string hideNamesOverrideHash = "";
    private bool bookHidden;
    private bool bookSlotOccupied;
    private string hidePickupBookOverrideHash = "";
    private bool playerMarkersHidden;
    private bool gravesMarkersHidden;
    private bool playerMarkersSlotOccupied;
    private string hidePlayerMarkersOverrideHash = "";
    private string hideGravesMarkersOverrideHash = "";
    private string hideCombinedMarkersOverrideHash = "";
    private bool healthLinesHidden;
    private bool healthLinesSlotOccupied;
    private string hideHealthLinesOverrideHash = "";
    private bool classicFillEnabled;
    private bool classicFillNeedsUpdate;
    private bool classicFillSlotOccupied;
    private string classicAbilityFillOverrideHash = "";
    private bool colorFixEnabled;
    private bool colorFixSlotOccupied;
    private string colorFixOverrideHash = "";
    private bool working;
    private bool addonsMounted;
    private int revealIndex;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageText(IntPtr window, int message, IntPtr wParam, string lParam);

    /// <summary>
    /// Installs the dimmed prompt that the edit control paints itself (EM_SETCUEBANNER). A label
    /// placed over the box would lose to the native control, and the message only sticks once the
    /// control owns a handle, so it is deferred when the field is built before the form is shown.
    /// </summary>
    private static void ApplyCueBanner(TextBox box, string prompt)
    {
        if (box.IsHandleCreated)
        {
            SendMessageText(box.Handle, 0x1501, new IntPtr(1), prompt);
            return;
        }
        box.HandleCreated += delegate { SendMessageText(box.Handle, 0x1501, new IntPtr(1), prompt); };
    }

    public SelectorForm(string runtimeRoot, string deadlockRoot, string cacheRoot, string assetHash)
    {
        this.runtimeRoot = runtimeRoot;
        this.deadlockRoot = deadlockRoot;
        this.cacheRoot = cacheRoot;
        this.assetHash = assetHash;
        cards = new Dictionary<string, SkyboxCard>(StringComparer.OrdinalIgnoreCase);
        variants = LoadManifest();
        revealCards = new List<SkyboxCard>();
        variantsByHash = new Dictionary<string, SkyboxVariant>(StringComparer.OrdinalIgnoreCase);
        foreach (SkyboxVariant variant in variants)
        {
            variantsByHash[variant.sha256] = variant;
            if (!String.IsNullOrWhiteSpace(variant.legacySha256))
                variantsByHash[variant.legacySha256] = variant;
        }

        AutoScaleDimensions = UiScale.AutoScaleDimensions;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Background;
        ClientSize = new Size(LayoutWidth, LayoutHeight);
        MaximumSize = new Size(LayoutWidth, LayoutHeight);
        MinimumSize = new Size(LayoutWidth, LayoutHeight);
        DoubleBuffered = true;
        Font = UiTheme.Font(9F, FontStyle.Regular);
        ForeColor = UiTheme.Text;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0D;
        Padding = Padding.Empty;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Deadlock Skybox Selector";
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        BuildInterface();
        BuildCards();
        EnableWindowDragging(dashboard);
        entranceTimer = new Timer();
        entranceTimer.Interval = 15;
        entranceTimer.Tick += AnimateEntrance;
        revealTimer = new Timer();
        revealTimer.Interval = 34;
        revealTimer.Tick += RevealNextCard;
        Shown += delegate
        {
            UpdateRoundedRegion();
            entranceTimer.Start();
            StartCardReveal();
            RefreshStatusAsync();
            WarmPages();
        };
        Resize += delegate { UpdateRoundedRegion(); };
        FormClosed += delegate { DisposeCardImages(); };
    }

    private void BuildInterface()
    {
        dashboard = new DashboardCanvas();
        dashboard.Dock = DockStyle.Fill;
        dashboard.Padding = new Padding(10, 0, 10, 10);
        Controls.Add(dashboard);

        titleBar = BuildTitleBar();
        dashboard.Controls.Add(titleBar);

        TableLayoutPanel shell = new TableLayoutPanel();
        shell.BackColor = Color.Transparent;
        shell.ColumnCount = 2;
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.Dock = DockStyle.Fill;
        shell.Margin = Padding.Empty;
        shell.Padding = Padding.Empty;
        shell.RowCount = 1;
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        dashboard.Controls.Add(shell);
        shell.BringToFront();
        titleBar.BringToFront();

        shell.Controls.Add(BuildRail(), 0, 0);

        TableLayoutPanel content = new TableLayoutPanel();
        content.BackColor = Color.Transparent;
        content.ColumnCount = 1;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.Dock = DockStyle.Fill;
        // The top inset is a band for the window buttons, which float over the whole canvas at the
        // top right. The status strip carries right-aligned text, so it has to start below them.
        content.Padding = new Padding(24, 52, 26, 20);
        content.RowCount = 2;
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.Controls.Add(content, 1, 0);

        // The status strip lives in the shell rather than on a page: SetWorking writes progress
        // into it, and an operation started from the FPS or GameInfo page has to report somewhere.
        content.Controls.Add(BuildStatusPanel(), 0, 0);

        pageHost = new Panel();
        pageHost.BackColor = Color.Transparent;
        pageHost.Dock = DockStyle.Fill;
        pageHost.Margin = new Padding(0, 10, 0, 0);
        content.Controls.Add(pageHost, 0, 1);

        // Every page is built up front. The state methods (ApplyStatus,
        // SetWorking, UpdateButtons) then never see a missing control and need no page awareness.
        pages = new Panel[4];
        pages[0] = BuildSkyboxesPage();
        pages[1] = BuildFixesPage();
        pages[2] = BuildGameInfoPage();
        pages[3] = BuildSettingsPage();
        for (int index = 0; index < pages.Length; index++)
        {
            pages[index].Dock = DockStyle.Fill;
            pages[index].Visible = false;
            pageHost.Controls.Add(pages[index]);
        }

        ShowPage(0);
    }

    /// <summary>
    /// Icon-only navigation column. It replaces the old text sidebar so new sections can be added
    /// without redesigning the shell; the filters it used to hold now sit on the Skyboxes page.
    /// </summary>
    private Panel BuildRail()
    {
        RoundedPanel rail = new RoundedPanel();
        rail.BackColor = Color.Transparent;
        rail.BorderColor = UiTheme.BorderSoft;
        rail.CornerRadius = 22;
        rail.Dock = DockStyle.Fill;
        rail.FillColor = UiTheme.Sidebar;
        rail.GradientColor = Color.FromArgb(24, 35, 32);
        rail.Margin = new Padding(0, 10, 0, 10);
        rail.Padding = new Padding(8, 18, 8, 18);

        railTips = new ToolTip();
        railTips.InitialDelay = 380;
        railTips.ReshowDelay = 140;

        // The wordmark left with the old text sidebar, so the icon is now the only place the
        // application names itself inside its own window; the tooltip spells the name out for anyone
        // who wants it. No plate is drawn behind it - the artwork brings its own brass-edged tile.
        IconBadge badge = new IconBadge("SkyboxSelector.Payload.app.ico", BadgeFont);
        badge.FallbackColor = UiTheme.AccentHover;
        badge.FallbackText = "D";
        badge.Location = new Point(16, 18);
        badge.Size = new Size(40, 40);
        rail.Controls.Add(badge);
        railTips.SetToolTip(badge, "Deadlock Skybox Selector");

        Panel brandRule = new Panel();
        brandRule.BackColor = UiTheme.Alpha(UiTheme.Brass, 130);
        brandRule.Location = new Point(18, 74);
        brandRule.Size = new Size(36, 1);
        rail.Controls.Add(brandRule);

        railButtons = new RailButton[4];
        railButtons[0] = CreateRailButton(RailIcon.Skyboxes, "Skyboxes", 96, 0);
        railButtons[1] = CreateRailButton(RailIcon.Fixes, "Fixes", 160, 1);
        railButtons[2] = CreateRailButton(RailIcon.GameInfo, "GameInfo", 224, 2);
        railButtons[3] = CreateRailButton(RailIcon.Settings, "Settings", 288, 3);
        for (int index = 0; index < railButtons.Length; index++)
            rail.Controls.Add(railButtons[index]);

        Panel footRule = new Panel();
        footRule.BackColor = UiTheme.Alpha(UiTheme.Brass, 130);
        footRule.Location = new Point(18, 300);
        footRule.Size = new Size(36, 1);
        rail.Controls.Add(footRule);

        // Settings is measured up from the bottom edge, the same way the old sidebar footer was,
        // so the top group keeps room to grow.
        RailButton settingsButton = railButtons[3];
        EventHandler layout = delegate
        {
            settingsButton.Top = Math.Max(railButtons[2].Bottom + 24, rail.ClientSize.Height - 74);
            footRule.Top = settingsButton.Top - 17;
        };
        rail.Resize += layout;
        layout(rail, EventArgs.Empty);
        return rail;
    }

    private RailButton CreateRailButton(RailIcon icon, string label, int top, int index)
    {
        RailButton button = new RailButton();
        button.AccessibleName = label;
        button.Icon = icon;
        button.Location = new Point(8, top);
        button.Size = new Size(56, 56);
        button.Click += delegate { ShowPage(index); };
        // The rail carries no captions, so the tooltip is the only place the shortcut can be taught.
        railTips.SetToolTip(button, label + "   Ctrl+" + (index + 1));
        return button;
    }

    /// <summary>
    /// Shows one page and marks its rail icon; no state method has to know about pages.
    /// </summary>
    private void ShowPage(int index)
    {
        if (pages == null || index < 0 || index >= pages.Length)
            return;

        activePage = index;
        if (railButtons != null)
        {
            for (int button = 0; button < railButtons.Length; button++)
                railButtons[button].IsActive = button == index;
        }
        RevealActivePage();
    }

    /// <summary>Puts the active page on screen and hides the rest.</summary>
    private void RevealActivePage()
    {
        if (pages == null || activePage < 0 || activePage >= pages.Length)
            return;
        for (int page = 0; page < pages.Length; page++)
            pages[page].Visible = page == activePage;
        pages[activePage].BringToFront();
    }

    /// <summary>
    /// Creates the window handles for the pages that start hidden. A page builds its whole subtree's
    /// handles the first time it is revealed, and left alone that lands on the first click of that
    /// section. Doing it while the window is already up spends it once, before anyone has clicked
    /// anything.
    /// </summary>
    private void WarmPages()
    {
        if (pages == null)
            return;
        for (int page = 0; page < pages.Length; page++)
        {
            if (page != activePage)
                WarmHandles(pages[page]);
        }
    }

    private static void WarmHandles(Control control)
    {
        IntPtr handle = control.Handle;
        GC.KeepAlive(handle);
        for (int child = 0; child < control.Controls.Count; child++)
            WarmHandles(control.Controls[child]);
    }

    /// <summary>
    /// Shared page heading. The lines are painted rather than put in AutoSize labels because a label
    /// leaves GDI to add its own overhang inset, and that inset differs between the sizes the title and
    /// the line under it are set at - which is exactly the ragged left edge this pass is meant to
    /// remove. NoPadding puts both lines on the gutter to the pixel.
    ///
    /// The second line is optional. A page that hands in an empty subtitle gets a shorter heading row
    /// with its title centred in it, rather than the title parked at the top of a band with nothing
    /// under it, which reads as a missing element rather than as spacing.
    /// </summary>
    private Panel BuildPageHeader(string title, string subtitle)
    {
        Panel header = new Panel();
        header.BackColor = Color.Transparent;
        header.Dock = DockStyle.Fill;
        header.Margin = Padding.Empty;

        string caption = UiTheme.Track(title.ToUpperInvariant());
        string detail = subtitle ?? "";
        header.Paint += delegate(object sender, PaintEventArgs paint)
        {
            Graphics graphics = paint.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int top = detail.Length > 0
                ? 2
                : Math.Max(0, (header.ClientSize.Height - PageTitleHeight) / 2);
            // The title is the only copy on a page that sits on bare canvas instead of inside a
            // panel, so the empty gutter beside it would read as a mistake. The brass tick fills it
            // and repeats the marker the active rail icon carries. It is inset from the line box at
            // both ends so it marks the capitals rather than the leading around them.
            using (GraphicsPath tick = UiTheme.RoundedPath(
                new RectangleF(9F, top + 2.5F, 4F, Math.Max(6F, PageTitleHeight - 5F)), 2F))
            using (LinearGradientBrush brass = new LinearGradientBrush(
                new RectangleF(9F, top + 1.5F, 4F, Math.Max(8F, PageTitleHeight - 3F)),
                UiTheme.AccentHover,
                UiTheme.Brass,
                LinearGradientMode.Vertical))
                graphics.FillPath(brass, tick);

            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(graphics, caption, PageTitleFont,
                new Point(Gutter, top), UiTheme.Text, TextFormatFlags.NoPadding);
            if (detail.Length > 0)
            {
                TextRenderer.DrawText(graphics, detail, PageSubtitleFont,
                    new Point(Gutter, 35), UiTheme.TextMuted, TextFormatFlags.NoPadding);
            }
        };
        return header;
    }

    /// <summary>
    /// Page frame with a fixed heading row. A table is used rather than docking because dock
    /// order inside a plain panel depends on z-order, and the rows here have to stay put.
    /// </summary>
    private TableLayoutPanel BuildPage(string title, string subtitle, params int[] bodyRows)
    {
        TableLayoutPanel page = new TableLayoutPanel();
        page.BackColor = Color.Transparent;
        page.ColumnCount = 1;
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        page.Margin = Padding.Empty;
        page.Padding = Padding.Empty;
        page.RowCount = 1 + bodyRows.Length;
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, String.IsNullOrEmpty(subtitle)
            ? CompactPageHeaderHeight
            : PageHeaderHeight));
        foreach (int height in bodyRows)
        {
            page.RowStyles.Add(height <= 0
                ? new RowStyle(SizeType.Percent, 100F)
                : new RowStyle(SizeType.Absolute, height));
        }
        page.Controls.Add(BuildPageHeader(title, subtitle), 0, 0);
        return page;
    }

    private Panel BuildSkyboxesPage()
    {
        // No second heading line and no category row. Both were saying what the grid already shows -
        // the cards carry their own category caption, and search still matches on it - so the band they
        // occupied goes to the gallery instead, which is the only thing on this page anyone came for.
        TableLayoutPanel page = BuildPage("Skyboxes", "", 0, 76);

        RoundedPanel searchFrame = BuildSearchFrame();
        Panel header = (Panel)page.GetControlFromPosition(0, 0);
        header.Controls.Add(searchFrame);
        searchFrame.Location = new Point(
            Math.Max(0, header.Width - searchFrame.Width),
            Math.Max(0, (CompactPageHeaderHeight - searchFrame.Height) / 2));
        header.Resize += delegate
        {
            searchFrame.Left = Math.Max(0, header.ClientSize.Width - searchFrame.Width);
        };

        RoundedPanel galleryFrame = new RoundedPanel();
        galleryFrame.BorderColor = UiTheme.BorderSoft;
        galleryFrame.CornerRadius = 18;
        galleryFrame.Dock = DockStyle.Fill;
        galleryFrame.FillColor = Color.FromArgb(23, 33, 31);
        galleryFrame.Margin = new Padding(0, 10, 0, 4);
        galleryFrame.Padding = new Padding(7);

        cardScroll = new SkyboxScrollHost();
        cardScroll.BackColor = galleryFrame.FillColor;
        cardScroll.Dock = DockStyle.Fill;
        cardScroll.Margin = Padding.Empty;
        cardGrid = cardScroll.Content;
        galleryFrame.Controls.Add(cardScroll);
        page.Controls.Add(galleryFrame, 0, 1);

        page.Controls.Add(BuildSkyboxActions(), 0, 2);
        return page;
    }

    /// <summary>Search field with its hand-drawn magnifier, lifted out of the old header.</summary>
    private RoundedPanel BuildSearchFrame()
    {
        RoundedPanel searchFrame = new RoundedPanel();
        searchFrame.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        searchFrame.BorderColor = UiTheme.BorderSoft;
        searchFrame.CornerRadius = 14;
        searchFrame.FillColor = UiTheme.Surface;
        searchFrame.GradientColor = Color.FromArgb(31, 43, 40);
        searchFrame.Size = new Size(280, 40);
        searchFrame.Paint += delegate(object glyphSender, PaintEventArgs glyph)
        {
            glyph.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(UiTheme.TextDim, 1.7F))
            {
                glyph.Graphics.DrawEllipse(pen, 15F, 13.5F, 10F, 10F);
                glyph.Graphics.DrawLine(pen, 24.4F, 22.9F, 28.4F, 26.9F);
            }
        };

        searchBox = new TextBox();
        searchBox.BackColor = searchFrame.FillAt(14);
        searchBox.BorderStyle = BorderStyle.None;
        searchBox.Font = UiTheme.Font(9F, FontStyle.Regular);
        searchBox.ForeColor = UiTheme.Text;
        searchBox.Location = new Point(37, 11);
        searchBox.Width = searchFrame.Width - 54;
        searchBox.TextChanged += delegate
        {
            searchQuery = searchBox.Text.Trim();
            ApplyFilter();
        };
        // An accent ring while typing, so the field reads as focused without a system border.
        searchBox.GotFocus += delegate
        {
            searchFrame.BorderColor = UiTheme.Alpha(UiTheme.Accent, 165);
            searchFrame.Invalidate();
        };
        searchBox.LostFocus += delegate
        {
            searchFrame.BorderColor = UiTheme.BorderSoft;
            searchFrame.Invalidate();
        };
        searchFrame.Controls.Add(searchBox);
        searchBox.Top = Math.Max(2, (searchFrame.Height - searchBox.Height) / 2);

        // A placeholder label would sit behind the native edit control, so the prompt is handed to
        // the edit itself. EM_SETCUEBANNER needs the comctl32 v6 manifest, which the launcher has.
        ApplyCueBanner(searchBox, "Search skies");
        return searchFrame;
    }

    private Panel BuildTitleBar()
    {
        Panel bar = new Panel();
        bar.BackColor = Color.Transparent;
        bar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        bar.Location = new Point(ClientSize.Width - 106, 0);
        bar.Size = new Size(96, 56);

        WindowButton close = new WindowButton();
        close.Kind = WindowButtonKind.Close;
        close.HoverColor = UiTheme.Danger;
        close.Size = new Size(36, 34);
        close.Click += delegate { Close(); };
        bar.Controls.Add(close);

        WindowButton minimize = new WindowButton();
        minimize.Kind = WindowButtonKind.Minimize;
        minimize.HoverColor = UiTheme.Cyan;
        minimize.Size = new Size(36, 34);
        minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
        bar.Controls.Add(minimize);
        bar.Resize += delegate
        {
            close.Location = new Point(bar.ClientSize.Width - close.Width - 13, 11);
            minimize.Location = new Point(close.Left - minimize.Width - 4, 11);
        };
        close.Location = new Point(bar.ClientSize.Width - close.Width - 13, 11);
        minimize.Location = new Point(close.Left - minimize.Width - 4, 11);
        return bar;
    }

    private void EnableWindowDragging(Control root)
    {
        if (root == null || IsInteractiveDragControl(root))
            return;

        // Keep the custom scrollbar interactive while allowing blank gallery space to drag.
        if (!(root is SkyboxScrollHost))
        {
            root.MouseDown -= DragWindow;
            root.MouseDown += DragWindow;
        }

        foreach (Control child in root.Controls)
            EnableWindowDragging(child);
    }

    private static bool IsInteractiveDragControl(Control control)
    {
        return control is ActionButton ||
            control is FixToggle ||
            control is RailButton ||
            control is WindowButton ||
            control is SkyboxCard;
    }

    private void DragWindow(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }

    private void AnimateEntrance(object sender, EventArgs e)
    {
        Opacity = Math.Min(1D, Opacity + 0.075D);
        if (Opacity >= 1D)
            entranceTimer.Stop();
    }

    private void StartCardReveal()
    {
        revealTimer.Stop();
        revealIndex = 0;
        revealCards.Clear();
        foreach (SkyboxVariant variant in variants)
        {
            SkyboxCard card = cards[variant.id];
            if (cardScroll.IsInViewport(card))
            {
                card.ResetReveal();
                revealCards.Add(card);
            }
            else
            {
                card.RevealImmediately();
            }
        }
        if (revealCards.Count > 0)
            revealTimer.Start();
    }

    private void RevealNextCard(object sender, EventArgs e)
    {
        int revealed = 0;
        while (revealIndex < revealCards.Count && revealed < 2)
        {
            SkyboxCard card = revealCards[revealIndex++];
            card.BeginReveal();
            revealed++;
        }
        if (revealIndex >= revealCards.Count)
            revealTimer.Stop();
    }

    private void UpdateRoundedRegion()
    {
        Region previous = Region;
        using (GraphicsPath path = UiTheme.RoundedPath(
            new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height)), 18F))
            Region = new Region(path);
        if (previous != null)
            previous.Dispose();
    }

    /// <summary>Card shell shared by the Fixes, GameInfo and Settings pages.</summary>
    private static RoundedPanel BuildCard()
    {
        RoundedPanel card = new RoundedPanel();
        card.BorderColor = UiTheme.BorderSoft;
        card.CornerRadius = 18;
        card.Dock = DockStyle.Fill;
        card.FillColor = Color.FromArgb(24, 34, 32);
        card.GradientColor = Color.FromArgb(31, 44, 40);
        card.Margin = new Padding(0, 0, 0, 4);
        return card;
    }

    private static Label BuildCardHeading(string text, int top)
    {
        Label heading = new Label();
        heading.AutoSize = true;
        heading.BackColor = Color.Transparent;
        heading.Font = UiTheme.Heavy(7.5F);
        heading.ForeColor = UiTheme.Brass;
        heading.Location = new Point(Gutter - TextInset(heading.Font), top);
        heading.Text = UiTheme.Track(text);
        return heading;
    }

    /// <summary>
    /// Wrapped body copy. The rows are laid out here rather than left to AutoSize because the label
    /// has to reserve them before the card is shown, and a hidden page never gets a first layout.
    /// <see cref="ProseLabel"/> paints its own rows with NoPadding, so the glyphs start on the gutter
    /// without the inset correction the plain labels around it need.
    /// </summary>
    private static Label BuildCardBody(string text, int top, int width)
    {
        ProseLabel body = new ProseLabel();
        body.Font = UiTheme.Font(8.5F, FontStyle.Regular);
        body.ForeColor = UiTheme.Mix(UiTheme.Text, UiTheme.TextMuted, 0.42F);
        body.Leading = ProseLeading;
        body.Location = new Point(Gutter, top);
        body.Text = text;
        body.LayoutColumn(width);
        return body;
    }

    /// <summary>Value line under a heading, in the same weight the status strip uses.</summary>
    private static Label BuildStateLabel(int top, string text)
    {
        Label state = new Label();
        state.AutoSize = true;
        state.BackColor = Color.Transparent;
        state.Font = UiTheme.Heavy(9.5F);
        state.ForeColor = UiTheme.TextMuted;
        state.Location = new Point(Gutter - TextInset(state.Font), top);
        state.Text = text;
        return state;
    }

    /// <summary>
    /// The left inset GDI puts inside a Label before the first glyph. Label hands its text to
    /// TextRenderer without NoPadding, so a heading, its body copy and a value line in three
    /// different faces start at three different pixels. Subtracting the inset lets each caller put
    /// the glyphs themselves on the gutter instead of the box around them.
    ///
    /// The inset is measured by rendering a probe label rather than derived from the font metrics:
    /// the pad depends on the padding option Label picks and on the face GDI+ actually resolved, and
    /// a formula that is off by three pixels is exactly the raggedness this is meant to remove. One
    /// small bitmap per distinct font at build time, cached, never on a paint path.
    /// </summary>
    private static int TextInset(Font font)
    {
        string key = font.FontFamily.Name + "|" + font.SizeInPoints.ToString("0.##") + "|" + (int)font.Style;
        int inset;
        if (TextInsets.TryGetValue(key, out inset))
            return inset;

        inset = 0;
        using (Label probe = new Label())
        {
            probe.AutoSize = false;
            probe.BackColor = Color.Black;
            probe.Font = font;
            probe.ForeColor = Color.White;
            probe.Padding = Padding.Empty;
            probe.Size = new Size(80, font.Height + 10);
            probe.Text = "H";
            using (Bitmap sheet = new Bitmap(probe.Width, probe.Height))
            {
                probe.DrawToBitmap(sheet, new Rectangle(0, 0, sheet.Width, sheet.Height));
                inset = FirstLitColumn(sheet);
            }
        }

        TextInsets[key] = inset;
        return inset;
    }

    /// <summary>Leftmost column of a probe sheet that carries any glyph coverage.</summary>
    private static int FirstLitColumn(Bitmap sheet)
    {
        for (int x = 0; x < sheet.Width; x++)
        {
            for (int y = 0; y < sheet.Height; y++)
            {
                if (sheet.GetPixel(x, y).G > 90)
                    return x;
            }
        }
        return 0;
    }

    private Panel BuildFixesPage()
    {
        TableLayoutPanel page = BuildPage("Fixes", "Close Deadlock before switching. Changes appear on the next launch.", 0);
        RoundedPanel card = BuildCard();
        TableLayoutPanel rows = new TableLayoutPanel();
        rows.BackColor = Color.Transparent;
        rows.Dock = DockStyle.Fill;
        rows.Margin = Padding.Empty;
        rows.Padding = new Padding(Gutter, 8, Gutter, 8);
        rows.ColumnCount = 1;
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rows.RowCount = 9;
        rows.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
        for (int i = 0; i < rows.RowCount; i++)
            rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows.RowCount));

        veilSwitch = new FixToggle();
        veilStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 0, "BASE VEIL", "Team-colored clouds above the bases.", veilSwitch, veilStateLabel);
        veilSwitch.Click += delegate { ToggleBaseVeil(); };

        smokeSwitch = new FixToggle();
        smokeStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 1, "FACTORY SMOKE", "The large dark cloud above the factory.", smokeSwitch, smokeStateLabel);
        smokeSwitch.Click += delegate { ToggleFactorySmoke(); };

        namesSwitch = new FixToggle();
        namesStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 2, "UNIT NAMES", "Floating labels over heroes and other units.", namesSwitch, namesStateLabel);
        namesSwitch.Click += delegate { ToggleNames(); };

        bookSwitch = new FixToggle();
        bookStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 3, "PICKUP BOOK", "The brief floating model after collecting a bonus.", bookSwitch, bookStateLabel);
        bookSwitch.Click += delegate { TogglePickupBook(); };

        playerMarkersSwitch = new FixToggle();
        playerMarkersStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 4, "PLAYER MARKERS", "Player name, portrait and distance in the world.", playerMarkersSwitch, playerMarkersStateLabel);
        playerMarkersSwitch.Click += delegate { TogglePlayerMarkers(); };

        gravesMarkersSwitch = new FixToggle();
        gravesMarkersStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 5, "GRAVES MARKERS", "Distance icons for Graves' zombies and ultimate.", gravesMarkersSwitch, gravesMarkersStateLabel);
        gravesMarkersSwitch.Click += delegate { ToggleGravesMarkers(); };

        healthLinesSwitch = new FixToggle();
        healthLinesStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 6, "HP BAR LINES", "Horizontal tick marks inside your health bar.", healthLinesSwitch, healthLinesStateLabel);
        healthLinesSwitch.Click += delegate { ToggleHealthLines(); };

        classicFillSwitch = new FixToggle();
        classicFillStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 7, "CLASSIC ABILITY FILL", "Purple upgrades with bright amber build recommendations.", classicFillSwitch, classicFillStateLabel);
        classicFillSwitch.Click += delegate { ToggleClassicFill(); };

        colorFixSwitch = new FixToggle();
        colorFixStateLabel = BuildStateLabel(0, "Checking");
        AddFixRow(rows, 8, "COLORFIX", "Softer scene contrast, without changing the HUD.", colorFixSwitch, colorFixStateLabel);
        colorFixSwitch.Click += delegate { ToggleColorFix(); };

        card.Controls.Add(rows);
        page.Controls.Add(card, 0, 1);
        return page;
    }

    private static void AddFixRow(TableLayoutPanel rows, int index, string title, string description,
        FixToggle toggle, Label state)
    {
        Panel row = new Panel();
        row.BackColor = Color.Transparent;
        row.Dock = DockStyle.Fill;
        row.Margin = Padding.Empty;

        Label heading = BuildCardHeading(title, 0);
        heading.AutoSize = false;
        heading.Location = new Point(0, 6);
        heading.Height = 19;
        heading.AutoEllipsis = true;
        row.Controls.Add(heading);

        Label detail = new Label();
        detail.AutoSize = false;
        detail.AutoEllipsis = true;
        detail.BackColor = Color.Transparent;
        detail.Font = new Font("Segoe UI", Math.Max(6F, 9F * UiScale.FontFactor), FontStyle.Regular);
        detail.ForeColor = UiTheme.Mix(UiTheme.Text, UiTheme.TextMuted, 0.38F);
        detail.Location = new Point(0, 26);
        detail.Height = 20;
        detail.Text = description;
        row.Controls.Add(detail);

        state.AutoSize = false;
        state.AutoEllipsis = true;
        state.Font = new Font("Segoe UI", Math.Max(6F, 9F * UiScale.FontFactor), FontStyle.Bold);
        state.Size = new Size(245, 24);
        state.TextAlign = ContentAlignment.MiddleRight;
        row.Controls.Add(state);

        toggle.Size = new Size(62, 32);
        toggle.AccessibleName = title;
        row.Controls.Add(toggle);

        Panel line = null;
        if (index < rows.RowCount - 1)
        {
            line = new Panel();
            line.BackColor = UiTheme.Mix(UiTheme.BorderSoft, Color.FromArgb(24, 34, 32), 0.3F);
            line.Height = 1;
            row.Controls.Add(line);
        }

        row.Resize += delegate
        {
            int toggleLeft = row.ClientSize.Width - toggle.Width;
            toggle.Location = new Point(toggleLeft, Math.Max(0, (row.ClientSize.Height - toggle.Height) / 2));
            state.Location = new Point(toggleLeft - state.Width - 16,
                Math.Max(0, (row.ClientSize.Height - state.Height) / 2));
            int copyWidth = Math.Max(100, state.Left - 24);
            heading.Width = copyWidth;
            detail.Width = copyWidth;
            if (line != null)
            {
                line.Location = new Point(0, Math.Max(0, row.ClientSize.Height - 1));
                line.Width = row.ClientSize.Width;
            }
        };
        rows.Controls.Add(row, 0, index);
    }

    private Panel BuildGameInfoPage()
    {
        TableLayoutPanel page = BuildPage(
            "GameInfo",
            "Your saved game configuration, ready to apply.",
            0);

        RoundedPanel card = BuildCard();
        card.Controls.Add(BuildCardHeading("SAVED CONFIG", 26));
        Label summary = BuildCardBody(
            "Maxfps v1.0 with your texture and aspect-ratio settings. In-world pickup panels use " +
            "the game's default resolution. Physics is off; applying backs up the current file.",
            53, 690);
        card.Controls.Add(summary);

        installButton = new ActionButton();
        installButton.Location = new Point(Gutter, summary.Bottom + 33);
        installButton.Size = new Size(226, 44);
        installButton.Text = "APPLY CONFIG";
        installButton.Tone = ActionButtonTone.Install;
        installButton.Click += delegate { InstallComponent(); };
        card.Controls.Add(installButton);
        card.Controls.Add(BuildCardBody("Snapshot CE91F9C6A1CC  -  30 Sep 2026", installButton.Bottom + 20, 690));

        page.Controls.Add(card, 0, 1);
        return page;
    }

    private Panel BuildSettingsPage()
    {
        TableLayoutPanel page = BuildPage(
            "Settings",
            "Deadlock Skybox Selector - paths, hotkeys and build details.",
            0);

        RoundedPanel card = BuildCard();
        card.Controls.Add(BuildCardHeading("GAME FOLDER", 26));

        Label location = new Label();
        location.AutoEllipsis = false;
        location.BackColor = Color.Transparent;
        location.Font = UiTheme.Mono(8F);
        location.ForeColor = UiTheme.Mix(UiTheme.Text, UiTheme.TextMuted, 0.3F);
        location.Location = new Point(Gutter - TextInset(location.Font), 49);
        location.Text = deadlockRoot;
        Size locationSize = TextRenderer.MeasureText(deadlockRoot, location.Font, new Size(620, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
        location.Size = new Size(620, locationSize.Height + 2);
        card.Controls.Add(location);

        ActionButton openFolder = new ActionButton();
        openFolder.Location = new Point(Gutter, location.Bottom + 14);
        openFolder.Size = new Size(196, 40);
        openFolder.Text = "OPEN GAME FOLDER";
        openFolder.Tone = ActionButtonTone.Install;
        openFolder.Click += delegate { OpenDeadlockFolder(); };
        card.Controls.Add(openFolder);

        Panel separator = new Panel();
        separator.BackColor = UiTheme.BorderSoft;
        separator.Location = new Point(Gutter, openFolder.Bottom + 22);
        separator.Size = new Size(660, 1);
        card.Controls.Add(separator);

        card.Controls.Add(BuildCardHeading("HOTKEYS", separator.Bottom + 20));
        Label hotkeys = BuildCardBody(
            "Ctrl+1 to Ctrl+4 switch sections. Ctrl+F jumps into search, Escape clears it, and the " +
            "arrow keys walk the grid. Double-click a card for a full-size preview.",
            separator.Bottom + 43, 660);
        card.Controls.Add(hotkeys);

        card.Controls.Add(BuildCardHeading("BUILD", hotkeys.Bottom + 26));

        Label build = new Label();
        build.AutoSize = true;
        build.BackColor = Color.Transparent;
        build.Font = UiTheme.Mono(7.5F);
        build.ForeColor = UiTheme.TextDim;
        build.Location = new Point(Gutter - TextInset(build.Font), hotkeys.Bottom + 49);
        build.Text = "assets " + ShortHash(assetHash) + "  -  " + variants.Count + " skies";
        card.Controls.Add(build);

        Label author = new Label();
        author.AutoSize = true;
        author.BackColor = Color.Transparent;
        author.Font = UiTheme.Heavy(7F);
        author.ForeColor = UiTheme.Brass;
        author.Location = new Point(Gutter - TextInset(author.Font), build.Bottom + 10);
        author.Text = UiTheme.Track("MADE BY HARRITON");
        card.Controls.Add(author);

        page.Controls.Add(card, 0, 1);
        return page;
    }

    private static string ShortHash(string hash)
    {
        if (String.IsNullOrEmpty(hash))
            return "unknown";
        return hash.Length <= 12 ? hash : hash.Substring(0, 12);
    }

    private void OpenDeadlockFolder()
    {
        try
        {
            if (!Directory.Exists(deadlockRoot))
                throw new DirectoryNotFoundException("Deadlock folder was not found: " + deadlockRoot);

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "explorer.exe";
            info.Arguments = Quote(deadlockRoot);
            info.UseShellExecute = true;
            Process.Start(info);
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    /// <summary>
    /// Hides the cards that do not match the search text. The selection is deliberately kept even when
    /// its card is filtered out: the status panel always names what Apply would install, so silently
    /// dropping it would be the more surprising behaviour.
    /// </summary>
    private void ApplyFilter()
    {
        int visible = 0;
        cardGrid.SuspendLayout();
        foreach (SkyboxVariant variant in variants)
        {
            SkyboxCard card;
            if (!cards.TryGetValue(variant.id, out card))
                continue;
            bool match = MatchesFilter(variant);
            card.Visible = match;
            if (match)
                visible++;
        }
        if (emptyLabel != null)
            emptyLabel.Visible = visible == 0;
        cardGrid.ResumeLayout(true);
        if (cardScroll != null)
            cardScroll.ScrollToTop();
    }

    private bool MatchesFilter(SkyboxVariant variant)
    {
        if (searchQuery.Length == 0)
            return true;
        // The category is still searchable even though the tab row that filtered by it is gone:
        // typing "anime" is the shortest route to that half of the library.
        return Contains(GetDisplayName(variant), searchQuery) ||
            Contains(variant.id, searchQuery) ||
            Contains(variant.category, searchQuery);
    }

    private static bool Contains(string text, string query)
    {
        return !String.IsNullOrEmpty(text) &&
            text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private Control BuildStatusPanel()
    {
        RoundedPanel panel = new RoundedPanel();
        panel.BorderColor = UiTheme.Alpha(UiTheme.Brass, 90);
        panel.CornerRadius = 17;
        panel.Dock = DockStyle.Fill;
        panel.DrawTopGlow = true;
        panel.FillColor = Color.FromArgb(29, 40, 37);
        panel.GradientColor = Color.FromArgb(38, 52, 46);
        panel.Margin = new Padding(0, 0, 0, 8);
        panel.Padding = Padding.Empty;

        // The dot lives in the lane left of the text axis, the same place the page heading keeps its
        // brass tick. Markers to the left of the gutter, words on it - that is the whole alignment
        // rule of the window in one line.
        statusDot = new StatusDot();
        statusDot.Tone = UiTheme.Warning;
        statusDot.Location = new Point(7, 21);
        statusDot.Size = new Size(11, 11);
        panel.Controls.Add(statusDot);

        statusTitle = new Label();
        statusTitle.AutoSize = true;
        statusTitle.BackColor = Color.Transparent;
        statusTitle.Font = UiTheme.Heavy(11F);
        statusTitle.ForeColor = UiTheme.Text;
        statusTitle.Location = new Point(Gutter - TextInset(statusTitle.Font), 15);
        statusTitle.Text = "Reading the install";
        panel.Controls.Add(statusTitle);

        statusDetail = new Label();
        statusDetail.AutoEllipsis = true;
        statusDetail.BackColor = Color.Transparent;
        statusDetail.Font = UiTheme.Font(8.5F, FontStyle.Regular);
        statusDetail.ForeColor = UiTheme.Mix(UiTheme.Text, UiTheme.TextMuted, 0.42F);
        statusDetail.Location = new Point(Gutter - TextInset(statusDetail.Font), 41);
        statusDetail.Size = new Size(600, 18);
        statusDetail.Text = "Checking gameinfo.gi and the skybox packs";
        panel.Controls.Add(statusDetail);

        selectionTitle = new Label();
        selectionTitle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        selectionTitle.Font = UiTheme.Heavy(8F);
        selectionTitle.BackColor = Color.Transparent;
        selectionTitle.ForeColor = UiTheme.Accent;
        selectionTitle.Location = new Point(panel.Width - 270, 17);
        selectionTitle.Size = new Size(245, 20);
        selectionTitle.Text = UiTheme.Track("NOTHING PICKED");
        selectionTitle.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(selectionTitle);

        selectionDetail = new Label();
        selectionDetail.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        selectionDetail.BackColor = Color.Transparent;
        selectionDetail.Font = UiTheme.Font(8.5F, FontStyle.Regular);
        selectionDetail.ForeColor = UiTheme.Mix(UiTheme.Text, UiTheme.TextMuted, 0.42F);
        selectionDetail.Location = new Point(panel.Width - 270, 41);
        selectionDetail.Size = new Size(245, 18);
        selectionDetail.Text = "Pick a card below";
        selectionDetail.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(selectionDetail);

        // Right-aligned text carries the same overhang pad on its trailing edge, so the inset is
        // shortened by it - otherwise this block would stop short of the gutter the buttons under
        // the gallery are flush with.
        int titleInset = Gutter - TextInset(selectionTitle.Font);
        int detailInset = Gutter - TextInset(selectionDetail.Font);
        panel.Resize += delegate
        {
            selectionTitle.Left = panel.ClientSize.Width - selectionTitle.Width - titleInset;
            selectionDetail.Left = panel.ClientSize.Width - selectionDetail.Width - detailInset;
        };
        return panel;
    }

    /// <summary>
    /// Skybox actions stay separate from the independent visual fixes page.
    /// </summary>
    private Control BuildSkyboxActions()
    {
        RoundedPanel panel = new RoundedPanel();
        panel.BorderColor = UiTheme.BorderSoft;
        panel.CornerRadius = 18;
        panel.Dock = DockStyle.Fill;
        panel.FillColor = Color.FromArgb(25, 35, 33);
        panel.GradientColor = Color.FromArgb(32, 45, 41);
        panel.Margin = new Padding(0, 10, 0, 0);

        restoreButton = new ActionButton();
        restoreButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        restoreButton.Tone = ActionButtonTone.Restore;
        restoreButton.Location = new Point(panel.Width - 330, 16);
        restoreButton.Size = new Size(158, 44);
        restoreButton.Text = "RESTORE";
        restoreButton.Click += delegate { ApplySelection("vanilla"); };
        panel.Controls.Add(restoreButton);

        applyButton = new ActionButton();
        applyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        applyButton.Location = new Point(panel.Width - 170, 16);
        applyButton.Size = new Size(158, 44);
        applyButton.Text = "APPLY SKY";
        applyButton.Tone = ActionButtonTone.Apply;
        applyButton.Click += delegate
        {
            if (selectedVariant != null)
                ApplySelection(selectedVariant.id);
        };
        panel.Controls.Add(applyButton);

        panel.Resize += delegate
        {
            applyButton.Left = panel.ClientSize.Width - applyButton.Width - Gutter;
            restoreButton.Left = applyButton.Left - restoreButton.Width - 12;
        };
        UpdateButtons();
        return panel;
    }

    private void BuildCards()
    {
        cardGrid.SuspendLayout();
        cardTips = new ToolTip();
        cardTips.AutoPopDelay = 6000;
        cardTips.InitialDelay = 550;
        cardTips.ReshowDelay = 200;
        foreach (SkyboxVariant variant in variants)
        {
            string previewPath = ResolveThumbnailPath(variant);
            SkyboxCard card = new SkyboxCard(variant, previewPath);
            // The flow wraps on width + right margin, so the card pitch is sized to land just
            // inside the viewport rather than leaving a dead column on the right.
            card.Margin = new Padding(0, 0, 10, 12);
            card.Click += delegate { SelectVariant(variant); };
            card.DoubleClick += delegate { ShowLargePreview(variant); };
            cardTips.SetToolTip(card, GetDisplayName(variant) + " - double-click for a full-size look");
            cards.Add(variant.id, card);
            cardGrid.Controls.Add(card);
        }

        emptyLabel = new Label();
        emptyLabel.BackColor = Color.Transparent;
        emptyLabel.Font = UiTheme.Heavy(10F);
        emptyLabel.ForeColor = UiTheme.TextMuted;
        // Sits where a card title would: 7 px of frame padding plus 5 px of grid padding is already
        // spent before the flow starts, so the margin makes up the rest of the 24 px axis.
        emptyLabel.Margin = new Padding(Math.Max(0, 12 - TextInset(emptyLabel.Font)), 16, 0, 0);
        emptyLabel.Size = new Size(460, 26);
        emptyLabel.Text = "No sky matches that search.";
        emptyLabel.Visible = false;
        cardGrid.Controls.Add(emptyLabel);
        cardGrid.ResumeLayout();
    }

    private string ResolveThumbnailPath(SkyboxVariant variant)
    {
        string thumbnail = Path.Combine(cacheRoot, ".thumbnails-v1", variant.id + ".jpg");
        return File.Exists(thumbnail) ? thumbnail : ResolveCachePath(variant.preview);
    }

    private List<SkyboxVariant> LoadManifest()
    {
        string path = Path.Combine(cacheRoot, "manifest.json");
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        SkyboxManifest manifest = serializer.Deserialize<SkyboxManifest>(File.ReadAllText(path));
        if (manifest == null || manifest.formatVersion != 2 || manifest.variants == null)
            throw new InvalidDataException("The skybox library manifest is invalid.");
        if (manifest.baseVeilOverride != null &&
            Regex.IsMatch(manifest.baseVeilOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            baseVeilOverrideHash = manifest.baseVeilOverride.sha256;
        if (manifest.factorySmokeOverride != null &&
            Regex.IsMatch(manifest.factorySmokeOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            factorySmokeOverrideHash = manifest.factorySmokeOverride.sha256;
        if (manifest.hideNamesOverride != null &&
            Regex.IsMatch(manifest.hideNamesOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            hideNamesOverrideHash = manifest.hideNamesOverride.sha256;
        if (manifest.hidePickupBookOverride != null &&
            Regex.IsMatch(manifest.hidePickupBookOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            hidePickupBookOverrideHash = manifest.hidePickupBookOverride.sha256;
        if (manifest.hidePlayerMarkersOverride != null &&
            Regex.IsMatch(manifest.hidePlayerMarkersOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            hidePlayerMarkersOverrideHash = manifest.hidePlayerMarkersOverride.sha256;
        if (manifest.hideGravesMarkersOverride != null &&
            Regex.IsMatch(manifest.hideGravesMarkersOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            hideGravesMarkersOverrideHash = manifest.hideGravesMarkersOverride.sha256;
        if (manifest.hideCombinedMarkersOverride != null &&
            Regex.IsMatch(manifest.hideCombinedMarkersOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            hideCombinedMarkersOverrideHash = manifest.hideCombinedMarkersOverride.sha256;
        if (manifest.hideHealthLinesOverride != null &&
            Regex.IsMatch(manifest.hideHealthLinesOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            hideHealthLinesOverrideHash = manifest.hideHealthLinesOverride.sha256;
        if (manifest.classicAbilityFillOverride != null &&
            Regex.IsMatch(manifest.classicAbilityFillOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            classicAbilityFillOverrideHash = manifest.classicAbilityFillOverride.sha256;
        if (manifest.colorFixOverride != null &&
            Regex.IsMatch(manifest.colorFixOverride.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            colorFixOverrideHash = manifest.colorFixOverride.sha256;
        return new List<SkyboxVariant>(manifest.variants);
    }

    private string ResolveCachePath(string relativePath)
    {
        if (String.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("The skybox preview path is invalid.");
        string root = Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(cacheRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidDataException("The skybox preview is missing: " + relativePath);
        return path;
    }

    private void SelectVariant(SkyboxVariant variant)
    {
        selectedVariant = variant;
        foreach (KeyValuePair<string, SkyboxCard> entry in cards)
            entry.Value.IsSelected = String.Equals(entry.Key, variant.id, StringComparison.OrdinalIgnoreCase);

        selectionTitle.Text = UiTheme.Track(GetDisplayName(variant).ToUpperInvariant());
        selectionDetail.Text = String.Equals(variant.id, currentSelection, StringComparison.OrdinalIgnoreCase) &&
            !currentNeedsUpdate
            ? "Live right now"
            : "Ready to apply";
        UpdateButtons();
    }

    /// <summary>
    /// Keyboard access for the gallery. ProcessCmdKey is used instead of a KeyDown handler so the
    /// arrows arrive before WinForms turns them into dialog navigation between the action buttons.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (HandleGalleryKey(keyData))
            return true;
        return base.ProcessCmdKey(ref message, keyData);
    }

    private bool HandleGalleryKey(Keys keyData)
    {
        if ((keyData & Keys.Alt) == Keys.Alt || (keyData & Keys.Shift) == Keys.Shift)
            return false;

        Keys key = keyData & Keys.KeyCode;
        bool control = (keyData & Keys.Control) == Keys.Control;
        bool typing = searchBox != null && searchBox.Focused;

        if (control)
        {
            // Ctrl+1..4 jump between rail sections; the icons carry no labels, so a keyboard
            // route to them matters more here than it did with the old text sidebar.
            if (key >= Keys.D1 && key <= Keys.D4)
            {
                ShowPage(key - Keys.D1);
                return true;
            }
            if (key != Keys.F || searchBox == null)
                return false;
            if (activePage != 0)
                ShowPage(0);
            searchBox.Focus();
            searchBox.SelectAll();
            return true;
        }

        if (key == Keys.Escape)
        {
            if (searchBox == null || searchBox.Text.Length == 0)
                return false;
            searchBox.Clear();
            return true;
        }

        if (working)
            return false;

        // Gallery navigation belongs to the Skyboxes page: moving the selection while another
        // page is up would change what Apply installs with nothing on screen to show it.
        if (activePage != 0)
            return false;

        switch (key)
        {
            case Keys.Left:
                return !typing && MoveSelection(-1);
            case Keys.Right:
                return !typing && MoveSelection(1);
            case Keys.Up:
                return MoveSelection(-GridColumns());
            case Keys.Down:
                return MoveSelection(GridColumns());
            case Keys.Home:
                return !typing && SelectVisibleAt(0);
            case Keys.End:
                return !typing && SelectVisibleAt(Int32.MaxValue);
            case Keys.Space:
                if (typing || selectedVariant == null || FocusedControl() is ActionButton)
                    return false;
                ShowLargePreview(selectedVariant);
                return true;
            case Keys.Return:
                // Apply changes game files, so it only reacts when the keyboard is not busy with
                // the search box or another button that has its own Enter behaviour.
                if (typing || FocusedControl() is ActionButton)
                    return false;
                if (applyButton == null || !applyButton.Enabled || selectedVariant == null)
                    return false;
                ApplySelection(selectedVariant.id);
                return true;
        }
        return false;
    }

    private Control FocusedControl()
    {
        Control focused = ActiveControl;
        while (focused is IContainerControl)
        {
            Control inner = ((IContainerControl)focused).ActiveControl;
            if (inner == null)
                break;
            focused = inner;
        }
        return focused;
    }

    private List<SkyboxCard> VisibleCards()
    {
        List<SkyboxCard> visible = new List<SkyboxCard>();
        foreach (Control control in cardGrid.Controls)
        {
            SkyboxCard card = control as SkyboxCard;
            if (card != null && card.Visible)
                visible.Add(card);
        }
        return visible;
    }

    private int GridColumns()
    {
        List<SkyboxCard> visible = VisibleCards();
        if (visible.Count == 0)
            return 1;
        int top = visible[0].Top;
        int columns = 0;
        foreach (SkyboxCard card in visible)
        {
            if (card.Top != top)
                break;
            columns++;
        }
        return Math.Max(1, columns);
    }

    private bool MoveSelection(int delta)
    {
        List<SkyboxCard> visible = VisibleCards();
        if (visible.Count == 0)
            return false;

        int index = -1;
        if (selectedVariant != null)
        {
            for (int position = 0; position < visible.Count; position++)
            {
                if (String.Equals(visible[position].Variant.id, selectedVariant.id, StringComparison.OrdinalIgnoreCase))
                {
                    index = position;
                    break;
                }
            }
        }

        int target = index < 0 ? (delta < 0 ? visible.Count - 1 : 0) : index + delta;
        return SelectCard(visible[Math.Max(0, Math.Min(visible.Count - 1, target))]);
    }

    private bool SelectVisibleAt(int index)
    {
        List<SkyboxCard> visible = VisibleCards();
        if (visible.Count == 0)
            return false;
        return SelectCard(visible[Math.Max(0, Math.Min(visible.Count - 1, index))]);
    }

    private bool SelectCard(SkyboxCard card)
    {
        if (card == null || card.Variant == null)
            return false;
        SelectVariant(card.Variant);
        cardScroll.EnsureVisible(card);
        return true;
    }

    private void ShowLargePreview(SkyboxVariant variant)
    {
        if (variant == null || working)
            return;
        try
        {
            using (PreviewForm preview = new PreviewForm(GetDisplayName(variant), ResolveCachePath(variant.preview)))
                preview.ShowDialog(this);
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void RefreshStatusAsync()
    {
        SetWorking(true, "Reading the install", "Checking gameinfo.gi and the skybox packs");
        BackgroundWorker worker = new BackgroundWorker();
        SelectorStatus status = null;
        worker.DoWork += delegate { status = ReadStatus(); };
        worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                ShowError(e.Error);
                SetWorking(false, "Could not read the install", GetDeepMessage(e.Error));
                return;
            }
            SetWorking(false, "Ready", "Install read.");
            ApplyStatus(status);
        };
        worker.RunWorkerAsync();
    }

    private SelectorStatus ReadStatus()
    {
        SelectorStatus status = new SelectorStatus();
        string gameInfo = Path.Combine(deadlockRoot, "game", "citadel", "gameinfo.gi");
        status.AddonsMounted = File.Exists(gameInfo) && Regex.IsMatch(
            File.ReadAllText(gameInfo),
            "(?im)^\\s*Game\\s+\"?citadel/addons\"?\\s*$");

        string addonsRoot = Path.Combine(deadlockRoot, "game", "citadel", "addons");
        string managedTarget = Path.Combine(addonsRoot, "pak01_dir.vpk");
        string veilTarget = Path.Combine(addonsRoot, "pak03_dir.vpk");
        string smokeTarget = Path.Combine(addonsRoot, "pak04_dir.vpk");
        string namesTarget = Path.Combine(addonsRoot, "pak05_dir.vpk");
        string bookTarget = Path.Combine(addonsRoot, "pak06_dir.vpk");
        string playerMarkersTarget = Path.Combine(addonsRoot, "pak07_dir.vpk");
        string healthLinesTarget = Path.Combine(addonsRoot, "pak08_dir.vpk");
        string classicFillTarget = Path.Combine(addonsRoot, "pak09_dir.vpk");
        string colorFixTarget = Path.Combine(addonsRoot, "pak10_dir.vpk");
        bool legacyPresent = false;
        bool unknownFiles = false;

        if (File.Exists(veilTarget))
        {
            string veilHash = ComputeFileSha256(veilTarget);
            status.BaseVeilHidden = String.Equals(
                veilHash, baseVeilOverrideHash, StringComparison.OrdinalIgnoreCase);
            status.BaseVeilSlotOccupied = !status.BaseVeilHidden;
        }

        if (File.Exists(smokeTarget))
        {
            string smokeHash = ComputeFileSha256(smokeTarget);
            status.FactorySmokeHidden = String.Equals(
                smokeHash, factorySmokeOverrideHash, StringComparison.OrdinalIgnoreCase);
            status.FactorySmokeSlotOccupied = !status.FactorySmokeHidden;
        }

        if (File.Exists(namesTarget))
        {
            string namesHash = ComputeFileSha256(namesTarget);
            status.NamesHidden = String.Equals(
                namesHash, hideNamesOverrideHash, StringComparison.OrdinalIgnoreCase);
            status.NamesSlotOccupied = !status.NamesHidden;
        }

        if (File.Exists(bookTarget))
        {
            string bookHash = ComputeFileSha256(bookTarget);
            status.BookHidden = String.Equals(
                bookHash, hidePickupBookOverrideHash, StringComparison.OrdinalIgnoreCase);
            status.BookSlotOccupied = !status.BookHidden;
        }

        if (File.Exists(playerMarkersTarget))
        {
            string markerHash = ComputeFileSha256(playerMarkersTarget);
            bool playerOnly = String.Equals(markerHash, hidePlayerMarkersOverrideHash, StringComparison.OrdinalIgnoreCase);
            bool gravesOnly = String.Equals(markerHash, hideGravesMarkersOverrideHash, StringComparison.OrdinalIgnoreCase);
            bool combined = String.Equals(markerHash, hideCombinedMarkersOverrideHash, StringComparison.OrdinalIgnoreCase);
            status.PlayerMarkersHidden = playerOnly || combined;
            status.GravesMarkersHidden = gravesOnly || combined;
            status.PlayerMarkersSlotOccupied = !playerOnly && !gravesOnly && !combined;
        }

        if (File.Exists(healthLinesTarget))
        {
            string linesHash = ComputeFileSha256(healthLinesTarget);
            status.HealthLinesHidden = String.Equals(
                linesHash, hideHealthLinesOverrideHash, StringComparison.OrdinalIgnoreCase);
            status.HealthLinesSlotOccupied = !status.HealthLinesHidden;
        }

        if (File.Exists(classicFillTarget))
        {
            string fillHash = ComputeFileSha256(classicFillTarget);
            status.ClassicFillEnabled = String.Equals(
                fillHash, classicAbilityFillOverrideHash, StringComparison.OrdinalIgnoreCase);
            status.ClassicFillNeedsUpdate = String.Equals(
                fillHash, "AA69FCB3F9E488654D66DD6C6D3B501109106377A12D03B74F6C2B4C65F185CB",
                StringComparison.OrdinalIgnoreCase) || String.Equals(
                fillHash, "BC31A1C4A901759671E6D13E10A0DCF3C806326110B42BC4F738C6B736F4DB3C",
                StringComparison.OrdinalIgnoreCase) || String.Equals(
                fillHash, "C2732D4F9DF4E8899A3EB3BC220686BC9F43513191BBC640B80B3045F2257C25",
                StringComparison.OrdinalIgnoreCase) || String.Equals(
                fillHash, "56A6BA1858D3DFCDE0FC3DF67298831663BD4A1BA3AF864EFCC9614029079C7B",
                StringComparison.OrdinalIgnoreCase) || String.Equals(
                fillHash, "860E312D79820B322BA6A6F4914E333794211A3B87279F7822F379578BCCAC91",
                StringComparison.OrdinalIgnoreCase);
            status.ClassicFillSlotOccupied = !status.ClassicFillEnabled && !status.ClassicFillNeedsUpdate;
        }

        if (File.Exists(colorFixTarget))
        {
            string colorHash = ComputeFileSha256(colorFixTarget);
            status.ColorFixEnabled = String.Equals(
                colorHash, colorFixOverrideHash, StringComparison.OrdinalIgnoreCase);
            status.ColorFixSlotOccupied = !status.ColorFixEnabled;
        }

        if (File.Exists(managedTarget))
        {
            string hash = ReadManagedSha256(managedTarget);
            SkyboxVariant current;
            if (variantsByHash.TryGetValue(hash, out current))
            {
                status.CurrentSelection = current.id;
                status.NeedsUpdate = String.Equals(hash, current.legacySha256, StringComparison.OrdinalIgnoreCase);
            }
            else
                unknownFiles = true;
        }

        string[] legacyNames = { "pak49_dir.vpk", "pak50_dir.vpk", "pak51_dir.vpk" };
        string[] legacyHashes =
        {
            "C9749F68343056B0582F7D0DDFDC11C97E3D3F8EFAEBFCF691AFBB9BF7EA5C0E",
            "4A4885756F4991266014BCC7FB06ACAE9633FD3918A23C8651E60455B91475DB",
            "972DAB7C46AC5D0EBCA7E318C87C970124B3D3C8405D8F59F1C9E4DA974D347E"
        };
        for (int index = 0; index < legacyNames.Length; index++)
        {
            string path = Path.Combine(addonsRoot, legacyNames[index]);
            if (!File.Exists(path))
                continue;
            legacyPresent = true;
            if (!String.Equals(ComputeFileSha256(path), legacyHashes[index], StringComparison.OrdinalIgnoreCase))
                unknownFiles = true;
        }

        string selectedFile = Path.Combine(cacheRoot, "selected-skybox.txt");
        if (unknownFiles)
        {
            status.UnknownFiles = true;
            status.Detail = "Another addon is using the skybox slot. It will be backed up and overridden.";
        }
        else if (status.CurrentSelection == "vanilla" && !legacyPresent)
        {
            status.Detail = "No custom skybox is currently installed.";
            if (File.Exists(selectedFile))
                File.Delete(selectedFile);
        }
        else
        {
            status.Detail = status.NeedsUpdate
                ? "Old skybox package detected. Reapply it to update for the game patch."
                : legacyPresent
                    ? "Installed. Legacy files will be cleaned on the next apply."
                    : "Installed and ready.";
            WriteTextIfChanged(selectedFile, status.CurrentSelection + Environment.NewLine);
        }
        return status;
    }

    private static string ComputeFileSha256(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024,
            FileOptions.SequentialScan))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    /// <summary>
    /// Hashes the active skybox package, reusing the previous result while the file identity
    /// (full path, length, creation and last-write timestamps) is unchanged. The managed
    /// package is around 67 MB and the status is re-read on startup and after every
    /// operation, so an unconditional hash dominates the perceived load time.
    /// </summary>
    private string ReadManagedSha256(string path)
    {
        StatusHashEntry stored = ReadStatusHashCache();
        StatusHashEntry current = DescribeFile(path);
        if (current == null)
            return ComputeFileSha256(path);
        if (stored != null && stored.Matches(current) && Regex.IsMatch(stored.sha256 ?? "", "^[0-9a-fA-F]{64}$"))
            return stored.sha256;

        string hash = ComputeFileSha256(path);
        StatusHashEntry verified = DescribeFile(path);
        if (verified != null && verified.Matches(current))
        {
            verified.sha256 = hash;
            WriteStatusHashCache(verified);
        }
        return hash;
    }

    private StatusHashEntry ReadStatusHashCache()
    {
        try
        {
            string cacheFile = Path.Combine(cacheRoot, StatusHashFileName);
            if (!File.Exists(cacheFile))
                return null;
            return new JavaScriptSerializer().Deserialize<StatusHashEntry>(File.ReadAllText(cacheFile));
        }
        catch
        {
            return null;
        }
    }

    private void WriteStatusHashCache(StatusHashEntry entry)
    {
        try
        {
            string cacheFile = Path.Combine(cacheRoot, StatusHashFileName);
            string temporary = cacheFile + ".new";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(entry), new UTF8Encoding(false));
            if (File.Exists(cacheFile))
                File.Delete(cacheFile);
            File.Move(temporary, cacheFile);
        }
        catch
        {
            // The hash cache is an optimization; a read-only cache directory must not break the status read.
        }
    }

    private static StatusHashEntry DescribeFile(string path)
    {
        try
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists)
                return null;
            return new StatusHashEntry
            {
                path = info.FullName,
                length = info.Length,
                creationUtcTicks = info.CreationTimeUtc.Ticks,
                lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks
            };
        }
        catch
        {
            return null;
        }
    }

    private static void WriteTextIfChanged(string path, string text)
    {
        if (File.Exists(path) && String.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
            return;
        File.WriteAllText(path, text, Encoding.ASCII);
    }

    private void ApplyStatus(SelectorStatus status)
    {
        currentSelection = status.CurrentSelection;
        currentNeedsUpdate = status.NeedsUpdate;
        baseVeilHidden = status.BaseVeilHidden;
        baseVeilSlotOccupied = status.BaseVeilSlotOccupied;
        factorySmokeHidden = status.FactorySmokeHidden;
        factorySmokeSlotOccupied = status.FactorySmokeSlotOccupied;
        namesHidden = status.NamesHidden;
        namesSlotOccupied = status.NamesSlotOccupied;
        bookHidden = status.BookHidden;
        bookSlotOccupied = status.BookSlotOccupied;
        playerMarkersHidden = status.PlayerMarkersHidden;
        gravesMarkersHidden = status.GravesMarkersHidden;
        playerMarkersSlotOccupied = status.PlayerMarkersSlotOccupied;
        healthLinesHidden = status.HealthLinesHidden;
        healthLinesSlotOccupied = status.HealthLinesSlotOccupied;
        classicFillEnabled = status.ClassicFillEnabled;
        classicFillNeedsUpdate = status.ClassicFillNeedsUpdate;
        classicFillSlotOccupied = status.ClassicFillSlotOccupied;
        colorFixEnabled = status.ColorFixEnabled;
        colorFixSlotOccupied = status.ColorFixSlotOccupied;
        if (veilSwitch != null)
            veilSwitch.IsOn = baseVeilHidden;
        if (smokeSwitch != null)
            smokeSwitch.IsOn = factorySmokeHidden;
        if (namesSwitch != null)
            namesSwitch.IsOn = namesHidden;
        if (bookSwitch != null)
            bookSwitch.IsOn = bookHidden;
        if (playerMarkersSwitch != null)
            playerMarkersSwitch.IsOn = playerMarkersHidden;
        if (gravesMarkersSwitch != null)
            gravesMarkersSwitch.IsOn = gravesMarkersHidden;
        if (healthLinesSwitch != null)
            healthLinesSwitch.IsOn = healthLinesHidden;
        if (classicFillSwitch != null)
            classicFillSwitch.IsOn = classicFillEnabled;
        if (colorFixSwitch != null)
            colorFixSwitch.IsOn = colorFixEnabled;
        SetFixState(veilStateLabel, baseVeilHidden, baseVeilSlotOccupied, "pak03_dir.vpk");
        SetFixState(smokeStateLabel, factorySmokeHidden, factorySmokeSlotOccupied, "pak04_dir.vpk");
        SetFixState(namesStateLabel, namesHidden, namesSlotOccupied, "pak05_dir.vpk");
        SetFixState(bookStateLabel, bookHidden, bookSlotOccupied, "pak06_dir.vpk");
        SetFixState(playerMarkersStateLabel, playerMarkersHidden, playerMarkersSlotOccupied, "pak07_dir.vpk");
        SetFixState(gravesMarkersStateLabel, gravesMarkersHidden, playerMarkersSlotOccupied, "pak07_dir.vpk");
        SetFixState(healthLinesStateLabel, healthLinesHidden, healthLinesSlotOccupied, "pak08_dir.vpk");
        if (classicFillStateLabel != null)
        {
            classicFillStateLabel.Text = classicFillSlotOccupied ? "pak09_dir.vpk belongs to another mod"
                : classicFillNeedsUpdate ? "Update available" : classicFillEnabled ? "Classic purple" : "Modern green";
            classicFillStateLabel.ForeColor = classicFillSlotOccupied ? UiTheme.Warning
                : classicFillNeedsUpdate ? UiTheme.Warning : classicFillEnabled ? UiTheme.Success : UiTheme.TextMuted;
        }
        if (colorFixStateLabel != null)
        {
            colorFixStateLabel.Text = colorFixSlotOccupied ? "pak10_dir.vpk belongs to another mod"
                : colorFixEnabled ? "Softer contrast" : "Original contrast";
            colorFixStateLabel.ForeColor = colorFixSlotOccupied ? UiTheme.Warning
                : colorFixEnabled ? UiTheme.Success : UiTheme.TextMuted;
        }
        foreach (KeyValuePair<string, SkyboxCard> entry in cards)
            entry.Value.IsActive = String.Equals(entry.Key, currentSelection, StringComparison.OrdinalIgnoreCase);

        if (status.UnknownFiles)
        {
            statusTitle.Text = "Foreign addon in the folder";
            statusDetail.Text = status.Detail;
            statusDot.Tone = UiTheme.Warning;
        }
        else if (!status.AddonsMounted)
        {
            statusTitle.Text = "GameInfo patch missing";
            statusDetail.Text = "Deadlock cannot mount a skybox until the patch is installed.";
            statusDot.Tone = UiTheme.Warning;
        }
        else if (currentSelection == "vanilla")
        {
            statusTitle.Text = "Stock sky";
            statusDetail.Text = "Deadlock is running its original skybox.";
            statusDot.Tone = UiTheme.Success;
        }
        else
        {
            statusTitle.Text = status.NeedsUpdate ? "Sky update available" : "Sky loaded";
            statusDetail.Text = status.NeedsUpdate
                ? status.Detail
                : GetDisplayName(FindVariant(currentSelection)) + " is live in the game.";
            statusDot.Tone = status.NeedsUpdate ? UiTheme.Warning : UiTheme.Success;
        }

        addonsMounted = status.AddonsMounted;
        if (selectedVariant != null)
            selectionDetail.Text = selectedVariant.id == currentSelection && !currentNeedsUpdate
                ? "Live right now" : "Ready to apply";
        UpdateButtons();
    }

    private static void SetFixState(Label label, bool hidden, bool occupied, string slot)
    {
        if (label == null)
            return;
        label.Text = occupied ? slot + " belongs to another mod" : hidden ? "Hidden" : "Visible";
        label.ForeColor = occupied ? UiTheme.Warning : hidden ? UiTheme.Success : UiTheme.TextMuted;
    }

    private void InstallComponent()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before applying gameinfo.gi.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(
            "Apply your saved gameinfo.gi? The current file will be backed up first.",
            "Apply saved GameInfo",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        RunOperation("Applying saved gameinfo.gi", delegate
        {
            return RunGameInfoInstaller();
        }, delegate
        {
            RefreshStatusAsync();
        });
    }

    private OperationResult RunGameInfoInstaller()
    {
        ProcessStartInfo info = CreateProcessInfo(
            Path.Combine(runtimeRoot, "DeadlockGameInfoInstaller.exe"),
            "--yes --no-pause --deadlock-root " + Quote(deadlockRoot));
        return RunProcess(info, GameInfoTimeoutMilliseconds);
    }

    private ProcessStartInfo CreateProcessInfo(string fileName, string arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = fileName;
        info.Arguments = arguments;
        info.WorkingDirectory = runtimeRoot;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        return info;
    }

    private ProcessStartInfo CreatePowerShellInfo(string scriptPath, string arguments)
    {
        ProcessStartInfo info = CreateProcessInfo(
            "powershell.exe",
            "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(scriptPath) + arguments);
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        return info;
    }

    /// <summary>
    /// Runs a helper and returns its combined output. The streams are drained asynchronously
    /// because a full pipe would otherwise block the child forever, and the wait is bounded so a
    /// stuck helper cannot leave the hidden operation running with no way out of "Working".
    /// </summary>
    private static OperationResult RunProcess(ProcessStartInfo info, int timeoutMilliseconds)
    {
        StringBuilder output = new StringBuilder();
        object gate = new object();
        using (Process process = new Process())
        using (System.Threading.ManualResetEvent outputClosed = new System.Threading.ManualResetEvent(false))
        using (System.Threading.ManualResetEvent errorClosed = new System.Threading.ManualResetEvent(false))
        {
            process.StartInfo = info;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null)
                    outputClosed.Set();
                else
                    lock (gate)
                        output.AppendLine(e.Data);
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null)
                    errorClosed.Set();
                else
                    lock (gate)
                        output.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                TryKill(process);
                lock (gate)
                {
                    return new OperationResult
                    {
                        ExitCode = -1,
                        Output = "ERROR: The helper did not finish within " +
                            (timeoutMilliseconds / 1000) + " seconds and was stopped." +
                            Environment.NewLine + output
                    };
                }
            }

            outputClosed.WaitOne(2000);
            errorClosed.WaitOne(2000);
            lock (gate)
            {
                return new OperationResult
                {
                    ExitCode = process.ExitCode,
                    Output = output.ToString().Trim()
                };
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
            process.WaitForExit(5000);
        }
        catch
        {
            // The helper may have exited between the timeout and the kill; nothing else to do.
        }
    }

    private void ApplySelection(string selection)
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing the skybox.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string label = selection == "vanilla"
            ? "Putting the stock sky back"
            : "Swapping in " + GetDisplayName(FindVariant(selection));
        RunOperation(
            label,
            delegate { return RunSelector("select", selection); },
            delegate { ApplySelectionInPlace(selection); });
    }

    private void ToggleBaseVeil()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing the base veil.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = baseVeilHidden ? "veil-show" : "veil-hide";
        RunOperation(
            baseVeilHidden ? "Restoring the base clouds" : "Hiding the base clouds",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void ToggleFactorySmoke()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing the factory smoke.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = factorySmokeHidden ? "smoke-show" : "smoke-hide";
        RunOperation(
            factorySmokeHidden ? "Restoring the factory smoke" : "Hiding the factory smoke",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void ToggleNames()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing unit names.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = namesHidden ? "names-show" : "names-hide";
        RunOperation(
            namesHidden ? "Restoring unit names" : "Hiding unit names",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void TogglePickupBook()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing the pickup book.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = bookHidden ? "book-show" : "book-hide";
        RunOperation(
            bookHidden ? "Restoring the pickup model" : "Hiding the pickup model",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void TogglePlayerMarkers()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing player markers.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = playerMarkersHidden ? "markers-show" : "markers-hide";
        RunOperation(
            playerMarkersHidden ? "Restoring player markers" : "Hiding player markers",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void ToggleGravesMarkers()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing Graves markers.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = gravesMarkersHidden ? "graves-show" : "graves-hide";
        RunOperation(
            gravesMarkersHidden ? "Restoring Graves markers" : "Hiding Graves markers",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void ToggleHealthLines()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing the HP-bar lines.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = healthLinesHidden ? "lines-show" : "lines-hide";
        RunOperation(
            healthLinesHidden ? "Restoring HP-bar lines" : "Hiding HP-bar lines",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void ToggleClassicFill()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing ability colors.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = classicFillEnabled ? "fill-show" : "fill-hide";
        RunOperation(
            classicFillEnabled ? "Restoring modern ability colors" : "Restoring classic purple ability fill",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void ToggleColorFix()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before changing ColorFix.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string action = colorFixEnabled ? "colorfix-off" : "colorfix-on";
        RunOperation(
            colorFixEnabled ? "Restoring original contrast" : "Applying softer contrast",
            delegate { return RunSelector(action, ""); },
            delegate { RefreshStatusAsync(); });
    }

    private void RunOperation(string detail, Func<OperationResult> operation, Action onSuccess)
    {
        SetWorking(true, "Working", detail);
        BackgroundWorker worker = new BackgroundWorker();
        OperationResult result = null;
        worker.DoWork += delegate { result = operation(); };
        worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                ShowError(e.Error);
                RefreshStatusAsync();
                return;
            }
            if (result == null || !result.Success)
            {
                string message = result == null
                    ? "The operation did not return a result."
                    : FirstUsefulLine(result.Output, "The operation failed.");
                MessageBox.Show(message, "Operation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshStatusAsync();
                return;
            }

            if (onSuccess != null)
                onSuccess();
        };
        worker.RunWorkerAsync();
    }

    private void ApplySelectionInPlace(string selection)
    {
        currentSelection = selection;
        currentNeedsUpdate = false;
        WriteTextIfChanged(
            Path.Combine(cacheRoot, "selected-skybox.txt"),
            selection + Environment.NewLine);

        foreach (KeyValuePair<string, SkyboxCard> entry in cards)
            entry.Value.IsActive = String.Equals(entry.Key, selection, StringComparison.OrdinalIgnoreCase);

        if (selection == "vanilla")
        {
            statusTitle.Text = "Stock sky";
            statusDetail.Text = "Deadlock is running its original skybox.";
        }
        else
        {
            statusTitle.Text = "Sky loaded";
            statusDetail.Text = GetDisplayName(FindVariant(selection)) + " is live in the game.";
        }
        statusDot.Tone = UiTheme.Success;
        selectionDetail.Text = selectedVariant != null &&
            String.Equals(selectedVariant.id, selection, StringComparison.OrdinalIgnoreCase)
            ? "Live right now"
            : "Ready to apply";
        SetWorking(false, statusTitle.Text, statusDetail.Text);
    }

    private OperationResult RunSelector(string action, string selection)
    {
        string script = Path.Combine(runtimeRoot, "select-skybox.ps1");
        StringBuilder arguments = new StringBuilder();
        arguments.Append(" -Action ").Append(action);
        if (!String.IsNullOrWhiteSpace(selection))
            arguments.Append(" -Selection ").Append(Quote(selection));
        arguments.Append(" -DeadlockRoot ").Append(Quote(deadlockRoot));
        arguments.Append(" -CacheRoot ").Append(Quote(cacheRoot));

        ProcessStartInfo info = CreatePowerShellInfo(script, arguments.ToString());
        info.EnvironmentVariables["DEADLOCK_ROOT"] = deadlockRoot;
        info.EnvironmentVariables["SKYBOX_CACHE_ROOT"] = cacheRoot;
        info.EnvironmentVariables["SKYBOX_ASSET_SHA256"] = assetHash;
        return RunProcess(info, SelectorTimeoutMilliseconds);
    }

    /// <summary>
    /// Reports whether the managed Deadlock install or a mod manager is running. Deadlock itself
    /// is matched by path so a same-named executable elsewhere cannot block the selector, and an
    /// unreadable path is treated as a match because guessing wrong would edit live game files.
    /// </summary>
    private bool IsManagedProcessRunning()
    {
        string managedRoot = Path.GetFullPath(deadlockRoot).TrimEnd('\\') + "\\";
        foreach (Process process in Process.GetProcessesByName("deadlock"))
        {
            using (process)
            {
                try
                {
                    if (Path.GetFullPath(process.MainModule.FileName)
                        .StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    return true;
                }
            }
        }

        foreach (string name in new[] { "dmm", "deadlock-modmanager" })
        {
            Process[] running = Process.GetProcessesByName(name);
            foreach (Process process in running)
                process.Dispose();
            if (running.Length > 0)
                return true;
        }
        return false;
    }

    private void SetWorking(bool value, string title, string detail)
    {
        working = value;
        statusTitle.Text = title;
        statusDetail.Text = detail;
        statusDot.Tone = value ? UiTheme.Warning : statusDot.Tone;
        cardScroll.Enabled = !value;
        Cursor = value ? Cursors.WaitCursor : Cursors.Default;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (applyButton == null)
            return;
        applyButton.Enabled = !working && selectedVariant != null &&
            (currentNeedsUpdate ||
                !String.Equals(selectedVariant.id, currentSelection, StringComparison.OrdinalIgnoreCase));
        if (veilSwitch != null)
            veilSwitch.Enabled = !working && addonsMounted && !baseVeilSlotOccupied &&
                !String.IsNullOrWhiteSpace(baseVeilOverrideHash);
        if (smokeSwitch != null)
            smokeSwitch.Enabled = !working && addonsMounted && !factorySmokeSlotOccupied &&
                !String.IsNullOrWhiteSpace(factorySmokeOverrideHash);
        if (namesSwitch != null)
            namesSwitch.Enabled = !working && addonsMounted && !namesSlotOccupied &&
                !String.IsNullOrWhiteSpace(hideNamesOverrideHash);
        if (bookSwitch != null)
            bookSwitch.Enabled = !working && addonsMounted && !bookSlotOccupied &&
                !String.IsNullOrWhiteSpace(hidePickupBookOverrideHash);
        if (playerMarkersSwitch != null)
            playerMarkersSwitch.Enabled = !working && addonsMounted && !playerMarkersSlotOccupied &&
                !String.IsNullOrWhiteSpace(hidePlayerMarkersOverrideHash) &&
                !String.IsNullOrWhiteSpace(hideCombinedMarkersOverrideHash);
        if (gravesMarkersSwitch != null)
            gravesMarkersSwitch.Enabled = !working && addonsMounted && !playerMarkersSlotOccupied &&
                !String.IsNullOrWhiteSpace(hideGravesMarkersOverrideHash) &&
                !String.IsNullOrWhiteSpace(hideCombinedMarkersOverrideHash);
        if (healthLinesSwitch != null)
            healthLinesSwitch.Enabled = !working && addonsMounted && !healthLinesSlotOccupied &&
                !String.IsNullOrWhiteSpace(hideHealthLinesOverrideHash);
        if (classicFillSwitch != null)
            classicFillSwitch.Enabled = !working && addonsMounted && !classicFillSlotOccupied &&
                !String.IsNullOrWhiteSpace(classicAbilityFillOverrideHash);
        if (colorFixSwitch != null)
            colorFixSwitch.Enabled = !working && addonsMounted && !colorFixSlotOccupied &&
                !String.IsNullOrWhiteSpace(colorFixOverrideHash);
        restoreButton.Enabled = !working && currentSelection != "vanilla";
        if (installButton != null)
            installButton.Enabled = !working;
    }

    private SkyboxVariant FindVariant(string id)
    {
        foreach (SkyboxVariant variant in variants)
        {
            if (String.Equals(variant.id, id, StringComparison.OrdinalIgnoreCase))
                return variant;
        }
        return null;
    }

    private static string GetDisplayName(SkyboxVariant variant)
    {
        return SkyboxNames.Get(variant);
    }

    private static string FirstUsefulLine(string text, string fallback)
    {
        if (String.IsNullOrWhiteSpace(text))
            return fallback;
        foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                return line.Substring(6).Trim();
        }
        foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.Length > 0)
                return line;
        }
        return fallback;
    }

    private static string GetDeepMessage(Exception error)
    {
        Exception current = error;
        while (current.InnerException != null)
            current = current.InnerException;
        return current.Message;
    }

    private void ShowError(Exception error)
    {
        MessageBox.Show(GetDeepMessage(error), "Deadlock Skybox Selector",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void DisposeCardImages()
    {
        entranceTimer.Dispose();
        revealTimer.Dispose();
        foreach (SkyboxCard card in cards.Values)
            card.DisposePreview();
        if (cardTips != null)
        {
            cardTips.RemoveAll();
            cardTips.Dispose();
            cardTips = null;
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}

internal sealed class SkyboxCard : UserControl
{
    // 186 + the 10 px right margin gives a 196 px pitch. The viewport the icon rail freed up offers
    // 997 usable pixels, so five cards fit with room to spare, and the row still degrades to four on
    // a narrower display. Interior geometry is derived from the size WinForms hands back, so only
    // the pitch changes when this constant does.
    //
    // The height is the 100 px thumbnail plus one line of name: the category caption that used to sit
    // under it is gone, so the band it held comes off the card rather than being left as empty space.
    // Shorter cards also mean the seven rows of the library scroll less.
    private const int DesignWidth = 186;
    private const int DesignHeight = 150;
    private const int LiftRoom = 3;
    private static readonly Font TitleFont = UiTheme.Heavy(11.5F);
    private static readonly Font ActiveFont = UiTheme.Heavy(7F);
    private static readonly HashSet<SkyboxCard> AnimatedCards = new HashSet<SkyboxCard>();
    private static readonly Timer SharedAnimationTimer = CreateAnimationTimer();
    private readonly SkyboxVariant variant;
    private readonly Image preview;
    private Image scaledPreview;
    private Size scaledPreviewSize;
    private bool selected;
    private bool active;
    private bool hovered;
    private float hoverAmount;
    private float selectionAmount;
    private float revealAmount;
    private bool revealing;

    public SkyboxVariant Variant
    {
        get { return variant; }
    }

    public bool IsSelected
    {
        get { return selected; }
        set
        {
            selected = value;
            StartAnimation();
        }
    }

    public bool IsActive
    {
        get { return active; }
        set
        {
            active = value;
            StartAnimation();
            Invalidate();
        }
    }

    public void BeginReveal()
    {
        revealing = true;
        StartAnimation();
    }

    public void ResetReveal()
    {
        revealAmount = 0F;
        revealing = false;
        AnimatedCards.Remove(this);
        Invalidate();
    }

    public void RevealImmediately()
    {
        revealAmount = 1F;
        revealing = true;
        AnimatedCards.Remove(this);
        Invalidate();
    }

    public SkyboxCard(SkyboxVariant variant, string previewPath)
    {
        this.variant = variant;
        preview = LoadThumbnail(previewPath, 420, 236);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        Size = new Size(DesignWidth, DesignHeight);
        MouseEnter += OnCardMouseEnter;
        MouseLeave += OnCardMouseLeave;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // The card paints itself, so its interior geometry has to follow the size WinForms
        // gave it after DPI scaling instead of the raw design constants.
        float scale = Math.Max(0.5F, (float)Height / DesignHeight);
        float reveal = UiTheme.EaseOutCubic(revealAmount);
        int slide = (int)((1F - reveal) * 12F * scale);
        // Hovering raises the card. Both the lift and its shadow stay inside a reserved margin so
        // the movement never clips against the neighbours or the viewport edge.
        float room = LiftRoom * scale;
        float lift = hoverAmount * 2.4F * scale;
        RectangleF cardBounds = new RectangleF(
            room,
            room + slide - lift,
            Math.Max(1F, Width - (room * 2F)),
            Math.Max(1F, Height - (room * 2F) - slide));
        float radius = 15F * scale;
        Color baseFill = UiTheme.Mix(UiTheme.Surface, UiTheme.SurfaceHover, hoverAmount * 0.72F);
        baseFill = UiTheme.Mix(baseFill, Color.FromArgb(68, 58, 36), selectionAmount * 0.8F);
        Color border = UiTheme.Mix(UiTheme.BorderSoft, UiTheme.Border, hoverAmount);
        border = UiTheme.Mix(border, UiTheme.Accent, selectionAmount);
        if (active && selectionAmount < 0.2F)
            border = UiTheme.Mix(border, UiTheme.Success, 0.76F);

        if (hoverAmount > 0.02F && reveal > 0.55F)
            UiTheme.DrawSoftShadow(graphics, cardBounds, radius, 3, (int)(54F * hoverAmount));

        using (GraphicsPath cardPath = UiTheme.RoundedPath(cardBounds, radius))
        {
            using (LinearGradientBrush fill = new LinearGradientBrush(
                new RectangleF(cardBounds.X, cardBounds.Y - 1F, cardBounds.Width, cardBounds.Height + 2F),
                UiTheme.Alpha(UiTheme.Mix(baseFill, Color.White, 0.05F), (int)(255F * reveal)),
                UiTheme.Alpha(UiTheme.Mix(baseFill, UiTheme.Background, 0.17F), (int)(255F * reveal)),
                LinearGradientMode.Vertical))
                graphics.FillPath(fill, cardPath);
            using (Pen pen = new Pen(UiTheme.Alpha(border, (int)(255F * reveal)), (selected ? 1.8F : 1F) * scale))
                graphics.DrawPath(pen, cardPath);
        }

        float pad = 8F * scale;
        Rectangle imageBounds = Rectangle.Round(new RectangleF(
            cardBounds.X + pad,
            cardBounds.Y + pad,
            Math.Max(1F, cardBounds.Width - (pad * 2F)),
            Math.Max(1F, 100F * scale)));
        Image image = GetScaledPreview(imageBounds.Size);
        using (GraphicsPath imagePath = UiTheme.RoundedPath(imageBounds, 11F * scale))
        {
            GraphicsState state = graphics.Save();
            graphics.SetClip(imagePath);
            if (reveal >= 0.995F)
            {
                graphics.DrawImage(image, imageBounds);
            }
            else
            {
                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix33 = reveal;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    graphics.DrawImage(image, imageBounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
            }

            // A scrim along the lower half of the thumbnail keeps the ACTIVE pill and the title
            // edge legible over bright skies without dimming the whole image. The brush covers one
            // pixel more than it fills, because a gradient brush tiles and would otherwise leave a
            // hard seam of the end colour along its own top edge.
            RectangleF scrimBounds = new RectangleF(
                imageBounds.X,
                imageBounds.Y + (imageBounds.Height * 0.46F),
                imageBounds.Width,
                imageBounds.Height * 0.54F);
            using (LinearGradientBrush scrim = new LinearGradientBrush(
                RectangleF.Inflate(scrimBounds, 0F, 1F),
                Color.Transparent,
                UiTheme.Alpha(Color.FromArgb(8, 13, 12), (int)(140F * reveal)),
                LinearGradientMode.Vertical))
                graphics.FillRectangle(scrim, scrimBounds);

            if (hoverAmount > 0.01F || selectionAmount > 0.01F)
            {
                Color glowColor = UiTheme.Mix(UiTheme.Accent, UiTheme.Violet, selectionAmount * 0.55F);
                using (LinearGradientBrush overlay = new LinearGradientBrush(
                    RectangleF.Inflate(imageBounds, 0F, 1F),
                    Color.Transparent,
                    UiTheme.Alpha(glowColor, (int)(55F * Math.Max(hoverAmount, selectionAmount))),
                    90F))
                    graphics.FillRectangle(overlay, imageBounds);
            }
            graphics.Restore(state);
        }
        // ClearType only once the card has finished fading in: it ignores the alpha the fade
        // depends on, so the crisp hint is saved for the frame where the text is fully opaque.
        graphics.TextRenderingHint = reveal >= 0.995F
            ? TextRenderingHint.ClearTypeGridFit
            : TextRenderingHint.AntiAliasGridFit;
        float textLeft = cardBounds.X + pad + scale;
        float textWidth = Math.Max(1F, cardBounds.Width - ((pad + scale) * 2F));
        // Typographic format, so GDI+ does not add its own em/6 bearing in front of the first glyph.
        // Without this the card title starts two pixels right of every other left edge in the window,
        // which is visible as soon as the cards line up under the page heading.
        using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
        using (SolidBrush titleBrush = new SolidBrush(UiTheme.Alpha(UiTheme.Text, (int)(255F * reveal))))
        {
            format.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.LineAlignment = StringAlignment.Center;
            graphics.DrawString(
                SkyboxNames.Get(variant),
                TitleFont,
                titleBrush,
                new RectangleF(textLeft, imageBounds.Bottom + (5F * scale), textWidth, 22F * scale),
                format);
        }
        if (active)
        {
            string activeLabel = UiTheme.Track("ACTIVE");
            SizeF labelSize = graphics.MeasureString(activeLabel, ActiveFont);
            RectangleF activePill = new RectangleF(
                imageBounds.X + (7F * scale),
                imageBounds.Bottom - (7F * scale) - (19F * scale),
                labelSize.Width + (11F * scale),
                19F * scale);
            using (GraphicsPath activePath = UiTheme.RoundedPath(activePill, 9.5F * scale))
            using (SolidBrush activeFill = new SolidBrush(UiTheme.Alpha(Color.FromArgb(14, 62, 47), (int)(238F * reveal))))
            using (Pen activeEdge = new Pen(UiTheme.Alpha(UiTheme.Success, (int)(110F * reveal))))
            using (SolidBrush activeText = new SolidBrush(UiTheme.Alpha(UiTheme.Success, (int)(255F * reveal))))
            using (StringFormat centered = new StringFormat())
            {
                centered.Alignment = StringAlignment.Center;
                centered.LineAlignment = StringAlignment.Center;
                graphics.FillPath(activeFill, activePath);
                graphics.DrawPath(activeEdge, activePath);
                graphics.DrawString(activeLabel, ActiveFont, activeText, activePill, centered);
            }
        }

        // The selected card gets a filled tick instead of relying on the border alone, which is
        // easy to miss next to the green "already installed" border.
        if (selectionAmount > 0.02F)
        {
            float badge = 22F * scale;
            RectangleF check = new RectangleF(
                imageBounds.Right - badge - (7F * scale),
                imageBounds.Y + (7F * scale),
                badge,
                badge);
            int badgeAlpha = (int)(255F * selectionAmount * reveal);
            using (SolidBrush disc = new SolidBrush(UiTheme.Alpha(UiTheme.Accent, badgeAlpha)))
                graphics.FillEllipse(disc, check);
            using (Pen tick = new Pen(UiTheme.Alpha(Color.FromArgb(22, 30, 25), badgeAlpha), 2F * scale))
            {
                tick.StartCap = LineCap.Round;
                tick.EndCap = LineCap.Round;
                graphics.DrawLines(tick, new PointF[]
                {
                    new PointF(check.X + (badge * 0.28F), check.Y + (badge * 0.51F)),
                    new PointF(check.X + (badge * 0.44F), check.Y + (badge * 0.68F)),
                    new PointF(check.X + (badge * 0.73F), check.Y + (badge * 0.33F))
                });
            }
        }
    }

    private void OnCardMouseEnter(object sender, EventArgs e)
    {
        hovered = true;
        StartAnimation();
    }

    private void OnCardMouseLeave(object sender, EventArgs e)
    {
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            hovered = false;
            StartAnimation();
        }
    }

    private bool AnimateFrame()
    {
        hoverAmount = Approach(hoverAmount, hovered ? 1F : 0F, 0.16F);
        selectionAmount = Approach(selectionAmount, selected ? 1F : 0F, 0.18F);
        revealAmount = Approach(revealAmount, revealing ? 1F : 0F, 0.12F);
        Invalidate();
        return Near(hoverAmount, hovered ? 1F : 0F) &&
            Near(selectionAmount, selected ? 1F : 0F) &&
            Near(revealAmount, revealing ? 1F : 0F);
    }

    private void StartAnimation()
    {
        if (IsDisposed || Disposing)
            return;
        AnimatedCards.Add(this);
        if (!SharedAnimationTimer.Enabled)
            SharedAnimationTimer.Start();
    }

    private static Timer CreateAnimationTimer()
    {
        Timer timer = new Timer();
        timer.Interval = 15;
        timer.Tick += delegate
        {
            if (AnimatedCards.Count == 0)
            {
                timer.Stop();
                return;
            }

            SkyboxCard[] cards = new SkyboxCard[AnimatedCards.Count];
            AnimatedCards.CopyTo(cards);
            foreach (SkyboxCard card in cards)
            {
                if (card.IsDisposed || card.Disposing || card.AnimateFrame())
                    AnimatedCards.Remove(card);
            }
            if (AnimatedCards.Count == 0)
                timer.Stop();
        };
        return timer;
    }

    private static float Approach(float value, float target, float speed)
    {
        return value + ((target - value) * speed);
    }

    private static bool Near(float value, float target)
    {
        return Math.Abs(value - target) < 0.012F;
    }

    private static Image LoadThumbnail(string path, int width, int height)
    {
        using (Image source = Image.FromFile(path))
            return Rescale(source, width, height);
    }

    /// <summary>
    /// Returns the preview already resampled to the size it is painted at. The cards animate on
    /// a 15 ms timer, so resampling the 420x236 preview down to the ~200x112 image area inside
    /// every paint would repeat the same downscale dozens of times per second per card.
    /// </summary>
    private Image GetScaledPreview(Size size)
    {
        if (size.Width < 1 || size.Height < 1)
            return preview;
        if (scaledPreview != null && scaledPreviewSize == size)
            return scaledPreview;
        if (preview.Width == size.Width && preview.Height == size.Height)
            return preview;

        Image rescaled;
        try
        {
            rescaled = Rescale(preview, size.Width, size.Height);
        }
        catch (Exception)
        {
            return preview;
        }

        if (scaledPreview != null)
            scaledPreview.Dispose();
        scaledPreview = rescaled;
        scaledPreviewSize = size;
        return scaledPreview;
    }

    private static Image Rescale(Image source, int width, int height)
    {
        Bitmap thumbnail = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(thumbnail))
        {
            graphics.Clear(Color.FromArgb(9, 10, 10));
            graphics.CompositingMode = CompositingMode.SourceCopy;
            if (source.Width == width && source.Height == height)
            {
                graphics.DrawImageUnscaled(source, 0, 0);
                return thumbnail;
            }
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = Math.Max((float)width / source.Width, (float)height / source.Height);
            int drawWidth = Math.Max(1, (int)(source.Width * scale));
            int drawHeight = Math.Max(1, (int)(source.Height * scale));
            int left = (width - drawWidth) / 2;
            int top = (height - drawHeight) / 2;
            graphics.DrawImage(source, new Rectangle(left, top, drawWidth, drawHeight));
        }
        return thumbnail;
    }

    public void DisposePreview()
    {
        AnimatedCards.Remove(this);
        if (scaledPreview != null)
        {
            scaledPreview.Dispose();
            scaledPreview = null;
        }
        preview.Dispose();
    }
}

/// <summary>A compact switch whose state reflects the installed fix, not an unconfirmed click.</summary>
internal sealed class FixToggle : Control
{
    private bool isOn;

    public bool IsOn
    {
        get { return isOn; }
        set
        {
            if (isOn == value)
                return;
            isOn = value;
            Invalidate();
        }
    }

    public FixToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Enabled && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        RoundedPanel panel = Parent as RoundedPanel;
        if (panel == null)
        {
            base.OnPaintBackground(e);
            return;
        }
        using (SolidBrush brush = new SolidBrush(panel.FillAt(Top)))
            e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF track = new RectangleF(1.5F, 2.5F, Width - 3F, Height - 5F);
        Color fill = !Enabled ? UiTheme.SurfaceRaised
            : isOn ? Color.FromArgb(52, 113, 97) : UiTheme.SurfaceHover;
        Color border = !Enabled ? UiTheme.BorderSoft
            : isOn ? UiTheme.Cyan : UiTheme.Border;
        using (GraphicsPath path = UiTheme.RoundedPath(track, track.Height / 2F))
        using (SolidBrush brush = new SolidBrush(fill))
        using (Pen pen = new Pen(border, 1.2F))
        {
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }

        float knobSize = track.Height - 7F;
        float knobX = isOn ? track.Right - knobSize - 3.5F : track.Left + 3.5F;
        RectangleF knob = new RectangleF(knobX, track.Top + 3.5F, knobSize, knobSize);
        using (SolidBrush brush = new SolidBrush(Enabled ? UiTheme.Text : UiTheme.TextDim))
            e.Graphics.FillEllipse(brush, knob);

        if (Focused)
        {
            RectangleF focus = new RectangleF(0.75F, 1.75F, Width - 1.5F, Height - 3.5F);
            using (GraphicsPath path = UiTheme.RoundedPath(focus, focus.Height / 2F))
            using (Pen pen = new Pen(UiTheme.Accent, 1F))
                e.Graphics.DrawPath(pen, path);
        }
    }
}

internal enum ActionButtonTone
{
    Install,
    Restore,
    Apply
}

internal sealed class ActionButton : Control
{
    private readonly Timer animationTimer;
    private ActionButtonTone tone;
    private bool hovered;
    private bool pressed;
    private float hoverAmount;
    private float pressAmount;
    private string tracked = "";

    public ActionButtonTone Tone
    {
        get { return tone; }
        set
        {
            tone = value;
            Invalidate();
        }
    }

    public ActionButton()
    {
        Cursor = Cursors.Hand;
        Font = UiTheme.Heavy(9F);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = true;
        animationTimer = new Timer();
        animationTimer.Interval = 16;
        animationTimer.Tick += Animate;
    }

    // Letter-spacing the caption is a string rebuild, so it happens when the caption changes rather
    // than inside the paint handler, which runs on every frame of the hover ramp.
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        tracked = UiTheme.Track(Text);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (Enabled)
        {
            hovered = true;
            StartAnimation();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        pressed = false;
        StartAnimation();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Enabled && e.Button == MouseButtons.Left)
        {
            pressed = true;
            StartAnimation();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        pressed = false;
        StartAnimation();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color baseColor;
        Color hoverColor;
        switch (tone)
        {
            case ActionButtonTone.Install:
                baseColor = Color.FromArgb(58, 112, 101);
                hoverColor = UiTheme.Cyan;
                break;
            case ActionButtonTone.Restore:
                baseColor = Color.FromArgb(120, 66, 44);
                hoverColor = UiTheme.Violet;
                break;
            default:
                baseColor = UiTheme.Accent;
                hoverColor = UiTheme.AccentHover;
                break;
        }

        Color visibleBase = Enabled
            ? baseColor
            : UiTheme.Mix(UiTheme.Surface, baseColor, 0.42F);
        Color fill = UiTheme.Mix(visibleBase, hoverColor, hoverAmount * 0.64F);
        fill = UiTheme.Mix(fill, UiTheme.Background, pressAmount * 0.20F);
        Color border = Enabled
            ? UiTheme.Mix(baseColor, hoverColor, 0.45F + (hoverAmount * 0.5F))
            : UiTheme.Mix(UiTheme.Border, baseColor, 0.58F);
        RectangleF bounds = new RectangleF(0.75F, 0.75F, Width - 1.5F, Height - 1.5F);
        using (GraphicsPath path = UiTheme.RoundedPath(bounds, 13F))
        {
            // A gentle vertical ramp rather than a flat block: lit along the top edge and settling
            // into the base tone at the bottom, which is what makes the pill read as raised.
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new RectangleF(bounds.X, bounds.Y - 1F, bounds.Width, bounds.Height + 2F),
                UiTheme.Mix(fill, Color.White, Enabled ? 0.14F : 0.05F),
                UiTheme.Mix(fill, UiTheme.Background, 0.1F),
                LinearGradientMode.Vertical))
                e.Graphics.FillPath(brush, path);
            using (Pen pen = new Pen(border, 1.2F))
                e.Graphics.DrawPath(pen, path);
        }
        if (Enabled)
        {
            using (Pen highlight = new Pen(UiTheme.Alpha(Color.White, 44 + (int)(30F * hoverAmount)), 1.1F))
                e.Graphics.DrawArc(highlight, bounds.X + 2F, bounds.Y + 1F, bounds.Width - 4F, 20F, 191F, 158F);
        }

        // Brass is a light fill, so the primary action carries dark type the way Deadlock's own
        // brass plates do; the darker tones keep the cream text.
        Color textColor;
        if (!Enabled)
            textColor = UiTheme.Mix(UiTheme.TextMuted, baseColor, 0.32F);
        else if (tone == ActionButtonTone.Apply)
            textColor = Color.FromArgb(28, 22, 8);
        else
            textColor = UiTheme.Text;
        Rectangle textBounds = Rectangle.Round(bounds);
        textBounds.Offset(0, (int)Math.Round(pressAmount));
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        TextRenderer.DrawText(e.Graphics, tracked, Font, textBounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        RoundedPanel panel = Parent as RoundedPanel;
        if (panel != null)
        {
            using (SolidBrush brush = new SolidBrush(panel.FillAt(Top)))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            return;
        }
        base.OnPaintBackground(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Enabled && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void Animate(object sender, EventArgs e)
    {
        hoverAmount += (((hovered && Enabled) ? 1F : 0F) - hoverAmount) * 0.18F;
        pressAmount += (((pressed && Enabled) ? 1F : 0F) - pressAmount) * 0.24F;
        Invalidate();
        if (Math.Abs(hoverAmount - ((hovered && Enabled) ? 1F : 0F)) < 0.012F &&
            Math.Abs(pressAmount - ((pressed && Enabled) ? 1F : 0F)) < 0.012F)
            animationTimer.Stop();
    }

    private void StartAnimation()
    {
        if (!animationTimer.Enabled)
            animationTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            animationTimer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// The state light in the status strip. A small panel with a background colour reads as a hard
/// pixel square, so the dot paints itself: an antialiased disc inside a soft halo of its own tone.
/// </summary>
internal sealed class StatusDot : Control
{
    private Color tone = UiTheme.Success;

    public Color Tone
    {
        get { return tone; }
        set
        {
            tone = value;
            Invalidate();
        }
    }

    public StatusDot()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(14, 14);
        TabStop = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        RoundedPanel panel = Parent as RoundedPanel;
        if (panel == null)
        {
            base.OnPaintBackground(e);
            return;
        }
        using (SolidBrush brush = new SolidBrush(panel.FillAt(Top)))
            e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Min(Width, Height);
        RectangleF halo = new RectangleF((Width - size) / 2F, (Height - size) / 2F, size, size);
        RectangleF core = RectangleF.Inflate(halo, -size * 0.28F, -size * 0.28F);
        using (SolidBrush ring = new SolidBrush(UiTheme.Alpha(tone, 56)))
            e.Graphics.FillEllipse(ring, halo);
        using (SolidBrush fill = new SolidBrush(tone))
            e.Graphics.FillEllipse(fill, core);
    }
}

internal enum RailIcon
{
    Skyboxes,
    Fixes,
    GameInfo,
    Settings
}

/// <summary>
/// Square icon tile for the navigation rail: a 15 ms hover ramp, an active state and keyboard
/// routing, carrying a vector glyph instead of a label so the rail stays narrow while more sections
/// are added.
/// </summary>
internal sealed class RailButton : Control
{
    private readonly Timer timer;
    private bool active;
    private bool hovered;
    private float hover;

    public RailIcon Icon { get; set; }

    public bool IsActive
    {
        get { return active; }
        set
        {
            active = value;
            Invalidate();
        }
    }

    public RailButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable |
            ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        ForeColor = UiTheme.TextMuted;
        TabStop = true;
        timer = new Timer();
        timer.Interval = 15;
        timer.Tick += Animate;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hovered = true;
        timer.Start();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        timer.Start();
    }

    private void Animate(object sender, EventArgs e)
    {
        float target = hovered ? 1F : 0F;
        hover += hover < target ? 0.2F : -0.2F;
        if (Math.Abs(hover - target) < 0.02F)
        {
            hover = target;
            timer.Stop();
        }
        hover = Math.Max(0F, Math.Min(1F, hover));
        Invalidate();
    }

    // The tile is not a Button, so Enter and Space have to be routed by hand.
    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        RoundedPanel panel = Parent as RoundedPanel;
        if (panel == null)
        {
            base.OnPaintBackground(e);
            return;
        }
        using (SolidBrush brush = new SolidBrush(panel.FillAt(Top)))
            e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
        using (GraphicsPath path = UiTheme.RoundedPath(bounds, 15F))
        {
            if (active)
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    bounds,
                    Color.FromArgb(74, 68, 42),
                    Color.FromArgb(52, 51, 36),
                    LinearGradientMode.Vertical))
                    graphics.FillPath(fill, path);
                using (Pen pen = new Pen(UiTheme.Alpha(UiTheme.Accent, 110)))
                    graphics.DrawPath(pen, path);
            }
            else if (hover > 0.01F)
            {
                using (SolidBrush fill = new SolidBrush(UiTheme.Alpha(UiTheme.SurfaceHover, (int)(74F * hover))))
                    graphics.FillPath(fill, path);
            }
            if (Focused)
            {
                using (Pen pen = new Pen(UiTheme.Alpha(UiTheme.Cyan, 130)))
                    graphics.DrawPath(pen, path);
            }
        }
        if (active)
        {
            using (GraphicsPath bar = UiTheme.RoundedPath(new RectangleF(-1F, Height / 2F - 10F, 3F, 20F), 1.5F))
            using (SolidBrush brush = new SolidBrush(UiTheme.Accent))
                graphics.FillPath(brush, bar);
        }

        Color tint = active ? UiTheme.AccentHover : UiTheme.Mix(UiTheme.TextMuted, UiTheme.Text, hover);
        GraphicsState state = graphics.Save();
        graphics.TranslateTransform((Width - 24F) / 2F, (Height - 24F) / 2F);
        DrawIcon(graphics, tint);
        graphics.Restore(state);
    }

    /// <summary>
    /// Glyphs are drawn as vectors in a 24x24 box rather than taken from an icon font: GDI+
    /// silently falls back to Microsoft Sans Serif for a missing family, which would turn a
    /// glyph range into stray characters on a machine without the font.
    /// </summary>
    private void DrawIcon(Graphics graphics, Color tint)
    {
        using (Pen pen = new Pen(tint, 1.7F))
        {
            pen.LineJoin = LineJoin.Round;
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            switch (Icon)
            {
                case RailIcon.Skyboxes:
                    using (GraphicsPath frame = UiTheme.RoundedPath(new RectangleF(2F, 3.5F, 20F, 17F), 3.5F))
                        graphics.DrawPath(pen, frame);
                    graphics.DrawEllipse(pen, 5.5F, 7F, 4F, 4F);
                    graphics.DrawLines(pen, new[]
                    {
                        new PointF(3F, 18F),
                        new PointF(8.5F, 12F),
                        new PointF(12.5F, 16F),
                        new PointF(16F, 12.5F),
                        new PointF(21F, 18.5F)
                    });
                    break;
                case RailIcon.Fixes:
                    using (SolidBrush bolt = new SolidBrush(tint))
                        graphics.FillPolygon(bolt, new[]
                        {
                            new PointF(14F, 2F),
                            new PointF(6.5F, 13.2F),
                            new PointF(11F, 13.2F),
                            new PointF(9.5F, 22F),
                            new PointF(17.5F, 10.4F),
                            new PointF(13F, 10.4F)
                        });
                    break;
                case RailIcon.GameInfo:
                    graphics.DrawLines(pen, new[]
                    {
                        new PointF(14.5F, 2.5F),
                        new PointF(5.5F, 2.5F),
                        new PointF(5.5F, 21.5F),
                        new PointF(18.5F, 21.5F),
                        new PointF(18.5F, 6.5F),
                        new PointF(14.5F, 2.5F),
                        new PointF(14.5F, 6.5F),
                        new PointF(18.5F, 6.5F)
                    });
                    graphics.DrawLine(pen, 9F, 12F, 15F, 12F);
                    graphics.DrawLine(pen, 9F, 16.5F, 15F, 16.5F);
                    break;
                case RailIcon.Settings:
                    graphics.DrawLine(pen, 3.5F, 6.5F, 20.5F, 6.5F);
                    graphics.DrawLine(pen, 3.5F, 12F, 20.5F, 12F);
                    graphics.DrawLine(pen, 3.5F, 17.5F, 20.5F, 17.5F);
                    using (SolidBrush knobFill = new SolidBrush(UiTheme.Background))
                    {
                        FillKnob(graphics, pen, knobFill, 8.5F, 6.5F);
                        FillKnob(graphics, pen, knobFill, 15.5F, 12F);
                        FillKnob(graphics, pen, knobFill, 10.5F, 17.5F);
                    }
                    break;
            }
        }
    }

    private static void FillKnob(Graphics graphics, Pen pen, Brush fill, float x, float y)
    {
        RectangleF knob = new RectangleF(x - 2.6F, y - 2.6F, 5.2F, 5.2F);
        graphics.FillEllipse(fill, knob);
        graphics.DrawEllipse(pen, knob);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop();
            timer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal enum WindowButtonKind
{
    Minimize,
    Maximize,
    Close
}

internal sealed class WindowButton : Control
{
    private readonly Timer timer;
    private bool hovered;
    private float hoverAmount;

    public WindowButtonKind Kind { get; set; }
    public Color HoverColor { get; set; }

    public WindowButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Kind = WindowButtonKind.Close;
        HoverColor = UiTheme.Accent;
        timer = new Timer();
        timer.Interval = 16;
        timer.Tick += delegate
        {
            hoverAmount += ((hovered ? 1F : 0F) - hoverAmount) * 0.2F;
            Invalidate();
            if (Math.Abs(hoverAmount - (hovered ? 1F : 0F)) < 0.01F)
                timer.Stop();
        };
        MouseEnter += delegate { hovered = true; timer.Start(); };
        MouseLeave += delegate { hovered = false; timer.Start(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF bounds = new RectangleF(1, 1, Width - 3, Height - 3);
        Color idleFill = UiTheme.Mix(UiTheme.SurfaceRaised, HoverColor, 0.17F);
        Color fill = UiTheme.Mix(idleFill, HoverColor, 0.34F * hoverAmount);
        Color border = UiTheme.Mix(UiTheme.Border, HoverColor, 0.34F + (0.38F * hoverAmount));
        using (GraphicsPath path = UiTheme.RoundedPath(bounds, 9F))
        using (SolidBrush brush = new SolidBrush(fill))
        {
            e.Graphics.FillPath(brush, path);
            using (Pen outline = new Pen(border, 1F))
                e.Graphics.DrawPath(outline, path);
        }

        Color glyph = UiTheme.Mix(UiTheme.TextMuted, HoverColor, 0.42F + (0.58F * hoverAmount));
        using (Pen pen = new Pen(glyph, 1.7F))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            float centerX = Width / 2F;
            float centerY = Height / 2F;
            if (Kind == WindowButtonKind.Minimize)
            {
                e.Graphics.DrawLine(pen, centerX - 5F, centerY + 3F, centerX + 5F, centerY + 3F);
            }
            else if (Kind == WindowButtonKind.Maximize)
            {
                e.Graphics.DrawRectangle(pen, centerX - 5F, centerY - 5F, 10F, 10F);
            }
            else
            {
                e.Graphics.DrawLine(pen, centerX - 4.5F, centerY - 4.5F, centerX + 4.5F, centerY + 4.5F);
                e.Graphics.DrawLine(pen, centerX + 4.5F, centerY - 4.5F, centerX - 4.5F, centerY + 4.5F);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            timer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class DoubleBufferedFlowPanel : FlowLayoutPanel
{
    public DoubleBufferedFlowPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}
