using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
namespace AuraOne;
[Service(Exported=false,ForegroundServiceType=ForegroundService.TypeMicrophone)]
public sealed class LiveService:Service
{
    public static event Action? StopRequested;
    public override IBinder? OnBind(Intent? intent)=>null;
    public override StartCommandResult OnStartCommand(Intent? intent,StartCommandFlags flags,int startId)
    {
        if(intent?.Action=="stop"){StopRequested?.Invoke();StopSelf();return StartCommandResult.NotSticky;}
        var manager=(NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("voice","Live voice sessions",NotificationImportance.Low));
        var stop=PendingIntent.GetService(this,2,new Intent(this,typeof(LiveService)).SetAction("stop"),PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable);
        var open=PendingIntent.GetActivity(this,2,new Intent(this,typeof(MainActivity)),PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable);
        var notification=new Notification.Builder(this,"voice").SetSmallIcon(Android.Resource.Drawable.IcDialogInfo).SetContentTitle("Aura One · Gemini Live").SetContentText("Voice session enabled. Tap Stop to end this session.").SetContentIntent(open).SetOngoing(true).AddAction(new Notification.Action.Builder(null,"Stop",stop).Build()).Build();
        if(OperatingSystem.IsAndroidVersionAtLeast(30))StartForeground(901,notification,ForegroundService.TypeMicrophone);else StartForeground(901,notification);
        return StartCommandResult.NotSticky;
    }
}
