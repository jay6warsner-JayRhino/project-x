using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
namespace AuraOne;
[Service(Exported=false,ForegroundServiceType=ForegroundService.TypeMediaProjection)]
public sealed class CaptureService:Service,ImageReader.IOnImageAvailableListener
{
    public static event Action<string,string>? Frame;public static event Action? Ended;
    MediaProjection? projection;ImageReader? reader;VirtualDisplay? display;HandlerThread? thread;long last;bool stopped;
    public override IBinder? OnBind(Intent? intent)=>null;
    public override StartCommandResult OnStartCommand(Intent? intent,StartCommandFlags flags,int startId)
    {
        if(intent?.Action=="stop"){StopSelf();return StartCommandResult.NotSticky;}
        try
        {
            var manager=(NotificationManager)GetSystemService(NotificationService)!;manager.CreateNotificationChannel(new NotificationChannel("screen","Screen sharing",NotificationImportance.Low));
            var stop=PendingIntent.GetService(this,1,new Intent(this,typeof(CaptureService)).SetAction("stop"),PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable);
            var notification=new Notification.Builder(this,"screen").SetSmallIcon(Android.Resource.Drawable.IcMenuView).SetContentTitle("Aura One is sharing your screen").SetContentText("Tap Stop to end sharing.").SetOngoing(true).AddAction(new Notification.Action.Builder(null,"Stop",stop).Build()).Build();
            if(OperatingSystem.IsAndroidVersionAtLeast(29))StartForeground(900,notification,ForegroundService.TypeMediaProjection);else StartForeground(900,notification);
            var data=intent?.GetParcelableExtra("data") as Intent;if(data==null){StopSelf();return StartCommandResult.NotSticky;}
            var service=(MediaProjectionManager)GetSystemService(MediaProjectionService)!;projection=service.GetMediaProjection(intent!.GetIntExtra("result",-1),data);
            thread=new HandlerThread("AuraScreen");thread.Start();var handler=new Handler(thread.Looper!);projection!.RegisterCallback(new ProjectionCallback(this),handler);
            var metrics=Resources!.DisplayMetrics!;var width=metrics.WidthPixels;var height=metrics.HeightPixels;
            reader=ImageReader.NewInstance(width,height,(ImageFormatType)1,2);reader.SetOnImageAvailableListener(this,handler);
            display=projection.CreateVirtualDisplay("AuraScreen",width,height,(int)metrics.DensityDpi,(Android.Views.DisplayFlags)(int)VirtualDisplayFlags.AutoMirror,reader.Surface,null,handler);
        }
        catch{StopSelf();}
        return StartCommandResult.NotSticky;
    }
    sealed class ProjectionCallback(CaptureService owner):MediaProjection.Callback{public override void OnStop()=>owner.StopSelf();}
    public void OnImageAvailable(ImageReader? source)
    {
        using var image=source?.AcquireLatestImage();if(image==null||System.Environment.TickCount64-last<1500||stopped)return;last=System.Environment.TickCount64;
        try{var plane=image.GetPlanes()![0];var buffer=plane.Buffer!;var pixel=plane.PixelStride;var paddedWidth=plane.RowStride/pixel;using var raw=Bitmap.CreateBitmap(paddedWidth,image.Height,Bitmap.Config.Argb8888!);raw.CopyPixelsFromBuffer(buffer);using var cropped=Bitmap.CreateBitmap(raw,0,0,image.Width,image.Height);var scale=Math.Min(1d,768d/Math.Max(cropped.Width,cropped.Height));using var resized=Bitmap.CreateScaledBitmap(cropped,(int)(cropped.Width*scale),(int)(cropped.Height*scale),true);using var output=new MemoryStream();resized.Compress(Bitmap.CompressFormat.Jpeg!,65,output);Frame?.Invoke("screen",Convert.ToBase64String(output.ToArray()));}catch{/* Display size changes can invalidate individual frames. */}
    }
    public override void OnDestroy(){stopped=true;display?.Release();reader?.Close();projection?.Stop();thread?.QuitSafely();Ended?.Invoke();base.OnDestroy();}
}
