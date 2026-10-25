using System.Windows.Input;

namespace TellMe.App.Controls;

public partial class RiveBottomBar : ContentView
{
    public static readonly BindableProperty SelectedTabProperty =
        BindableProperty.Create(
            nameof(SelectedTab),
            typeof(int),
            typeof(RiveBottomBar),
            0,
            defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnSelectedTabChanged);

    public int SelectedTab
    {
        get => (int)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public static readonly BindableProperty TabChangedCommandProperty =
        BindableProperty.Create(
            nameof(TabChangedCommand),
            typeof(ICommand),
            typeof(RiveBottomBar),
            null);

    public ICommand? TabChangedCommand
    {
        get => (ICommand?)GetValue(TabChangedCommandProperty);
        set => SetValue(TabChangedCommandProperty, value);
    }

    public event EventHandler<int>? TabChanged;

    public RiveBottomBar()
    {
        InitializeComponent();
        SizeChanged += OnBarSizeChanged;
        Loaded += OnBarLoaded;
    }

    private void OnBarLoaded(object? sender, EventArgs e)
    {
        if (Width > 0 && Height > 0)
        {
            UpdateBeigeArc(Width, Height);
        }
    }

    private void OnBarSizeChanged(object? sender, EventArgs e)
    {
        if (Width > 0 && Height > 0)
        {
            UpdateBeigeArc(Width, Height);
        }
    }

    private void UpdateBeigeArc(double width, double height)
    {
        try
        {
            if (width <= 0 || height <= 0) return;

            // In Rive artboard (430x165), Fit.FitWidth with Alignment.BottomCenter:
            double s = width / 430.0;
            double artH = 165.0 * s;
            double yOffset = Math.Max(0, height - artH);

            // Arc wire parameters in Rive artboard:
            // Arc goes from (30, 40) to (400, 40) with control points (135, 108) and (295, 108).
            // Outer stroke width is 9.5. To ensure the beige shape strictly hugs the wire
            // without ever crossing above it into the upper section, we anchor the top boundary
            // along the centerline / inner stroke of the wire (y = 42, ctrlY = 110 in artboard space).
            double wireY = yOffset + 42.0 * s;
            double ctrlY = yOffset + 110.0 * s;
            double xLeft = 30.0 * s;
            double xRight = 400.0 * s;
            double cx1 = 135.0 * s;
            double cx2 = 295.0 * s;
            double bottom = Math.Max(height, artH);

            var figure = new Microsoft.Maui.Controls.Shapes.PathFigure
            {
                StartPoint = new Point(0, wireY),
                IsClosed = true,
                Segments = new Microsoft.Maui.Controls.Shapes.PathSegmentCollection
                {
                    new Microsoft.Maui.Controls.Shapes.LineSegment(new Point(xLeft, wireY)),
                    new Microsoft.Maui.Controls.Shapes.BezierSegment(new Point(cx1, ctrlY), new Point(cx2, ctrlY), new Point(xRight, wireY)),
                    new Microsoft.Maui.Controls.Shapes.LineSegment(new Point(width, wireY)),
                    new Microsoft.Maui.Controls.Shapes.LineSegment(new Point(width, bottom)),
                    new Microsoft.Maui.Controls.Shapes.LineSegment(new Point(0, bottom))
                }
            };

            BeigeArcPath.Data = new Microsoft.Maui.Controls.Shapes.PathGeometry
            {
                Figures = new Microsoft.Maui.Controls.Shapes.PathFigureCollection { figure }
            };
        }
        catch
        {
            // Fallback to static PathData in XAML if any layout timing issue
        }
    }

    private static void OnSelectedTabChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is RiveBottomBar bar && newValue is int newIndex)
        {
            if (bar.RiveView != null && bar.RiveView.SelectedTab != newIndex)
            {
                bar.RiveView.SelectedTab = newIndex;
            }
        }
    }

    public void SelectTab(int index)
    {
        if (SelectedTab == index)
            return;

        SelectedTab = index;

        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            // Ignore on platforms without haptic engine
        }

        TabChanged?.Invoke(this, index);
        TabChangedCommand?.Execute(index);
    }

    private void OnTab0Tapped(object sender, TappedEventArgs e) => SelectTab(0);
    private void OnTab1Tapped(object sender, TappedEventArgs e) => SelectTab(1);
    private void OnTab2Tapped(object sender, TappedEventArgs e) => SelectTab(2);
    private void OnTab3Tapped(object sender, TappedEventArgs e) => SelectTab(3);
}
