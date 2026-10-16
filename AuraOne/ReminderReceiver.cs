using Android.App;
using Android.Content;
using Android.OS;
using Android.Speech.Tts;
namespace AuraOne;
[BroadcastReceiver(Enabled=true,Exported=false)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class ReminderReceiver:BroadcastReceiver
{
    public static event Action<int>? Delivered;
    public static void Schedule(Context context,Reminder reminder)
    {
        if(reminder.Done||reminder.At<=DateTimeOffset.Now)return;
        var intent=new Intent(context,typeof(ReminderReceiver));intent.PutExtra("id",reminder.Id);
        var pending=PendingIntent.GetBroadcast(context,reminder.Id,intent,PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable)!;
        var alarms=(AlarmManager)context.GetSystemService(Context.AlarmService)!;
        alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup,reminder.At.ToUnixTimeMilliseconds(),pending);
    }
    public static void Cancel(Context context,Reminder reminder)
    {var pending=PendingIntent.GetBroadcast(context,reminder.Id,new Intent(context,typeof(ReminderReceiver)),PendingIntentFlags.NoCreate|PendingIntentFlags.Immutable);if(pending!=null){((AlarmManager)context.GetSystemService(Context.AlarmService)!).Cancel(pending);pending.Cancel();}}
    public static void Reschedule(Context context,Brain brain){foreach(var r in brain.Reminders.Where(x=>!x.Done&&x.At>DateTimeOffset.Now))Schedule(context,r);}
    public override void OnReceive(Context? context,Intent? intent)
    {
        if(context==null)return;var store=new BrainStore(context.FilesDir!.AbsolutePath);var brain=store.Load();
        if(intent?.Action==Intent.ActionBootCompleted){Reschedule(context,brain);return;}
        var reminder=brain.Reminders.FirstOrDefault(x=>x.Id==intent?.GetIntExtra("id",-1)&&!x.Done);if(reminder==null)return;
        var manager=(NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if(OperatingSystem.IsAndroidVersionAtLeast(26))manager.CreateNotificationChannel(new NotificationChannel("reminders","Aura reminders",NotificationImportance.High));
        var open=PendingIntent.GetActivity(context,reminder.Id,new Intent(context,typeof(MainActivity)),PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable);
        var builder=OperatingSystem.IsAndroidVersionAtLeast(26)?new Notification.Builder(context,"reminders"):new Notification.Builder(context);
        builder.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo).SetContentTitle("Aura One reminder").SetContentText(reminder.Text).SetStyle(new Notification.BigTextStyle().BigText(reminder.Text)).SetAutoCancel(true).SetContentIntent(open);
        try{manager.Notify(reminder.Id,builder.Build());reminder.Done=true;store.Save(brain);Delivered?.Invoke(reminder.Id);if(brain.SpeakReminders)new ReminderSpeech(context.ApplicationContext!,GoAsync(),brain,reminder.Text).Start();}catch(Java.Lang.SecurityException){/* Notification permission can be revoked at any time. */}
    }
    // Keep the broadcast alive briefly for spoken delivery. The notification remains
    // available when TTS is missing, slow, muted by the device, or cannot initialise.
    sealed class ReminderSpeech(Context context,PendingResult? pending,Brain brain,string text):Java.Lang.Object,TextToSpeech.IOnInitListener
    {
        TextToSpeech? speech;readonly Handler handler=new(Looper.MainLooper!);int finished;
        public void Start(){speech=new TextToSpeech(context,this);handler.PostDelayed(Finish,9000);}
        public void OnInit(OperationResult result){if(result!=OperationResult.Success){Finish();return;}speech?.SetLanguage(Java.Util.Locale.ForLanguageTag(brain.Language));speech?.SetSpeechRate(brain.VoiceRate);speech?.SetPitch(brain.VoicePitch);speech?.SetOnUtteranceProgressListener(new SpeechProgress(this));speech?.Speak("Reminder. "+text,QueueMode.Flush,new Bundle(),Guid.NewGuid().ToString());}
        public void Finish(){if(Interlocked.Exchange(ref finished,1)!=0)return;handler.Post(()=>{speech?.Stop();speech?.Shutdown();pending?.Finish();});}
        sealed class SpeechProgress(ReminderSpeech owner):UtteranceProgressListener{public override void OnStart(string? id){}public override void OnDone(string? id)=>owner.Finish();[Obsolete]public override void OnError(string? id)=>owner.Finish();}
    }
}
