#if ANDROID
using Android.Content;
using Android.Views;
using Microsoft.Maui.Handlers;
using Rive.Android;
using Rive.Android.Controllers;
using Rive.Android.Core;
using TellMe.App.Controls;

namespace TellMe.App.Platforms.Android;

public class RiveWheelListener : Java.Lang.Object, RiveFileController.IListener
{
    private readonly Action<string, string> _onStateChanged;

    public RiveWheelListener(Action<string, string> onStateChanged)
    {
        _onStateChanged = onStateChanged;
    }

    public void NotifyStateChanged(string stateMachineName, string stateName)
    {
        _onStateChanged?.Invoke(stateMachineName, stateName);
    }

    public void NotifyAdvance(float p0) { }
    public void NotifyLoop(IPlayableInstance p0) { }
    public void NotifyPause(IPlayableInstance p0) { }
    public void NotifyPlay(IPlayableInstance p0) { }
    public void NotifyStop(IPlayableInstance p0) { }
}

public class RiveWheelHandler : ViewHandler<RiveWheelView, RiveAnimationView>
{
    public static IPropertyMapper<RiveWheelView, RiveWheelHandler> PropertyMapper =
        new PropertyMapper<RiveWheelView, RiveWheelHandler>(ViewMapper)
        {
            [nameof(IView.Background)] = MapIgnoreBackground,
            [nameof(RiveWheelView.SelectedSlot)] = MapSelectedSlot,
            [nameof(RiveWheelView.SlotCount)] = MapSlotCount,
        };

    private static void MapIgnoreBackground(RiveWheelHandler handler, RiveWheelView view)
    {
        // TextureView does not support backgrounds
    }

    public static CommandMapper<RiveWheelView, RiveWheelHandler> CommandMapper =
        new CommandMapper<RiveWheelView, RiveWheelHandler>(ViewCommandMapper)
        {
            [RiveWheelView.FireAddSlot] = MapFireAddSlot,
            [RiveWheelView.FireResetWheel] = MapFireResetWheel,
        };

    public RiveWheelHandler() : base(PropertyMapper, CommandMapper)
    {
    }

    private static byte[]? _cachedRiveBytes;
    private RiveWheelListener? _wheelListener;

    protected override RiveAnimationView CreatePlatformView()
    {
        var view = new RiveAnimationView.Builder(Context).Build();
        view.SetOpaque(false);
        view.LayoutParameters = new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);

        try
        {
            if (_cachedRiveBytes == null)
            {
                const string assetName = "tellme_perch_wheel.riv";
                try
                {
                    using var assetStream = Context.Assets?.Open(assetName);
                    if (assetStream != null)
                    {
                        using var ms = new MemoryStream();
                        assetStream.CopyTo(ms);
                        _cachedRiveBytes = ms.ToArray();
                        global::Android.Util.Log.Info("TellMeRiveWheel", $"[OK] Loaded {_cachedRiveBytes.Length} bytes via Context.Assets ({assetName})");
                    }
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error("TellMeRiveWheel", $"Context.Assets failed: {ex.Message}");
                }

                if (_cachedRiveBytes == null)
                {
                    try
                    {
                        using var fileStream = FileSystem.OpenAppPackageFileAsync(assetName).GetAwaiter().GetResult();
                        using var ms = new MemoryStream();
                        fileStream.CopyTo(ms);
                        _cachedRiveBytes = ms.ToArray();
                        global::Android.Util.Log.Info("TellMeRiveWheel", $"[OK] Loaded {_cachedRiveBytes.Length} bytes via FileSystem ({assetName})");
                    }
                    catch (Exception ex)
                    {
                        global::Android.Util.Log.Error("TellMeRiveWheel", $"FileSystem fallback failed: {ex.Message}");
                    }
                }
            }

            if (_cachedRiveBytes != null && _cachedRiveBytes.Length > 0)
            {
                global::Android.Util.Log.Info("TellMeRiveWheel", $"Configuring Perch_Wheel with State Machine 1 ({_cachedRiveBytes.Length} bytes)...");

                // Enregistrement de l'écouteur d'événements et de changements d'état natifs Rive
                _wheelListener = new RiveWheelListener(OnRiveStateChanged);
                view.RegisterListener(_wheelListener);

                // Initialisation avec State Machine 1 pour activer les écouteurs vectoriels natifs Rive
                view.SetRiveBytes(
                    _cachedRiveBytes,
                    "Perch_Wheel",
                    null,
                    "State Machine 1",
                    true,
                    Fit.Contain,
                    Alignment.Center,
                    Loop.Loop2);

                view.Touch += OnViewTouch;

                int initialCount = Math.Clamp(VirtualView?.SlotCount ?? 3, 2, 8);
                int initialSlot = Math.Clamp(VirtualView?.SelectedSlot ?? 0, 0, 7);

                view.PostDelayed(() =>
                {
                    try
                    {
                        view.Play($"add_slot_{initialCount}", Loop.Oneshot, Direction.Forwards, true, false);
                        view.Play($"select_slot_{initialSlot}", Loop.Oneshot, Direction.Forwards, true, false);
                    }
                    catch (Exception ex)
                    {
                        global::Android.Util.Log.Error("TellMeRiveWheel", $"Initial anims failed: {ex.Message}");
                    }
                }, 50);

                global::Android.Util.Log.Info("TellMeRiveWheel", $"Perch_Wheel initialized with native Rive state machine: slotCount={initialCount}, selectedSlot={initialSlot}!");
            }
            else
            {
                global::Android.Util.Log.Error("TellMeRiveWheel", "FAILED: _cachedRiveBytes is NULL or empty!");
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TellMeRiveWheel", $"Error initializing RiveAnimationView: {ex}");
        }

        return view;
    }

