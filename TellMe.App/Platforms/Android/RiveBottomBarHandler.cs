#if ANDROID
using Android.Content;
using Android.Views;
using Microsoft.Maui.Handlers;
using Rive.Android;
using Rive.Android.Core;
using TellMe.App.Controls;

namespace TellMe.App.Platforms.Android;

public class RiveBottomBarHandler : ViewHandler<RiveBottomBarView, RiveAnimationView>
{
    public static IPropertyMapper<RiveBottomBarView, RiveBottomBarHandler> PropertyMapper =
        new PropertyMapper<RiveBottomBarView, RiveBottomBarHandler>(ViewMapper)
        {
            [nameof(IView.Background)] = MapIgnoreBackground,
            [nameof(RiveBottomBarView.SelectedTab)] = MapSelectedTab,
        };

    private static void MapIgnoreBackground(RiveBottomBarHandler handler, RiveBottomBarView view)
    {
        // TextureView does not support backgrounds (throws UnsupportedOperationException)
    }

    public static CommandMapper<RiveBottomBarView, RiveBottomBarHandler> CommandMapper =
        new CommandMapper<RiveBottomBarView, RiveBottomBarHandler>(ViewCommandMapper)
        {
            [RiveBottomBarView.FireTab] = MapFireTab,
        };

    public RiveBottomBarHandler() : base(PropertyMapper, CommandMapper)
    {
    }

    private static byte[]? _cachedRiveBytes;
    private static int _currentTab = 0;
    private static int _transitionToken = 0;

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
                const string assetName = "tellme_bottom_bar_transparent.riv";
                try
                {
                    using var assetStream = Context.Assets?.Open(assetName);
                    if (assetStream != null)
                    {
                        using var ms = new MemoryStream();
                        assetStream.CopyTo(ms);
                        _cachedRiveBytes = ms.ToArray();
                        global::Android.Util.Log.Error("TellMeRive", $"[OK] Loaded {_cachedRiveBytes.Length} bytes via Context.Assets ({assetName})");
                    }
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error("TellMeRive", $"Context.Assets failed: {ex.Message}");
                }

                if (_cachedRiveBytes == null)
                {
                    try
                    {
                        using var fileStream = FileSystem.OpenAppPackageFileAsync(assetName).GetAwaiter().GetResult();
                        using var ms = new MemoryStream();
                        fileStream.CopyTo(ms);
                        _cachedRiveBytes = ms.ToArray();
                        global::Android.Util.Log.Error("TellMeRive", $"[OK] Loaded {_cachedRiveBytes.Length} bytes via FileSystem ({assetName})");
                    }
                    catch (Exception ex)
                    {
                        global::Android.Util.Log.Error("TellMeRive", $"FileSystem fallback failed: {ex.Message}");
                    }
                }
            }

            if (_cachedRiveBytes != null && _cachedRiveBytes.Length > 0)
            {
                _currentTab = 0;
                global::Android.Util.Log.Error("TellMeRive", $"Configuring RiveAnimationView with {_cachedRiveBytes.Length} bytes...");
                view.SetRiveBytes(
                    _cachedRiveBytes,
                    "Artboard 1",
                    "Idle_0",
                    null,
                    true,
                    Fit.FitWidth,
                    Alignment.BottomCenter,
                    Loop.Loop2);
                global::Android.Util.Log.Error("TellMeRive", "SetRiveBytes executed successfully with Idle_0 loop!");
            }
            else
            {
                global::Android.Util.Log.Error("TellMeRive", "FAILED: _cachedRiveBytes is NULL or empty!");
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TellMeRive", $"Error initializing RiveAnimationView: {ex}");
        }

        return view;
    }

    public static void MapSelectedTab(RiveBottomBarHandler handler, RiveBottomBarView view)
    {
        UpdateTabTouch(handler.PlatformView, view.SelectedTab);
    }

    public static void MapFireTab(RiveBottomBarHandler handler, RiveBottomBarView view, object? args)
    {
        if (args is int tabIndex)
        {
            UpdateTabTouch(handler.PlatformView, tabIndex);
        }
    }

    private static void UpdateTabTouch(RiveAnimationView? platformView, int tabIndex)
    {
        if (platformView == null) return;

        int newTab = Math.Clamp(tabIndex, 0, 3);
        if (_currentTab == newTab)
        {
            // Already active on this tab - do nothing to prevent interrupting animations
            return;
        }

        int prevTab = _currentTab;
        _currentTab = newTab;
        int token = ++_transitionToken;

        string transitionAnim = $"Trans_{prevTab}_{newTab}";
        string idleAnim = $"Idle_{newTab}";

        global::Android.Util.Log.Error("TellMeRive", $"Playing transition {transitionAnim} (from {prevTab} to {newTab}), then {idleAnim}");

        try
        {
            // Stoppe impérativement les animations en cours (ex: Idle_0, Idle_1)
            // sinon elles restent dans le LinkedHashSet de Rive et écrasent les suivantes !
            platformView.Stop();

            // Joue la transition (400 ms, 24 frames @ 60 fps)
            platformView.Play(transitionAnim, Loop.Oneshot, Direction.Forwards, false, false);

            // Enchaîne sur l'état Idle de l'onglet actif après 400 ms
            platformView.PostDelayed(() =>
            {
                try
                {
                    if (_transitionToken == token && _currentTab == newTab)
                    {
                        platformView.Stop();
                        platformView.Play(idleAnim, Loop.Loop2, Direction.Forwards, false, false);
                    }
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error("TellMeRive", $"Error playing idle animation: {ex.Message}");
                }
            }, 400);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TellMeRive", $"Error playing animation {transitionAnim}: {ex.Message}");
            try
            {
                platformView.Stop();
                platformView.Play(idleAnim, Loop.Loop2, Direction.Forwards, false, false);
            }
            catch { }
        }
    }
}
#endif
