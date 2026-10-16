using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
namespace AuraOne;
[Service(Exported=false,ForegroundServiceType=ForegroundService.TypeMicrophone)]
public sealed class LiveService:Service
{
    PowerManager.WakeLock? cpu;Handler? renewal;bool active;
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
        if(!active){active=true;renewal=new Handler(Looper.MainLooper!);var power=(PowerManager)GetSystemService(PowerService)!;cpu=power.NewWakeLock(WakeLockFlags.Partial,"AuraOne:LiveVoice");cpu.SetReferenceCounted(false);RenewCpu();}
        return StartCommandResult.NotSticky;
    }
    void RenewCpu(){if(!active)return;cpu?.Acquire(10*60*1000L);renewal?.PostDelayed(RenewCpu,5*60*1000L);}
    public override void OnDestroy(){active=false;renewal?.RemoveCallbacksAndMessages(null);try{if(cpu?.IsHeld==true)cpu.Release();}catch{}cpu?.Dispose();cpu=null;renewal?.Dispose();renewal=null;base.OnDestroy();}
}
