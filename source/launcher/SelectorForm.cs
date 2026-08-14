using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(19, 27, 26);
    public static readonly Color BackgroundSoft = Color.FromArgb(25, 37, 35);
    public static readonly Color Sidebar = Color.FromArgb(20, 30, 29);
    public static readonly Color Surface = Color.FromArgb(29, 43, 40);
    public static readonly Color SurfaceRaised = Color.FromArgb(37, 56, 52);
    public static readonly Color SurfaceHover = Color.FromArgb(48, 71, 65);
    public static readonly Color Border = Color.FromArgb(79, 105, 94);
    public static readonly Color BorderSoft = Color.FromArgb(49, 70, 64);
    public static readonly Color Text = Color.FromArgb(246, 237, 211);
    public static readonly Color TextMuted = Color.FromArgb(187, 177, 147);
    public static readonly Color TextDim = Color.FromArgb(115, 110, 92);
    public static readonly Color Accent = Color.FromArgb(222, 159, 65);
    public static readonly Color AccentHover = Color.FromArgb(246, 196, 101);
    public static readonly Color Violet = Color.FromArgb(193, 105, 69);
    public static readonly Color Cyan = Color.FromArgb(104, 180, 167);
    public static readonly Color Success = Color.FromArgb(109, 180, 129);
    public static readonly Color Warning = Color.FromArgb(228, 168, 73);
    public static readonly Color Danger = Color.FromArgb(208, 93, 75);

    public static Font Font(float size, FontStyle style)
    {
        try
        {
            return new Font("Ebrima", size, FontStyle.Bold, GraphicsUnit.Point);
        }
        catch
        {
            return new Font("Arial", size, FontStyle.Bold, GraphicsUnit.Point);
        }
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
                Color.FromArgb(47, 61, 55),
                UiTheme.Background,
                132F))
                graphics.FillRectangle(background, bounds);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawGlow(graphics, new PointF(bounds.Width * 0.12F, bounds.Height * 0.08F), 360F, UiTheme.Accent, 52);
            DrawGlow(graphics, new PointF(bounds.Width * 0.82F, bounds.Height * 0.78F), 440F, UiTheme.Violet, 38);
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
        using (Pen border = new Pen(UiTheme.Alpha(UiTheme.AccentHover, 220), 1.4F))
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
        using (SolidBrush fill = new SolidBrush(FillColor))
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
        content.Padding = new Padding(9, 9, 12, 12);
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
        title.Font = UiTheme.Font(14F, FontStyle.Bold);
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
        label.Font = UiTheme.Font(size, size >= 14F ? FontStyle.Bold : FontStyle.Regular);
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
        title.Font = UiTheme.Font(16F, FontStyle.Bold);
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
        author.Font = UiTheme.Font(8F, FontStyle.Regular);
        author.ForeColor = UiTheme.TextDim;
        author.Location = new Point(20, 236);
        author.Size = new Size(340, 20);
        author.Text = "MADE BY HARRITON";
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

internal sealed class SkyboxManifest
{
    public int formatVersion { get; set; }
    public SkyboxVariant[] variants { get; set; }
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
}

internal static class SkyboxNames
{
    private static readonly IDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "anime_01", "Golden Citadel" },
            { "anime_02", "Amber Rooftops" },
            { "anime_03", "Quiet Morning" },
            { "anime_04", "Soft Sunrise" },
            { "anime_05", "Azure City" },
            { "anime_06", "Golden Clouds" },
            { "anime_07", "Clear Horizon" },
            { "anime_08", "Blue Evening" },
            { "anime_09", "Starlit Night" },
            { "anime_10", "Mountain Air" },
            { "anime_11", "Cotton Candy" },
            { "anime_12", "Crystal Sky" },
            { "anime_13", "Bright Downtown" },
            { "realistic_01", "Silver Overcast" },
            { "realistic_02", "Burnished Gold" },
            { "realistic_03", "Rose Dusk" },
            { "realistic_04", "Morning Glow" },
            { "realistic_05", "Golden Hour" },
            { "realistic_06", "White Haze" },
            { "realistic_07", "Cloudbreak" },
            { "realistic_08", "Pale Noon" },
            { "realistic_09", "Clear Day" },
            { "realistic_10", "High Clouds" },
            { "realistic_11", "Storm Light" },
            { "realistic_12", "Grey Front" },
            { "realistic_13", "Blue Skies" },
            { "realistic_14", "Rainy Sunset" },
            { "realistic_15", "Ember Sunset" },
            { "realistic_16", "Fading Day" },
            { "realistic_17", "City Mist" },
            { "realistic_18", "Deep Fog" },
            { "realistic_19", "Nightlock" }
        };

    public static string Get(SkyboxVariant variant)
    {
        if (variant == null)
            return "Original Deadlock";
        string name;
        if (Names.TryGetValue(variant.id ?? "", out name))
            return name;
        if (!String.IsNullOrWhiteSpace(variant.displayName))
            return variant.displayName;
        return String.IsNullOrWhiteSpace(variant.id) ? "Skybox" : variant.id;
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
    public bool AddonsMounted;
    public bool UnknownFiles;
    public string Detail;
}

