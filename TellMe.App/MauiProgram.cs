using Microsoft.Extensions.Logging;
using TellMe.App.Services;
using TellMe.App.ViewModels;
using TellMe.App.Views;
using TellMe.Core.Engine;
using TellMe.Core.Network;
using TellMe.Core.Storage;

using ZXing.Net.Maui.Controls;

#if ANDROID || IOS
using Rive.Maui;
#endif

namespace TellMe.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
#if ANDROID || IOS
			.UseRive()
#endif
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.ConfigureMauiHandlers(handlers =>
			{
#if ANDROID
				handlers.AddHandler<TellMe.App.Controls.RiveBottomBarView, TellMe.App.Platforms.Android.RiveBottomBarHandler>();
#endif
			});

	#if DEBUG
		builder.Logging.AddDebug();
	#endif

		// 1. Enregistrement unique (Singleton) du moteur TellMe
		builder.Services.AddSingleton<TellMeEngine>(sp =>
		{
			var dbPath = Path.Combine(FileSystem.AppDataDirectory, "tellme.db3");
			var database = new TellMeDatabase(dbPath);

	#if ANDROID
			ITransport transport = new TellMe.App.Platforms.Android.AndroidBluetoothTransport();
	#else
			ITransport transport = new DummyTransport();
	#endif

			var engine = new TellMeEngine(database, transport);
			engine._syncEngine.LogAction = (msg) =>
			{
#if ANDROID
				global::Android.Util.Log.Info("TellMe", $"[Sync] {msg}");
#endif
			};
			return engine;
		});

		// 2. ViewModels
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddTransient<ChatViewModel>();

		// 3. Pages
		builder.Services.AddSingleton<MainPage>();

		return builder.Build();
	}
}