    private void OnRiveStateChanged(string stateMachineName, string stateName)
    {
        global::Android.Util.Log.Info("TellMeRiveWheel", $"Native Rive StateChanged: machine={stateMachineName}, state={stateName}");
        if (string.IsNullOrEmpty(stateName)) return;

        // Quand Rive sélectionne une case (state_sel_0 .. state_sel_7)
        if (stateName.StartsWith("state_sel_"))
        {
            if (int.TryParse(stateName.Substring("state_sel_".Length), out int slotIndex))
            {
                VirtualView?.Dispatcher.Dispatch(() => VirtualView.NotifySlotSelected(slotIndex));
            }
        }
        // Quand Rive clique sur le bouton + (state_slot_2 .. state_slot_8)
        else if (stateName.StartsWith("state_slot_"))
        {
            VirtualView?.Dispatcher.Dispatch(() => VirtualView.NotifyAddSlotClicked());
        }
    }

    protected override void DisconnectHandler(RiveAnimationView platformView)
    {
        try
        {
            if (_wheelListener != null)
            {
                platformView.UnregisterListener(_wheelListener);
                _wheelListener = null;
            }
            platformView.Touch -= OnViewTouch;
        }
        catch { }

        base.DisconnectHandler(platformView);
    }

    private static readonly (float X, float Y)[] PlusPositions = new (float X, float Y)[]
    {
        (290f, 120f), // count = 1 -> bouton plus en position 1
        (333f, 225f), // count = 2 -> bouton plus en position 2
        (290f, 330f), // count = 3 -> bouton plus en position 3
        (185f, 373f), // count = 4 -> bouton plus en position 4
        (80f,  330f), // count = 5 -> bouton plus en position 5
        (37f,  225f), // count = 6 -> bouton plus en position 6
        (80f,  120f), // count = 7 -> bouton plus en position 7
        (-1000f, -1000f) // count = 8 -> masqué (roue pleine)
    };

    private static readonly (float X, float Y)[] SlotPositions = new (float X, float Y)[]
    {
        (185f, 80f),  // slot 0 (haut)
        (290f, 120f), // slot 1 (haut-droite)
        (333f, 225f), // slot 2 (droite)
        (290f, 330f), // slot 3 (bas-droite)
        (185f, 373f), // slot 4 (bas)
        (80f,  330f), // slot 5 (bas-gauche)
        (37f,  225f), // slot 6 (gauche)
        (80f,  120f), // slot 7 (haut-gauche)
    };

    private float _downX;
    private float _downY;