internal sealed class SelectorForm : Form
{
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
    private Panel statusDot;
    private ActionButton installButton;
    private ActionButton restoreGameInfoButton;
    private ActionButton fpsConfigButton;
    private ActionButton applyButton;
    private ActionButton restoreButton;
    private FilterButton allFilter;
    private SkyboxVariant selectedVariant;
    private string currentSelection = "vanilla";
    private bool working;
    private int revealIndex;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

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
            variantsByHash[variant.sha256] = variant;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Background;
        ClientSize = new Size(1200, 790);
        MaximumSize = new Size(1200, 790);
        MinimumSize = new Size(1200, 790);
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
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.Dock = DockStyle.Fill;
        shell.Margin = Padding.Empty;
        shell.Padding = Padding.Empty;
        shell.RowCount = 1;
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        dashboard.Controls.Add(shell);
        shell.BringToFront();
        titleBar.BringToFront();

        Panel sidebar = BuildSidebar();
        shell.Controls.Add(sidebar, 0, 0);

        TableLayoutPanel content = new TableLayoutPanel();
        content.BackColor = Color.Transparent;
        content.ColumnCount = 1;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.Dock = DockStyle.Fill;
        content.Padding = new Padding(26, 22, 26, 20);
        content.RowCount = 4;
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 94F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
        shell.Controls.Add(content, 1, 0);

        content.Controls.Add(BuildHeader(), 0, 0);
        content.Controls.Add(BuildStatusPanel(), 0, 1);

        RoundedPanel galleryFrame = new RoundedPanel();
        galleryFrame.BorderColor = UiTheme.BorderSoft;
        galleryFrame.CornerRadius = 18;
        galleryFrame.Dock = DockStyle.Fill;
        galleryFrame.FillColor = Color.FromArgb(27, 39, 37);
        galleryFrame.Margin = new Padding(0, 10, 0, 4);
        galleryFrame.Padding = new Padding(7);

        cardScroll = new SkyboxScrollHost();
        cardScroll.BackColor = galleryFrame.FillColor;
        cardScroll.Dock = DockStyle.Fill;
        cardScroll.Margin = Padding.Empty;
        cardGrid = cardScroll.Content;
        galleryFrame.Controls.Add(cardScroll);
        content.Controls.Add(galleryFrame, 0, 2);

        content.Controls.Add(BuildActionPanel(), 0, 3);
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
            control is FilterButton ||
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

