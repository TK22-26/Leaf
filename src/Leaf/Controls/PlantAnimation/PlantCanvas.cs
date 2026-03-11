using System.Windows;
using System.Windows.Media;

namespace Leaf.Controls.PlantAnimation;

/// <summary>
/// Custom-rendered WPF canvas that draws and animates the plant using DrawingContext.
/// Uses CompositionTarget.Rendering for smooth per-frame updates.
/// Lifecycle driven by IsVisibleChanged so it restarts properly when toggled via Visibility binding.
/// </summary>
public class PlantCanvas : FrameworkElement
{
    private readonly PlantModel _plant;
    private readonly WindField _wind;
    private readonly List<WindSpring> _branchSprings;
    private DateTime _startTime;
    private DateTime _lastFrame;
    private bool _isRunning;

    // Animation timing
    private const double GrowthDurationSec = 4.0;    // total growth phase (1.5x faster)
    private const double StalkGrowthPortion = 0.55;   // stalk finishes at 55% of growth time
    private const double BounceDurationSec = 0.8;     // bounce settle after growth completes

    // Visual settings
    private const int StalkSegments = 40;
    private const int LeafSegments = 16;
    private const int BranchSegments = 10;

    public PlantCanvas()
    {
        _plant = new PlantModel(stalkHeight: 340, branchCount: 8);
        _wind = new WindField(baseStrength: 3.5, gustStrength: 8.0, gustFrequency: 0.35);

        _branchSprings = _plant.Branches.Select((b, i) => new WindSpring(
            stiffness: 25 + i * 4,    // lower branches are stiffer
            damping: 1.8 + i * 0.15,
            mass: 0.9 + i * 0.08
        )).ToList();

        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += OnUnloaded;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAnimation();
    }