    private void OnViewTouch(object? sender, global::Android.Views.View.TouchEventArgs e)
    {
        if (e.Event == null || PlatformView == null || VirtualView == null)
        {
            e.Handled = false;
            return;
        }

        switch (e.Event.Action)
        {
            case MotionEventActions.Down:
                _downX = e.Event.GetX();
                _downY = e.Event.GetY();
                break;

            case MotionEventActions.Up:
                float upX = e.Event.GetX();
                float upY = e.Event.GetY();
                float distMoved = MathF.Sqrt(MathF.Pow(upX - _downX, 2) + MathF.Pow(upY - _downY, 2));
                if (distMoved < 35f)
                {
                    float viewW = PlatformView.Width;
                    float viewH = PlatformView.Height;
                    if (viewW > 0 && viewH > 0)
                    {
                        float scale = Math.Min(viewW / 400f, viewH / 450f);
                        if (scale > 0)
                        {
                            float artX = (upX - (viewW - 400f * scale) / 2f) / scale;
                            float artY = (upY - (viewH - 450f * scale) / 2f) / scale;

                            // 1. Galet central (Hublot pigeon)
                            float distCenter = MathF.Sqrt(MathF.Pow(artX - 185f, 2) + MathF.Pow(artY - 225f, 2));
                            if (distCenter <= 65f)
                            {
                                VirtualView.Dispatcher.Dispatch(() => VirtualView.NotifyPebbleClicked());
                                e.Handled = true;
                                return;
                            }

                            int count = Math.Clamp(VirtualView.SlotCount, 1, 8);

                            // 2. Bouton (+) dynamique suivant son positionnement dans l'espace Rive
                            if (count < 8)
                            {
                                var (plusX, plusY) = PlusPositions[count - 1];
                                float distPlus = MathF.Sqrt(MathF.Pow(artX - plusX, 2) + MathF.Pow(artY - plusY, 2));
                                if (distPlus <= 35f)
                                {
                                    global::Android.Util.Log.Info("TellMeRiveWheel", $"Dynamic (+) button tapped at artboard ({artX:F0}, {artY:F0}) for slotCount={count}");
                                    VirtualView.Dispatcher.Dispatch(() => VirtualView.NotifyAddSlotClicked());
                                    e.Handled = true;
                                    return;
                                }
                            }

                            // 3. Perchoirs existants (slots 0 .. count-1)
                            for (int i = 0; i < count; i++)
                            {
                                var (slotX, slotY) = SlotPositions[i];
                                float distSlot = MathF.Sqrt(MathF.Pow(artX - slotX, 2) + MathF.Pow(artY - slotY, 2));
                                if (distSlot <= 35f)
                                {
                                    int selectedIndex = i;
                                    global::Android.Util.Log.Info("TellMeRiveWheel", $"Slot {selectedIndex} tapped at artboard ({artX:F0}, {artY:F0})");
                                    VirtualView.Dispatcher.Dispatch(() => VirtualView.NotifySlotSelected(selectedIndex));
                                    e.Handled = true;
                                    return;
                                }
                            }
                        }
                    }
                }
                break;
        }

        e.Handled = false;
    }

    public static void MapSelectedSlot(RiveWheelHandler handler, RiveWheelView view)
    {
        if (handler.PlatformView == null) return;
        try
        {
            int slot = Math.Clamp(view.SelectedSlot, 0, 7);
            handler.PlatformView.Play($"select_slot_{slot}", Loop.Oneshot, Direction.Forwards, true, false);
            global::Android.Util.Log.Info("TellMeRiveWheel", $"MapSelectedSlot: played select_slot_{slot}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TellMeRiveWheel", $"MapSelectedSlot error: {ex.Message}");
        }
    }

    public static void MapSlotCount(RiveWheelHandler handler, RiveWheelView view)
    {
        if (handler.PlatformView == null) return;
        try
        {
            int count = Math.Clamp(view.SlotCount, 1, 8);
            if (count == 1)
            {
                handler.PlatformView.Play("idle_slot_1", Loop.Oneshot, Direction.Forwards, true, false);
            }
            else
            {
                handler.PlatformView.Play($"add_slot_{count}", Loop.Oneshot, Direction.Forwards, true, false);
            }
            global::Android.Util.Log.Info("TellMeRiveWheel", $"MapSlotCount: played slot animation for count={count}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TellMeRiveWheel", $"MapSlotCount error: {ex.Message}");
        }
    }

    public static void MapFireAddSlot(RiveWheelHandler handler, RiveWheelView view, object? args)
    {
        if (handler.PlatformView == null) return;
        try
        {
            handler.PlatformView.Play("Timeline 1", Loop.Oneshot, Direction.Forwards, true, false);
            global::Android.Util.Log.Info("TellMeRiveWheel", "MapFireAddSlot: Timeline 1 played");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TellMeRiveWheel", $"MapFireAddSlot error: {ex.Message}");
        }
    }

    public static void MapFireResetWheel(RiveWheelHandler handler, RiveWheelView view, object? args)
    {
        if (handler.PlatformView == null) return;
        try
        {
            handler.PlatformView.Play("idle_slot_1", Loop.Oneshot, Direction.Forwards, true, false);
            global::Android.Util.Log.Info("TellMeRiveWheel", "MapFireResetWheel: idle_slot_1 played");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TellMeRiveWheel", $"MapFireResetWheel error: {ex.Message}");
        }
    }
}
#endif