    private Panel BuildSidebar()
    {
        RoundedPanel sidebar = new RoundedPanel();
        sidebar.BackColor = Color.Transparent;
        sidebar.BorderColor = UiTheme.BorderSoft;
        sidebar.CornerRadius = 22;
        sidebar.Dock = DockStyle.Fill;
        sidebar.FillColor = UiTheme.Sidebar;
        sidebar.Margin = new Padding(0, 10, 0, 10);
        sidebar.Padding = new Padding(20, 24, 20, 22);

        Label product = new Label();
        product.AutoSize = true;
        product.Font = UiTheme.Font(12F, FontStyle.Bold);
        product.ForeColor = UiTheme.Text;
        product.BackColor = Color.Transparent;
        product.Location = new Point(22, 23);
        product.Text = "DEADLOCK";
        sidebar.Controls.Add(product);

        Label productSub = new Label();
        productSub.AutoSize = true;
        productSub.Font = UiTheme.Font(7.5F, FontStyle.Regular);
        productSub.BackColor = Color.Transparent;
        productSub.ForeColor = UiTheme.Cyan;
        productSub.Location = new Point(23, 50);
        productSub.Text = "SKYBOX SELECTOR";
        sidebar.Controls.Add(productSub);

        Label library = new Label();
        library.AutoSize = true;
        library.Font = UiTheme.Font(8F, FontStyle.Bold);
        library.ForeColor = UiTheme.TextMuted;
        library.BackColor = Color.Transparent;
        library.Location = new Point(22, 111);
        library.Text = "LIBRARY";
        sidebar.Controls.Add(library);

        allFilter = CreateFilter("Skyboxes", "32", 142);
        sidebar.Controls.Add(allFilter);
        allFilter.IsActive = true;
        allFilter.Cursor = Cursors.Default;
        allFilter.TabStop = false;

        Panel separator = new Panel();
        separator.BackColor = UiTheme.BorderSoft;
        separator.Location = new Point(20, 210);
        separator.Size = new Size(180, 1);
        separator.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        sidebar.Controls.Add(separator);

        Label locationLabel = new Label();
        locationLabel.AutoSize = true;
        locationLabel.Font = UiTheme.Font(8F, FontStyle.Bold);
        locationLabel.ForeColor = UiTheme.TextMuted;
        locationLabel.BackColor = Color.Transparent;
        locationLabel.Location = new Point(22, 238);
        locationLabel.Text = "GAME LOCATION";
        sidebar.Controls.Add(locationLabel);

        Label location = new Label();
        location.AutoEllipsis = false;
        location.Font = UiTheme.Font(8F, FontStyle.Regular);
        location.BackColor = Color.Transparent;
        location.ForeColor = UiTheme.TextMuted;
        location.Location = new Point(22, 263);
        location.Text = deadlockRoot;
        Size locationSize = TextRenderer.MeasureText(deadlockRoot, location.Font, new Size(176, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
        location.Size = new Size(176, locationSize.Height + 2);
        sidebar.Controls.Add(location);

        ActionButton openFolder = new ActionButton();
        openFolder.Location = new Point(22, location.Bottom + 8);
        openFolder.Size = new Size(176, 36);
        openFolder.Text = "Open Deadlock folder";
        openFolder.Tone = ActionButtonTone.Install;
        openFolder.Click += delegate { OpenDeadlockFolder(); };
        sidebar.Controls.Add(openFolder);

        Label author = new Label();
        author.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        author.AutoSize = true;
        author.Font = UiTheme.Font(7.5F, FontStyle.Regular);
        author.BackColor = Color.Transparent;
        author.ForeColor = UiTheme.TextDim;
        author.Location = new Point(22, ClientSize.Height - 118);
        author.Text = "MADE BY HARRITON";
        sidebar.Controls.Add(author);

        sidebar.Resize += delegate
        {
            author.Top = sidebar.ClientSize.Height - 38;
        };
        return sidebar;
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

    private FilterButton CreateFilter(string text, string count, int top)
    {
        FilterButton button = new FilterButton();
        button.Location = new Point(14, top);
        button.Size = new Size(192, 40);
        button.Text = text;
        button.CountText = count;
        return button;
    }

    private Control BuildHeader()
    {
        Panel header = new Panel();
        header.BackColor = Color.Transparent;
        header.Dock = DockStyle.Fill;

        Label title = new Label();
        title.AutoSize = true;
        title.BackColor = Color.Transparent;
        title.Font = UiTheme.Font(21F, FontStyle.Bold);
        title.ForeColor = UiTheme.Text;
        title.Location = new Point(0, 2);
        title.Text = "Welcome back,";
        header.Controls.Add(title);

        Label user = new Label();
        user.AutoSize = true;
        user.BackColor = Color.Transparent;
        user.Font = UiTheme.Font(21F, FontStyle.Bold);
        user.ForeColor = UiTheme.AccentHover;
        user.Location = new Point(title.Right, 2);
        string windowsUser = Environment.UserName;
        if (String.IsNullOrWhiteSpace(windowsUser))
            windowsUser = "user";
        user.Text = windowsUser.Trim() + "!";
        header.Controls.Add(user);
        title.SizeChanged += delegate
        {
            Size measured = TextRenderer.MeasureText(title.Text, title.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            user.Left = title.Left + measured.Width + 3;
        };
        Size titleSize = TextRenderer.MeasureText(title.Text, title.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        user.Left = title.Left + titleSize.Width + 3;

        Label subtitle = new Label();
        subtitle.AutoSize = true;
        subtitle.BackColor = Color.Transparent;
        subtitle.Font = UiTheme.Font(9F, FontStyle.Regular);
        subtitle.ForeColor = UiTheme.TextMuted;
        subtitle.Location = new Point(2, 48);
        subtitle.Text = "Your skybox library is ready. Choose a new atmosphere for Deadlock.";
        header.Controls.Add(subtitle);

        return header;
    }

    private Control BuildStatusPanel()
    {
        RoundedPanel panel = new RoundedPanel();
        panel.BorderColor = UiTheme.BorderSoft;
        panel.CornerRadius = 17;
        panel.Dock = DockStyle.Fill;
        panel.DrawTopGlow = true;
        panel.FillColor = Color.FromArgb(34, 49, 45);
        panel.Margin = new Padding(0, 0, 0, 8);
        panel.Padding = new Padding(18, 12, 18, 12);

        statusDot = new Panel();
        statusDot.BackColor = UiTheme.Warning;
        statusDot.Location = new Point(20, 23);
        statusDot.Size = new Size(9, 9);
        panel.Controls.Add(statusDot);

        statusTitle = new Label();
        statusTitle.AutoSize = true;
        statusTitle.BackColor = Color.Transparent;
        statusTitle.Font = UiTheme.Font(10F, FontStyle.Bold);
        statusTitle.ForeColor = UiTheme.Text;
        statusTitle.Location = new Point(42, 14);
        statusTitle.Text = "Checking installation";
        panel.Controls.Add(statusTitle);

        statusDetail = new Label();
        statusDetail.AutoEllipsis = true;
        statusDetail.BackColor = Color.Transparent;
        statusDetail.Font = UiTheme.Font(8F, FontStyle.Regular);
        statusDetail.ForeColor = UiTheme.TextMuted;
        statusDetail.Location = new Point(43, 38);
        statusDetail.Size = new Size(620, 18);
        statusDetail.Text = "Reading the current selector state";
        panel.Controls.Add(statusDetail);

        selectionTitle = new Label();
        selectionTitle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        selectionTitle.Font = UiTheme.Font(9F, FontStyle.Bold);
        selectionTitle.BackColor = Color.Transparent;
        selectionTitle.ForeColor = UiTheme.Cyan;
        selectionTitle.Location = new Point(panel.Width - 270, 14);
        selectionTitle.Size = new Size(245, 20);
        selectionTitle.Text = "NOT SELECTED";
        selectionTitle.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(selectionTitle);

        selectionDetail = new Label();
        selectionDetail.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        selectionDetail.BackColor = Color.Transparent;
        selectionDetail.Font = UiTheme.Font(8F, FontStyle.Regular);
        selectionDetail.ForeColor = UiTheme.TextMuted;
        selectionDetail.Location = new Point(panel.Width - 270, 38);
        selectionDetail.Size = new Size(245, 18);
        selectionDetail.Text = "Choose a card below";
        selectionDetail.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(selectionDetail);
        panel.Resize += delegate
        {
            selectionTitle.Left = panel.ClientSize.Width - selectionTitle.Width - 20;
            selectionDetail.Left = panel.ClientSize.Width - selectionDetail.Width - 20;
        };
        return panel;
    }

    private Control BuildActionPanel()
    {
        RoundedPanel panel = new RoundedPanel();
        panel.BorderColor = UiTheme.BorderSoft;
        panel.CornerRadius = 18;
        panel.Dock = DockStyle.Fill;
        panel.FillColor = Color.FromArgb(30, 44, 41);
        panel.Margin = new Padding(0, 10, 0, 0);

        installButton = new ActionButton();
        installButton.Location = new Point(18, 20);
        installButton.Size = new Size(176, 44);
        installButton.Text = "Install component";
        installButton.Tone = ActionButtonTone.Install;
        installButton.Click += delegate { InstallComponent(); };
        panel.Controls.Add(installButton);

        restoreGameInfoButton = new ActionButton();
        restoreGameInfoButton.AccessibleName = "Restore default GameInfo";
        restoreGameInfoButton.Location = new Point(204, 20);
        restoreGameInfoButton.Size = new Size(86, 44);
        restoreGameInfoButton.Text = "Default GI";
        restoreGameInfoButton.Tone = ActionButtonTone.Restore;
        restoreGameInfoButton.Click += delegate { RestoreDefaultGameInfo(); };
        panel.Controls.Add(restoreGameInfoButton);

        fpsConfigButton = new ActionButton();
        fpsConfigButton.Location = new Point(302, 20);
        fpsConfigButton.Size = new Size(180, 44);
        fpsConfigButton.Text = "Install FPS config";
        fpsConfigButton.Tone = ActionButtonTone.Install;
        fpsConfigButton.Click += delegate { InstallFpsConfig(); };
        panel.Controls.Add(fpsConfigButton);

        restoreButton = new ActionButton();
        restoreButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        restoreButton.Tone = ActionButtonTone.Restore;
        restoreButton.Location = new Point(panel.Width - 330, 20);
        restoreButton.Size = new Size(145, 44);
        restoreButton.Text = "Restore";
        restoreButton.Click += delegate { ApplySelection("vanilla"); };
        panel.Controls.Add(restoreButton);

        applyButton = new ActionButton();
        applyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        applyButton.Location = new Point(panel.Width - 170, 20);
        applyButton.Size = new Size(152, 44);
        applyButton.Text = "Apply";
        applyButton.Tone = ActionButtonTone.Apply;
        applyButton.Click += delegate
        {
            if (selectedVariant != null)
                ApplySelection(selectedVariant.id);
        };
        panel.Controls.Add(applyButton);

        panel.Resize += delegate
        {
            applyButton.Left = panel.ClientSize.Width - applyButton.Width - 18;
            restoreButton.Left = applyButton.Left - restoreButton.Width - 14;
        };
        UpdateButtons();
        return panel;
    }

    private void BuildCards()
    {
        cardGrid.SuspendLayout();
        foreach (SkyboxVariant variant in variants)
        {
            string previewPath = ResolveThumbnailPath(variant);
            SkyboxCard card = new SkyboxCard(variant, previewPath);
            card.Margin = new Padding(0, 0, 14, 14);
            card.Click += delegate { SelectVariant(variant); };
            cards.Add(variant.id, card);
            cardGrid.Controls.Add(card);
        }
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

        selectionTitle.Text = GetDisplayName(variant).ToUpperInvariant();
        selectionDetail.Text = String.Equals(variant.id, currentSelection, StringComparison.OrdinalIgnoreCase)
            ? "Currently active"
            : "Ready to apply";
        UpdateButtons();
    }

    private void RefreshStatusAsync()
    {
        SetWorking(true, "Checking installation", "Reading the current selector state");
        BackgroundWorker worker = new BackgroundWorker();
        SelectorStatus status = null;
        worker.DoWork += delegate { status = ReadStatus(); };
        worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                ShowError(e.Error);
                SetWorking(false, "Status unavailable", GetDeepMessage(e.Error));
                return;
            }
            SetWorking(false, "Ready", "Installation status loaded.");
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
        bool legacyPresent = false;
        bool unknownFiles = false;

        if (File.Exists(managedTarget))
        {
            string hash = ComputeFileSha256(managedTarget);
            SkyboxVariant current;
            if (variantsByHash.TryGetValue(hash, out current))
                status.CurrentSelection = current.id;
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
            status.Detail = legacyPresent
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

    private static void WriteTextIfChanged(string path, string text)
    {
        if (File.Exists(path) && String.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
            return;
        File.WriteAllText(path, text, Encoding.ASCII);
    }

    private void ApplyStatus(SelectorStatus status)
    {
        currentSelection = status.CurrentSelection;
        foreach (KeyValuePair<string, SkyboxCard> entry in cards)
            entry.Value.IsActive = String.Equals(entry.Key, currentSelection, StringComparison.OrdinalIgnoreCase);

        if (status.UnknownFiles)
        {
            statusTitle.Text = "External addon detected";
            statusDetail.Text = status.Detail;
            statusDot.BackColor = UiTheme.Warning;
        }
        else if (!status.AddonsMounted)
        {
            statusTitle.Text = "Component required";
            statusDetail.Text = "Install the GameInfo component before applying a skybox.";
            statusDot.BackColor = UiTheme.Warning;
        }
        else if (currentSelection == "vanilla")
        {
            statusTitle.Text = "Ready";
            statusDetail.Text = "The original Deadlock skybox is active.";
            statusDot.BackColor = UiTheme.Success;
        }
        else
        {
            statusTitle.Text = "Skybox active";
            statusDetail.Text = GetDisplayName(FindVariant(currentSelection)) + " is installed.";
            statusDot.BackColor = UiTheme.Success;
        }

        installButton.Text = status.AddonsMounted ? "Component installed" : "Install component";
        installButton.Enabled = !status.AddonsMounted;
        RefreshFpsConfigState();
        if (selectedVariant != null)
            selectionDetail.Text = selectedVariant.id == currentSelection ? "Currently active" : "Ready to apply";
        UpdateButtons();
    }

    private void InstallComponent()
    {
        if (MessageBox.Show(
            "Install the required GameInfo component? A verified backup will be created before replacement.",
            "Install component",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        RunOperation("Installing component", delegate
        {
            return RunGameInfoInstaller(false);
        }, delegate
        {
            installButton.Text = "Component installed";
            installButton.Enabled = false;
            SetWorking(false, "Ready", "GameInfo component installed successfully.");
        });
    }

    private void RestoreDefaultGameInfo()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock and Deadlock Mod Manager before restoring GameInfo.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!HasOriginalGameInfoBackup())
        {
            MessageBox.Show("The original GameInfo backup was not found. Install the component once before using restore.",
                "Backup not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(
            "Restore the original gameinfo.gi from the first verified backup? The current file will also be backed up.",
            "Restore default GameInfo",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        RunOperation("Restoring default GameInfo", delegate
        {
            return RunGameInfoInstaller(true);
        }, delegate
        {
            SetWorking(false, "GameInfo restored", "The original gameinfo.gi has been restored.");
            RefreshStatusAsync();
        });
    }

    private OperationResult RunGameInfoInstaller(bool restore)
    {
        string installer = Path.Combine(runtimeRoot, "DeadlockGameInfoInstaller.exe");
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = installer;
        info.Arguments = "--yes --no-pause" + (restore ? " --restore" : "") +
            " --deadlock-root " + Quote(deadlockRoot);
        info.WorkingDirectory = runtimeRoot;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        using (Process process = Process.Start(info))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new OperationResult
            {
                ExitCode = process.ExitCode,
                Output = output + Environment.NewLine + error
            };
        }
    }

    private bool HasOriginalGameInfoBackup()
    {
        try
        {
            string directory = Path.Combine(deadlockRoot, "game", "citadel");
            return Directory.Exists(directory) &&
                Directory.GetFiles(directory, "gameinfo.gi.patchwin-backup-*", SearchOption.TopDirectoryOnly).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void InstallFpsConfig()
    {
        if (IsManagedProcessRunning())
        {
            MessageBox.Show("Close Deadlock before installing the FPS config.",
                "Deadlock is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RunOperation("Installing FPS config", delegate
        {
            return RunFpsConfig("install");
        }, delegate
        {
            fpsConfigButton.Text = "FPS config installed";
            fpsConfigButton.Enabled = false;
            SetWorking(false, "FPS config ready", "Safe runtime optimizations are installed.");
        });
    }

    private void RefreshFpsConfigState()
    {
        bool installed = IsFpsConfigInstalled();
        fpsConfigButton.Text = installed ? "FPS config installed" : "Install FPS config";
        fpsConfigButton.Enabled = !working && !installed;
    }

    private bool IsFpsConfigInstalled()
    {
        string autoexec = Path.Combine(deadlockRoot, "game", "citadel", "cfg", "autoexec.cfg");
        string profile = Path.Combine(runtimeRoot, "deadlock-fps.cfg");
        if (!File.Exists(autoexec) || !File.Exists(profile))
            return false;

        const string start = "// Deadlock Skybox Selector FPS profile - start";
        const string end = "// Deadlock Skybox Selector FPS profile - end";
        string text = File.ReadAllText(autoexec).Replace("\r\n", "\n").Replace('\r', '\n');
        string expected = File.ReadAllText(profile).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        Match block = Regex.Match(
            text,
            "(?ms)^" + Regex.Escape(start) + ".*?^" + Regex.Escape(end));
        return block.Success && block.Value.Contains(expected);
    }

    private OperationResult RunFpsConfig(string action)
    {
        string script = Path.Combine(runtimeRoot, "install-fps-config.ps1");
        string profile = Path.Combine(runtimeRoot, "deadlock-fps.cfg");
        StringBuilder arguments = new StringBuilder();
        arguments.Append("-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ");
        arguments.Append(Quote(script));
        arguments.Append(" -Action ").Append(action);
        arguments.Append(" -DeadlockRoot ").Append(Quote(deadlockRoot));
        arguments.Append(" -ProfilePath ").Append(Quote(profile));

        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = "powershell.exe";
        info.Arguments = arguments.ToString();
        info.WorkingDirectory = runtimeRoot;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        using (Process process = Process.Start(info))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new OperationResult
            {
                ExitCode = process.ExitCode,
                Output = (output + Environment.NewLine + error).Trim()
            };
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

        string label = selection == "vanilla" ? "Restoring original skybox" : "Applying " + GetDisplayName(FindVariant(selection));
        RunOperation(
            label,
            delegate { return RunSelector("select", selection); },
            delegate { ApplySelectionInPlace(selection); });
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
        WriteTextIfChanged(
            Path.Combine(cacheRoot, "selected-skybox.txt"),
            selection + Environment.NewLine);

        foreach (KeyValuePair<string, SkyboxCard> entry in cards)
            entry.Value.IsActive = String.Equals(entry.Key, selection, StringComparison.OrdinalIgnoreCase);

        if (selection == "vanilla")
        {
            statusTitle.Text = "Ready";
            statusDetail.Text = "The original Deadlock skybox is active.";
        }
        else
        {
            statusTitle.Text = "Skybox active";
            statusDetail.Text = GetDisplayName(FindVariant(selection)) + " is installed.";
        }
        statusDot.BackColor = UiTheme.Success;
        selectionDetail.Text = selectedVariant != null &&
            String.Equals(selectedVariant.id, selection, StringComparison.OrdinalIgnoreCase)
            ? "Currently active"
            : "Ready to apply";
        SetWorking(false, statusTitle.Text, statusDetail.Text);
    }

    private OperationResult RunSelector(string action, string selection)
    {
        string script = Path.Combine(runtimeRoot, "select-skybox.ps1");
        StringBuilder arguments = new StringBuilder();
        arguments.Append("-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ");
        arguments.Append(Quote(script));
        arguments.Append(" -Action ").Append(action);
        if (!String.IsNullOrWhiteSpace(selection))
            arguments.Append(" -Selection ").Append(Quote(selection));
        arguments.Append(" -DeadlockRoot ").Append(Quote(deadlockRoot));
        arguments.Append(" -CacheRoot ").Append(Quote(cacheRoot));

        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = "powershell.exe";
        info.Arguments = arguments.ToString();
        info.WorkingDirectory = runtimeRoot;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.EnvironmentVariables["DEADLOCK_ROOT"] = deadlockRoot;
        info.EnvironmentVariables["SKYBOX_CACHE_ROOT"] = cacheRoot;
        info.EnvironmentVariables["SKYBOX_ASSET_SHA256"] = assetHash;

        using (Process process = Process.Start(info))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new OperationResult
            {
                ExitCode = process.ExitCode,
                Output = (output + Environment.NewLine + error).Trim()
            };
        }
    }

    private bool IsManagedProcessRunning()
    {
        foreach (string name in new[] { "deadlock", "dmm", "deadlock-modmanager" })
        {
            if (Process.GetProcessesByName(name).Length > 0)
                return true;
        }
        return false;
    }

    private void SetWorking(bool value, string title, string detail)
    {
        working = value;
        statusTitle.Text = title;
        statusDetail.Text = detail;
        statusDot.BackColor = value ? UiTheme.Warning : statusDot.BackColor;
        cardScroll.Enabled = !value;
        if (value && fpsConfigButton != null)
            fpsConfigButton.Enabled = false;
        Cursor = value ? Cursors.WaitCursor : Cursors.Default;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (applyButton == null)
            return;
        applyButton.Enabled = !working && selectedVariant != null &&
            !String.Equals(selectedVariant.id, currentSelection, StringComparison.OrdinalIgnoreCase);
        restoreButton.Enabled = !working && currentSelection != "vanilla";
        if (fpsConfigButton != null && !working)
            fpsConfigButton.Enabled = !IsFpsConfigInstalled();
        if (restoreGameInfoButton != null)
            restoreGameInfoButton.Enabled = !working && HasOriginalGameInfoBackup();
        if (working)
            installButton.Enabled = false;
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
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}

internal sealed class SkyboxCard : UserControl
{
    private static readonly Font TitleFont = UiTheme.Font(9.5F, FontStyle.Bold);
    private static readonly Font ActiveFont = UiTheme.Font(6.7F, FontStyle.Bold);
    private static readonly HashSet<SkyboxCard> AnimatedCards = new HashSet<SkyboxCard>();
    private static readonly Timer SharedAnimationTimer = CreateAnimationTimer();
    private readonly SkyboxVariant variant;
    private readonly Image preview;
    private bool selected;
    private bool active;
    private bool hovered;
    private float hoverAmount;
    private float selectionAmount;
    private float revealAmount;
    private bool revealing;

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
        Size = new Size(218, 176);
        MouseEnter += OnCardMouseEnter;
        MouseLeave += OnCardMouseLeave;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float reveal = UiTheme.EaseOutCubic(revealAmount);
        int slide = (int)((1F - reveal) * 12F);
        RectangleF cardBounds = new RectangleF(0.75F, 0.75F + slide, Width - 1.5F, Height - 1.5F - slide);
        Color baseFill = UiTheme.Mix(UiTheme.Surface, UiTheme.SurfaceHover, hoverAmount * 0.72F);
        baseFill = UiTheme.Mix(baseFill, Color.FromArgb(68, 58, 36), selectionAmount * 0.8F);
        Color border = UiTheme.Mix(UiTheme.BorderSoft, UiTheme.Border, hoverAmount);
        border = UiTheme.Mix(border, UiTheme.Accent, selectionAmount);
        if (active && selectionAmount < 0.2F)
            border = UiTheme.Mix(border, UiTheme.Success, 0.76F);

        using (GraphicsPath cardPath = UiTheme.RoundedPath(cardBounds, 15F))
        using (SolidBrush fill = new SolidBrush(UiTheme.Alpha(baseFill, (int)(255F * reveal))))
        using (Pen pen = new Pen(UiTheme.Alpha(border, (int)(255F * reveal)), selected ? 1.8F : 1F))
        {
            e.Graphics.FillPath(fill, cardPath);
            e.Graphics.DrawPath(pen, cardPath);
        }

        Rectangle imageBounds = new Rectangle(9, 9 + slide, Width - 18, 112);
        using (GraphicsPath imagePath = UiTheme.RoundedPath(imageBounds, 11F))
        {
            GraphicsState state = e.Graphics.Save();
            e.Graphics.SetClip(imagePath);
            if (reveal >= 0.995F)
            {
                e.Graphics.DrawImage(preview, imageBounds);
            }
            else
            {
                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix33 = reveal;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    e.Graphics.DrawImage(preview, imageBounds, 0, 0, preview.Width, preview.Height, GraphicsUnit.Pixel, attributes);
                }
            }

            if (hoverAmount > 0.01F || selectionAmount > 0.01F)
            {
                Color glowColor = UiTheme.Mix(UiTheme.Accent, UiTheme.Violet, selectionAmount * 0.55F);
                using (LinearGradientBrush overlay = new LinearGradientBrush(
                    imageBounds,
                    Color.Transparent,
                    UiTheme.Alpha(glowColor, (int)(55F * Math.Max(hoverAmount, selectionAmount))),
                    90F))
                    e.Graphics.FillRectangle(overlay, imageBounds);
            }
            e.Graphics.Restore(state);
        }

        using (SolidBrush titleBrush = new SolidBrush(UiTheme.Alpha(UiTheme.Text, (int)(255F * reveal))))
            e.Graphics.DrawString(SkyboxNames.Get(variant), TitleFont, titleBrush, new RectangleF(11, 132 + slide, Width - 22, 24));

        if (active)
        {
            RectangleF activePill = new RectangleF(13, 13 + slide, 56, 20);
            using (GraphicsPath activePath = UiTheme.RoundedPath(activePill, 10F))
            using (SolidBrush activeFill = new SolidBrush(UiTheme.Alpha(Color.FromArgb(18, 73, 55), (int)(245F * reveal))))
            using (SolidBrush activeText = new SolidBrush(UiTheme.Alpha(UiTheme.Success, (int)(255F * reveal))))
            {
                e.Graphics.FillPath(activeFill, activePath);
                e.Graphics.DrawString("ACTIVE", ActiveFont, activeText, activePill.X + 8, activePill.Y + 4);
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
    }

    public void DisposePreview()
    {
        AnimatedCards.Remove(this);
        preview.Dispose();
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
        Font = UiTheme.Font(9F, FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = true;
        animationTimer = new Timer();
        animationTimer.Interval = 16;
        animationTimer.Tick += Animate;
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
                baseColor = Color.FromArgb(65, 125, 113);
                hoverColor = UiTheme.Cyan;
                break;
            case ActionButtonTone.Restore:
                baseColor = Color.FromArgb(142, 82, 56);
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
        using (GraphicsPath path = UiTheme.RoundedPath(bounds, 12F))
        using (SolidBrush brush = new SolidBrush(fill))
        using (Pen pen = new Pen(border, 1.2F))
        {
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }

        Color textColor = Enabled ? UiTheme.Text : UiTheme.Mix(UiTheme.TextMuted, baseColor, 0.32F);
        Rectangle textBounds = Rectangle.Round(bounds);
        textBounds.Offset(0, (int)Math.Round(pressAmount));
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        RoundedPanel panel = Parent as RoundedPanel;
        if (panel != null)
        {
            using (SolidBrush brush = new SolidBrush(panel.FillColor))
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

internal sealed class FilterButton : Button
{
    private static readonly Font CountFont = UiTheme.Font(8F, FontStyle.Regular);
    private bool active;

    public string CountText { get; set; }

    public bool IsActive
    {
        get { return active; }
        set
        {
            active = value;
            BackColor = active ? Color.FromArgb(67, 63, 39) : UiTheme.Sidebar;
            ForeColor = active ? UiTheme.Text : UiTheme.TextMuted;
            Invalidate();
        }
    }

    public FilterButton()
    {
        Cursor = Cursors.Hand;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.FromArgb(84, 77, 44);
        FlatAppearance.MouseOverBackColor = UiTheme.SurfaceRaised;
        FlatStyle = FlatStyle.Flat;
        Font = UiTheme.Font(9F, FontStyle.Bold);
        ForeColor = UiTheme.TextMuted;
        Padding = new Padding(14, 0, 12, 0);
        TextAlign = ContentAlignment.MiddleLeft;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaint(pevent);
        if (active)
        {
            using (SolidBrush brush = new SolidBrush(UiTheme.Accent))
                pevent.Graphics.FillRectangle(brush, 0, 7, 3, Height - 14);
        }
        using (SolidBrush brush = new SolidBrush(active ? UiTheme.Cyan : UiTheme.TextDim))
        {
            SizeF size = pevent.Graphics.MeasureString(CountText ?? "", CountFont);
            pevent.Graphics.DrawString(CountText ?? "", CountFont, brush, Width - size.Width - 14, (Height - size.Height) / 2F);
        }
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
