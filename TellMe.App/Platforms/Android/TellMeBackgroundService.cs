using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Android.Content.PM;


namespace TellMe.App.Platforms.Android;
[Service (Exported=false, ForegroundServiceType=ForegroundService.TypeConnectedDevice)]
public class TellMeBackgroundService : Service{

    public override IBinder? OnBind(Intent? intent){ return null;}
    private const int NotificationId = 1001;
    private const string ChannelId = "tellme_background_channel";
    private const string ChannelName = "Service TellMe P2P";

    public override StartCommandResult OnStartCommand(Intent? intent,StartCommandFlags flags,int startId){

        CreateNotificationChannel();
        //construire la notif
        var notification = new NotificationCompat.Builder(this,ChannelId).SetContentTitle("TellMe")
        .SetContentText("relais Bluetooth actif en arriere-plan")
        .SetSmallIcon(global::Android.Resource.Drawable.StatNotifySync)
        .SetOngoing(true)
        .Build();
        
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q){
            StartForeground(NotificationId,notification,ForegroundService.TypeConnectedDevice);
        }
        else {
            StartForeground(NotificationId,notification);
        }

        return StartCommandResult.Sticky;
    }

    private void CreateNotificationChannel(){
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O){
            var channel = new NotificationChannel(ChannelId,ChannelName,NotificationImportance.Low)
            {
                Description = "Maintient l'ecoute bluetooth active en arriere plan"
            };
            var NotificationManager = (NotificationManager?)GetSystemService(NotificationService);
            NotificationManager?.CreateNotificationChannel(channel);
        }
    }
    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(TellMeBackgroundService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(TellMeBackgroundService));
        context.StopService(intent);
    }

}