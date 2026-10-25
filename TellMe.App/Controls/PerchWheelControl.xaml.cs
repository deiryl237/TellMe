using Microsoft.Maui.Controls.Shapes;
using TellMe.Core.Models;
using Contact = TellMe.Core.Models.Contact;

namespace TellMe.App.Controls;

public partial class PerchWheelControl : ContentView
{
    public event EventHandler<int>? SlotSelected;
    public event EventHandler? AddSlotClicked;
    public event EventHandler? CenterPigeonClicked;

    private List<Contact> _contacts = new();
    private int _slotCount = 1;
    private int _selectedIndex = 0;
    private bool _isAlive = false;

    public PerchWheelControl()
    {
        InitializeComponent();
        Loaded += OnControlLoaded;
        Unloaded += OnControlUnloaded;
    }

    private void OnControlLoaded(object? sender, EventArgs e)
    {
        _isAlive = true;
        StartFloatingAnimation();
        RenderWheel();
    }

    private void OnControlUnloaded(object? sender, EventArgs e)
    {
        _isAlive = false;
    }

    public void SetSlots(IList<Contact>? contacts, int count, int selectedIndex)
    {
        _contacts = contacts?.ToList() ?? new List<Contact>();
        _slotCount = Math.Clamp(count > 0 ? count : (_contacts.Count > 0 ? _contacts.Count : 1), 1, 8);
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _slotCount - 1));

        RenderWheel();
    }

    public void SetStatusBadge(string text, bool inFlight)
    {
        StatusBadgeLabel.Text = text;
        StatusBadgeBorder.BackgroundColor = inFlight
            ? Color.FromArgb("#F59E0B")
            : Color.FromArgb("#15803D");
    }

    private void RenderWheel()
    {
        SlotsContainer.Children.Clear();

        // Nombre de perchoirs occupés par des contacts (au moins 1)
        int contactSlotsCount = Math.Clamp(_contacts.Count > 0 ? _contacts.Count : _slotCount, 1, 8);
        
        // Si on a moins de 8 contacts, on affiche le bouton (+) comme perchoir suivant
        bool showPlusSlot = contactSlotsCount < 8;
        int totalVisualSlots = showPlusSlot ? (contactSlotsCount + 1) : 8;

        const double radius = 124.0;
        const double cx = 170.0;
        const double cy = 170.0;
        const double startAngle = -90.0; // Départ en haut (12h)

        double angleStep = 360.0 / totalVisualSlots;

        for (int i = 0; i < totalVisualSlots; i++)
        {
            double angleDeg = startAngle + (i * angleStep);
            double angleRad = angleDeg * Math.PI / 180.0;

            if (i < contactSlotsCount)
            {
                // Slot de Contact
                int slotIndex = i;
                bool isSelected = (slotIndex == _selectedIndex);
                
                string labelText = "P";
                if (slotIndex < _contacts.Count && !string.IsNullOrWhiteSpace(_contacts[slotIndex].DisplayName))
                {
                    string name = _contacts[slotIndex].DisplayName.Trim();
                    labelText = name.Length > 2 ? name.Substring(0, 2).ToUpper() : name.ToUpper();
                }

                var slotBorder = CreateContactSlot(slotIndex, labelText, isSelected);

                const double slotSize = 48.0;
                double posX = cx + radius * Math.Cos(angleRad) - (slotSize / 2.0);
                double posY = cy + radius * Math.Sin(angleRad) - (slotSize / 2.0);

                AbsoluteLayout.SetLayoutBounds(slotBorder, new Rect(posX, posY, slotSize, slotSize));
                AbsoluteLayout.SetLayoutFlags(slotBorder, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);

                SlotsContainer.Children.Add(slotBorder);
            }
            else if (showPlusSlot && i == contactSlotsCount)
            {
                // Slot du bouton (+)
                var plusBorder = CreatePlusSlot();

                const double plusSize = 44.0;
                double posX = cx + radius * Math.Cos(angleRad) - (plusSize / 2.0);
                double posY = cy + radius * Math.Sin(angleRad) - (plusSize / 2.0);

                AbsoluteLayout.SetLayoutBounds(plusBorder, new Rect(posX, posY, plusSize, plusSize));
                AbsoluteLayout.SetLayoutFlags(plusBorder, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);

                SlotsContainer.Children.Add(plusBorder);
            }
        }
    }

    private Border CreateContactSlot(int slotIndex, string initial, bool isSelected)
    {
        var border = new Border
        {
            WidthRequest = 48,
            HeightRequest = 48,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(24) },
            BackgroundColor = isSelected ? Color.FromArgb("#231F1E") : Color.FromArgb("#FFFDF9"),
            Stroke = Color.FromArgb("#231F1E"),
            StrokeThickness = isSelected ? 2.5 : 1.5,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#231F1E")),
                Offset = isSelected ? new Point(2, 3) : new Point(1.5, 2),
                Radius = 0,
                Opacity = isSelected ? 0.25f : 0.14f
            }
        };

        var label = new Label
        {
            Text = initial,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = isSelected ? Color.FromArgb("#FAF7F2") : Color.FromArgb("#231F1E"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        border.Content = label;

        var tapRecognizer = new TapGestureRecognizer();
        tapRecognizer.Tapped += async (s, e) =>
        {
            await border.ScaleTo(1.2, 80, Easing.CubicOut);
            await border.ScaleTo(1.0, 80, Easing.CubicIn);

            _selectedIndex = slotIndex;
            RenderWheel();
            SlotSelected?.Invoke(this, slotIndex);
        };
        border.GestureRecognizers.Add(tapRecognizer);

        return border;
    }

    private Border CreatePlusSlot()
    {
        var border = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            BackgroundColor = Color.FromArgb("#FAF7F2"),
            Stroke = Color.FromArgb("#8C4A3E"),
            StrokeThickness = 1.8,
            StrokeDashArray = new DoubleCollection { 2.5, 2 },
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#8C4A3E")),
                Offset = new Point(1.5, 2),
                Radius = 0,
                Opacity = 0.16f
            }
        };

        var plusImage = new Image
        {
            Source = "icon_plus.svg",
            WidthRequest = 18,
            HeightRequest = 18,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        border.Content = plusImage;

        var tapRecognizer = new TapGestureRecognizer();
        tapRecognizer.Tapped += async (s, e) =>
        {
            await border.ScaleTo(1.2, 80, Easing.CubicOut);
            await border.ScaleTo(1.0, 80, Easing.CubicIn);

            AddSlotClicked?.Invoke(this, EventArgs.Empty);
        };
        border.GestureRecognizers.Add(tapRecognizer);

        return border;
    }

    private async void StartFloatingAnimation()
    {
        while (_isAlive)
        {
            try
            {
                await Task.WhenAll(
                    CenterPigeonBorder.TranslateTo(0, -4, 1600, Easing.SinInOut),
                    CenterPigeonBorder.ScaleTo(1.025, 1600, Easing.SinInOut)
                );
                await Task.WhenAll(
                    CenterPigeonBorder.TranslateTo(0, 4, 1600, Easing.SinInOut),
                    CenterPigeonBorder.ScaleTo(1.0, 1600, Easing.SinInOut)
                );
            }
            catch
            {
                break;
            }
        }
    }

    private async void OnCenterPigeonTapped(object? sender, EventArgs e)
    {
        await CenterPigeonBorder.ScaleTo(1.08, 90, Easing.CubicOut);
        await CenterPigeonBorder.ScaleTo(1.0, 90, Easing.CubicIn);

        CenterPigeonClicked?.Invoke(this, EventArgs.Empty);
    }
}