    private void StartAnimation()
    {
        // Reset timers for a fresh growth animation
        _startTime = DateTime.UtcNow;
        _lastFrame = _startTime;

        // Reset all wind springs so branches don't start mid-sway
        foreach (var spring in _branchSprings)
            spring.Reset();

        _isRunning = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_isRunning) return;
        _isRunning = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_isRunning) return;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 10) return;

        const double designWidth = 180;
        const double designHeight = 430;
        var scale = Math.Min(w / designWidth, h / designHeight);
        scale = Math.Max(scale, 0.1);

        var now = DateTime.UtcNow;
        double elapsed = (now - _startTime).TotalSeconds;
        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.05); // cap delta to avoid jumps
        _lastFrame = now;

        // Overall growth progress (0..1)
        double growthRaw = Math.Min(elapsed / GrowthDurationSec, 1.0);
        double growth = PlantMath.Gompertz(growthRaw, a: 1.0, b: 2.8, c: 6.5);

        // Stalk growth progress — stalk leads the branches
        double stalkGrowth = PlantMath.Clamp01(growthRaw / StalkGrowthPortion);
        double stalkProgress = PlantMath.EaseOutCubic(stalkGrowth);

        // Transform: fit the fixed plant model into the available space.
        double baseX = w * 0.5;
        double baseY = h - (18 * scale);
        var rootTransform = new TransformGroup();
        rootTransform.Children.Add(new ScaleTransform(scale, scale));
        rootTransform.Children.Add(new TranslateTransform(baseX, baseY));
        dc.PushTransform(rootTransform);

        // Bounce: after growth completes, overshoot then settle via damped elastic
        double bounceScale = 1.0;
        if (growthRaw >= 1.0)
        {
            double bounceT = PlantMath.Clamp01((elapsed - GrowthDurationSec) / BounceDurationSec);
            // Damped elastic: overshoots to ~1.06 then settles to 1.0
            bounceScale = 1.0 + 0.06 * Math.Sin(bounceT * Math.PI) * Math.Exp(-3.5 * bounceT);
        }

        if (Math.Abs(bounceScale - 1.0) > 0.0001)
        {
            // Scale from the base (origin is already at base due to translate)
            dc.PushTransform(new ScaleTransform(bounceScale, bounceScale, 0, 0));
        }

        // --- Draw stalk ---
        DrawStalk(dc, stalkProgress, elapsed);

        // --- Draw branches and leaves ---
        for (int i = 0; i < _plant.Branches.Count; i++)
        {
            var branch = _plant.Branches[i];
            var spring = _branchSprings[i];

            // Branch growth: starts after its delay, takes ~40% of total growth duration to fully grow
            double branchGrowthRaw = PlantMath.Clamp01((growthRaw - branch.GrowthDelay) / 0.40);
            double branchGrowth = PlantMath.EaseOutBack(branchGrowthRaw);

            if (branchGrowthRaw <= 0) continue; // not born yet

            // Wind sway
            double heightRatio = branch.StalkParameter;
            double windForce = _wind.Sample(elapsed, heightRatio, branch.NoiseOffset);
            double swayAngle = spring.Update(dt, windForce);

            // Attachment point on the stalk
            double attachT = Math.Min(branch.StalkParameter, stalkProgress);
            if (attachT < branch.StalkParameter * 0.98) continue; // stalk hasn't reached this point yet

            var attachPoint = PlantMath.Bezier(attachT,
                _plant.Stalk.P0, _plant.Stalk.P1, _plant.Stalk.P2, _plant.Stalk.P3);

            if (branch.IsTop)
            {
                DrawTopLeaf(dc, attachPoint, branchGrowth, swayAngle, branch, elapsed);
            }
            else
            {
                DrawBranchAndLeaf(dc, attachPoint, branchGrowth, swayAngle, branch, elapsed);
            }
        }

        if (Math.Abs(bounceScale - 1.0) > 0.0001)
        {
            dc.Pop(); // undo bounce scale
        }

        dc.Pop(); // undo translate
    }

    // =========================================================================
    // Drawing Methods
    // =========================================================================

    private void DrawStalk(DrawingContext dc, double stalkProgress, double time)
    {
        if (stalkProgress < 0.001) return;

        var stalk = _plant.Stalk;

        // Build the stalk as a filled shape (thick at base, thin at tip)
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            // Generate left and right edges of the tapered stalk
            var leftPoints = new List<Point>();
            var rightPoints = new List<Point>();

            for (int i = 0; i <= StalkSegments; i++)
            {
                double t = (double)i / StalkSegments * stalkProgress;
                var center = PlantMath.Bezier(t, stalk.P0, stalk.P1, stalk.P2, stalk.P3);
                var normal = PlantMath.BezierNormal(t, stalk.P0, stalk.P1, stalk.P2, stalk.P3);

                // Add subtle wind sway to upper portions
                double windOffset = PerlinNoise.FBM(time * 0.3, t * 2, octaves: 2) * 1.5 * t;
                center = new Point(center.X + windOffset, center.Y);

                double thickness = PlantMath.StemThickness(t / Math.Max(0.01, stalkProgress),
                    stalk.BaseThickness, stalk.TipThickness) * 0.5;

                leftPoints.Add(new Point(center.X + normal.X * thickness, center.Y + normal.Y * thickness));
                rightPoints.Add(new Point(center.X - normal.X * thickness, center.Y - normal.Y * thickness));
            }

            // Draw as closed path: left edge up, right edge back down
            ctx.BeginFigure(leftPoints[0], true, true);
            for (int i = 1; i < leftPoints.Count; i++)
                ctx.LineTo(leftPoints[i], true, true);
            for (int i = rightPoints.Count - 1; i >= 0; i--)
                ctx.LineTo(rightPoints[i], true, true);
        }
        geometry.Freeze();

        // Gradient fill: slightly lighter on left (light source), darker on right
        var stalkBrush = new LinearGradientBrush(
            PlantColors.StemLight,
            PlantColors.StemDark,
            new Point(0, 0.5), new Point(1, 0.5));
        stalkBrush.Freeze();

        // Subtle dark outline
        var outlinePen = new Pen(new SolidColorBrush(PlantColors.StemDark), 0.5);
        outlinePen.Freeze();

        dc.DrawGeometry(stalkBrush, outlinePen, geometry);
    }

    private void DrawBranchAndLeaf(DrawingContext dc, Point attachPoint,
        double branchGrowth, double swayAngle, BranchDefinition branch, double time)
    {
        // Branch grows outward from the stalk
        double dirX = branch.DirectionX;
        double len = branch.BranchLength * branchGrowth;
        double angle = branch.BranchAngle;

        // Branch end point with droop
        double endX = attachPoint.X + dirX * len * Math.Cos(angle);
        double endY = attachPoint.Y - len * Math.Sin(angle) + branch.Droop * branchGrowth;

        // Branch Bezier control points
        double cx1 = attachPoint.X + dirX * len * 0.33 * Math.Cos(angle);
        double cy1 = attachPoint.Y - len * 0.33 * Math.Sin(angle);
        double cx2 = attachPoint.X + dirX * len * 0.66 * Math.Cos(angle);
        double cy2 = attachPoint.Y - len * 0.66 * Math.Sin(angle) + branch.Droop * branchGrowth * 0.5;

        var bp0 = attachPoint;
        var bp1 = new Point(cx1, cy1);
        var bp2 = new Point(cx2, cy2);
        var bp3 = new Point(endX, endY);

        // Apply wind sway rotation around the attachment point
        dc.PushTransform(new RotateTransform(swayAngle * 180 / Math.PI, attachPoint.X, attachPoint.Y));

        // Draw the branch (thin tapered line)
        DrawBranch(dc, bp0, bp1, bp2, bp3, branchGrowth);

        // Draw the leaf at the end of the branch
        var leafAttach = PlantMath.Bezier(branchGrowth, bp0, bp1, bp2, bp3);
        double leafAngle = Math.Atan2(endY - cy2, endX - cx2); // tangent at branch tip

        // Leaf unfurl progress trails branch growth slightly
        double leafGrowth = PlantMath.Clamp01((branchGrowth - 0.3) / 0.7);
        double unfurl = PlantMath.EaseOutCubic(leafGrowth);

        if (unfurl > 0.01)
        {
            DrawLeaf(dc, leafAttach, leafAngle, unfurl, branch, time);
        }

        dc.Pop(); // undo sway rotation
    }

    private void DrawBranch(DrawingContext dc, Point p0, Point p1, Point p2, Point p3, double growth)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var leftPoints = new List<Point>();
            var rightPoints = new List<Point>();

            double baseThick = 3.0;
            double tipThick = 0.8;

            for (int i = 0; i <= BranchSegments; i++)
            {
                double t = (double)i / BranchSegments;
                var center = PlantMath.Bezier(t, p0, p1, p2, p3);
                var normal = PlantMath.BezierNormal(t, p0, p1, p2, p3);
                double thick = PlantMath.StemThickness(t, baseThick, tipThick) * 0.5;

                leftPoints.Add(new Point(center.X + normal.X * thick, center.Y + normal.Y * thick));
                rightPoints.Add(new Point(center.X - normal.X * thick, center.Y - normal.Y * thick));
            }

            ctx.BeginFigure(leftPoints[0], true, true);
            for (int i = 1; i < leftPoints.Count; i++)
                ctx.LineTo(leftPoints[i], true, true);
            for (int i = rightPoints.Count - 1; i >= 0; i--)
                ctx.LineTo(rightPoints[i], true, true);
        }
        geometry.Freeze();

        var brush = new SolidColorBrush(PlantColors.StemMid);
        brush.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private void DrawLeaf(DrawingContext dc, Point attachPoint, double baseAngle,
        double unfurl, BranchDefinition branch, double time)
    {
        double leafLen = branch.LeafLength * unfurl;
        double leafWid = branch.LeafWidth * unfurl;
        double dirX = branch.DirectionX;

        // Midrib: Bezier from attachment point outward in the branch's direction
        // During unfurl, it transitions from curled to open
        double curl = (1.0 - unfurl) * 0.6; // how much the leaf is still curled

        double cos = Math.Cos(baseAngle);
        double sin = Math.Sin(baseAngle);

        // Midrib control points (curled -> open)
        var m0 = attachPoint;
        var m1 = new Point(
            attachPoint.X + leafLen * 0.33 * cos,
            attachPoint.Y + leafLen * 0.33 * sin - curl * leafLen * 0.3);
        var m2 = new Point(
            attachPoint.X + leafLen * 0.66 * cos,
            attachPoint.Y + leafLen * 0.66 * sin - curl * leafLen * 0.15);
        var m3 = new Point(
            attachPoint.X + leafLen * cos,
            attachPoint.Y + leafLen * sin + curl * leafLen * 0.1);

        // Generate leaf outline from midrib
        var leftEdge = new List<Point>();
        var rightEdge = new List<Point>();

        for (int i = 0; i <= LeafSegments; i++)
        {
            double t = (double)i / LeafSegments;
            var mid = PlantMath.Bezier(t, m0, m1, m2, m3);
            var normal = PlantMath.BezierNormal(t, m0, m1, m2, m3);

            // Leaf width profile: widest at ~35% from base, tapering to points at both ends
            double widthProfile = Math.Sin(Math.PI * Math.Pow(t, 0.65)) * (1 - Math.Pow(t, 3) * 0.2);
            // Asymmetric: slightly wider on the outer edge
            double outerScale = 1.05;
            double innerScale = 0.95;

            double halfWidth = leafWid * 0.5 * widthProfile;

            leftEdge.Add(new Point(
                mid.X + normal.X * halfWidth * outerScale,
                mid.Y + normal.Y * halfWidth * outerScale));
            rightEdge.Add(new Point(
                mid.X - normal.X * halfWidth * innerScale,
                mid.Y - normal.Y * halfWidth * innerScale));
        }

        // Build leaf geometry
        var leafGeometry = new StreamGeometry();
        using (var ctx = leafGeometry.Open())
        {
            // Start at the base (which is a point)
            ctx.BeginFigure(leftEdge[0], true, true);

            // Left edge to tip using smooth Bezier approximation
            for (int i = 1; i < leftEdge.Count; i++)
                ctx.LineTo(leftEdge[i], true, true);

            // Right edge back to base
            for (int i = rightEdge.Count - 1; i >= 0; i--)
                ctx.LineTo(rightEdge[i], true, true);
        }
        leafGeometry.Freeze();

        // Leaf age based on unfurl progress (fully unfurled = more mature coloring)
        double age = PlantMath.Clamp01(unfurl * 0.8 + 0.1);
        var leafBrush = PlantColors.CreateLeafBrush(age, branch.LeafColorVariation);

        // Subtle dark edge
        var edgePen = new Pen(new SolidColorBrush(
            PlantColors.LeafColor(age, 0.5, branch.LeafColorVariation - 0.2)), 0.4);
        edgePen.Freeze();

        dc.DrawGeometry(leafBrush, edgePen, leafGeometry);

        // Draw midrib vein
        if (unfurl > 0.2)
        {
            DrawMidrib(dc, m0, m1, m2, m3, age, branch.LeafColorVariation, unfurl);
        }

        // Draw secondary veins
        if (unfurl > 0.5)
        {
            DrawSecondaryVeins(dc, m0, m1, m2, m3, leftEdge, rightEdge, age, branch.LeafColorVariation, unfurl);
        }
    }

    private void DrawMidrib(DrawingContext dc, Point m0, Point m1, Point m2, Point m3,
        double age, double variation, double unfurl)
    {
        var midribGeometry = new StreamGeometry();
        using (var ctx = midribGeometry.Open())
        {
            ctx.BeginFigure(m0, false, false);
            ctx.BezierTo(m1, m2, m3, true, true);
        }
        midribGeometry.Freeze();

        var veinBrush = PlantColors.CreateVeinBrush(age, variation);
        var pen = new Pen(veinBrush, 1.2 * unfurl) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        dc.DrawGeometry(null, pen, midribGeometry);
    }

    private void DrawSecondaryVeins(DrawingContext dc, Point m0, Point m1, Point m2, Point m3,
        List<Point> leftEdge, List<Point> rightEdge, double age, double variation, double unfurl)
    {
        double veinOpacity = PlantMath.Clamp01((unfurl - 0.5) / 0.5);
        var veinColor = PlantColors.LeafColor(age, 0.4, variation - 0.15);
        var adjustedColor = Color.FromArgb((byte)(veinOpacity * 140),
            (byte)(veinColor.R * 0.75), (byte)(veinColor.G * 0.78), (byte)(veinColor.B * 0.7));
        var pen = new Pen(new SolidColorBrush(adjustedColor), 0.6)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();

        int veinCount = 5;
        for (int v = 1; v <= veinCount; v++)
        {
            double t = 0.15 + 0.65 * v / (veinCount + 1);
            var midPoint = PlantMath.Bezier(t, m0, m1, m2, m3);
            int idx = (int)(t * LeafSegments);
            idx = Math.Min(idx, leftEdge.Count - 1);

            // Veins reach toward the leaf edge, slightly curved
            var leftTarget = leftEdge[idx];
            var rightTarget = rightEdge[idx];

            // Left vein
            var leftMid = new Point(
                midPoint.X + (leftTarget.X - midPoint.X) * 0.5,
                midPoint.Y + (leftTarget.Y - midPoint.Y) * 0.5 - 2);
            dc.DrawLine(pen, midPoint, leftMid);
            dc.DrawLine(pen, leftMid, leftTarget);

            // Right vein
            var rightMid = new Point(
                midPoint.X + (rightTarget.X - midPoint.X) * 0.5,
                midPoint.Y + (rightTarget.Y - midPoint.Y) * 0.5 - 2);
            dc.DrawLine(pen, midPoint, rightMid);
            dc.DrawLine(pen, rightMid, rightTarget);
        }
    }

    private void DrawTopLeaf(DrawingContext dc, Point attachPoint,
        double branchGrowth, double swayAngle, BranchDefinition branch, double time)
    {
        double unfurl = PlantMath.EaseOutCubic(branchGrowth);
        if (unfurl < 0.01) return;

        // Top leaf points straight up
        double baseAngle = -Math.PI / 2; // straight up

        dc.PushTransform(new RotateTransform(swayAngle * 180 / Math.PI * 0.5, attachPoint.X, attachPoint.Y));

        DrawLeaf(dc, attachPoint, baseAngle, unfurl, branch, time);

        dc.Pop();
    }
}
