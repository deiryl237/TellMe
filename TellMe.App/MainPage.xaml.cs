using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Network;


namespace TellMe.App;

#if ANDROID
using TellMe.App.Platforms.Android;
#endif

public partial class MainPage : ContentPage
{
	private readonly MessageStore _store = new();
	private SyncEngine? _syncEngine;

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
			_syncEngine = new SyncEngine(_store, _transport);

			_transport.StatusChanged += status =>
			{
				MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = $"Statut : {status}");
			};

			// Dès qu'un message s'ajoute au store (créé localement ou reçu par Bluetooth)
			_store.MessageAdded += msg =>
			{
				MainThread.BeginInvokeOnMainThread(UpdateDagUI);
			};

			UpdateDagUI();
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
				if (_syncEngine != null)
				{
					await _syncEngine.StartSyncAsync();
				}
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
				if (string.IsNullOrWhiteSpace(MessageEntry.Text)) return;

				string author = string.IsNullOrWhiteSpace(AuthorEntry.Text) ? "Téléphone" : AuthorEntry.Text.Trim();
				string text = MessageEntry.Text.Trim();

				// 1. Crée le message causal rattaché aux têtes actuelles du DAG local
				var msg = new MeshMessage(text, author, "Tous", _store.Heads.ToArray());

				// 2. L'ajoute au MessageStore local (déclenchera UpdateDagUI automatiquement)
				_store.AddMessage(msg);
				MessageEntry.Text = string.Empty;

				// 3. Propage immédiatement le nouveau message si un voisin est connecté
			#if ANDROID
				if (_syncEngine != null)
				{
					await _syncEngine.StartSyncAsync();
				}
			#endif
			}

			private async void OnSyncClicked(object sender, EventArgs e)
			{
			#if ANDROID
				if (_syncEngine != null)
				{
					StatusLabel.Text = "Statut : Synchronisation DAG en cours...";
					await _syncEngine.StartSyncAsync();
				}
			#else
				await DisplayAlert("Info", "Synchronisation disponible sur Android.", "OK");
			#endif
			}

				private void UpdateDagUI()
		{
			DagStatusLabel.Text = $"DAG : {_store.Count} message(s) | Têtes : {_store.Heads.Count} | Orphelins : {_store.BufferCount}";

			if (_store.Count == 0)
			{
				DagMessagesLabel.Text = "(Aucun message dans le DAG local)";
				return;
			}

			var sb = new System.Text.StringBuilder();
			foreach (var msg in _store.Messages.Reverse())
			{
				string shortHash = msg.Hash.Length >= 8 ? msg.Hash[..8] : msg.Hash;
				string parentsStr = msg.Parents.Length == 0 ? "racine" : string.Join(", ", msg.Parents.Select(p => p.Length >= 8 ? p[..8] : p));
				sb.AppendLine($"• [{msg.From}] : {msg.Message}");
				sb.AppendLine($"  (ID: {shortHash}... | Parents: {parentsStr})");
				sb.AppendLine();
			}
			DagMessagesLabel.Text = sb.ToString().TrimEnd();
		}
				private async void OnDisconnectClicked(object sender, EventArgs e)
		{
		#if ANDROID
				if (_transport != null)
				{
					await _transport.StopAsync();
				}
		#else
				await DisplayAlert("Info", "Déconnexion disponible sur Android.", "OK");
		#endif
		}



}
