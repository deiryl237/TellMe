namespace TellMe.App;

#if ANDROID
using TellMe.App.Platforms.Android;
#endif

public partial class MainPage : ContentPage
{
	#if ANDROID
		private AndroidBluetoothTransport? _transport;
	#endif

		public MainPage()
		{
			InitializeComponent();

	#if ANDROID
			try
			{
				_transport = new AndroidBluetoothTransport();

				// Met à jour le label de statut à chaque événement
				_transport.StatusChanged += status =>
				{
					MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = $"Statut : {status}");
				};

				// Dès que des octets arrivent dans l'air, on les décode et on les affiche !
				_transport.DataReceived += data =>
				{
					string text = System.Text.Encoding.UTF8.GetString(data);
					MainThread.BeginInvokeOnMainThread(() =>
					{
						ReceivedLabel.Text = $"Reçu à {DateTime.Now:HH:mm:ss} :\n{text}";
					});
				};
			}
			catch (Exception ex)
			{
				StatusLabel.Text = $"Erreur d'initialisation : {ex.Message}";
			}
	#else
			StatusLabel.Text = "Mode Bureau Windows (Le Bluetooth réel s'exécute sur Android).";
	#endif
		}

		protected override void OnAppearing()
		{
			base.OnAppearing();
	#if ANDROID
			try
			{
				AndroidBluetoothTransport.RequestPermissionsIfNeeded();
			}
			catch (Exception ex)
			{
				StatusLabel.Text = $"Erreur permission : {ex.Message}";
			}
	#endif
		}

		/// <summary>
		/// Rôle A : Le Récepteur se met en écoute.
		/// </summary>
		private async void OnListenClicked(object sender, EventArgs e)
		{
	#if ANDROID
			if (_transport != null)
			{
				await _transport.StartAsync();
			}
	#else
			await DisplayAlert("Info", "Le mode Bluetooth RFCOMM est conçu pour les téléphones Android.", "OK");
	#endif
		}

		/// <summary>
		/// Rôle B : L'Émetteur liste les téléphones déjà appairés en Bluetooth.
		/// </summary>
		private void OnRefreshDevicesClicked(object sender, EventArgs e)
		{
	#if ANDROID
			if (_transport != null)
			{
				var devices = _transport.GetPairedDevices();
				DevicesPicker.ItemsSource = devices;
				if (devices.Count > 0)
				{
					DevicesPicker.SelectedIndex = 0;
					StatusLabel.Text = $"{devices.Count} appareil(s) appairé(s) trouvé(s).";
				}
				else
				{
					StatusLabel.Text = "Aucun appareil appairé. Associe tes deux téléphones dans les paramètres Android !";
				}
			}
	#else
			DisplayAlert("Info", "Appairage disponible sur Android.", "OK");
	#endif
		}

		/// <summary>
		/// Rôle B : L'Émetteur se connecte au téléphone sélectionné.
		/// </summary>
		private async void OnConnectClicked(object sender, EventArgs e)
		{
	#if ANDROID
			if (_transport != null && DevicesPicker.SelectedItem is string deviceName)
			{
				await _transport.ConnectToDeviceAsync(deviceName);
			}
			else
			{
				await DisplayAlert("Attention", "Veuillez choisir un appareil dans la liste.", "OK");
			}
	#else
			await DisplayAlert("Info", "Connexion disponible sur Android.", "OK");
	#endif
		}

		/// <summary>
		/// Envoi d'un message textuel dans le socket RFCOMM.
		/// </summary>
		private async void OnSendClicked(object sender, EventArgs e)
		{
	#if ANDROID
			if (_transport != null && !string.IsNullOrWhiteSpace(MessageEntry.Text))
			{
				byte[] data = System.Text.Encoding.UTF8.GetBytes(MessageEntry.Text);
				await _transport.SendAsync(data);
				MessageEntry.Text = string.Empty;
			}
	#else
			await DisplayAlert("Info", "Envoi disponible sur Android.", "OK");
	#endif
		}
}
