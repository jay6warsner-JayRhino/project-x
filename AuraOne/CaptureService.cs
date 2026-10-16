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
    MediaProjection? projection;ImageReader? reader;VirtualDisplay? display;HandlerThread? thread;Handler? frames;long last;bool stopped;int width,height,density,generation;
    static int currentGeneration;
    static int activeGeneration;static long capturedFrames;
    public static bool Active=>Volatile.Read(ref activeGeneration)!=0;
    public static long CapturedFrames=>Interlocked.Read(ref capturedFrames);
    public static int NewSession()=>Interlocked.Increment(ref currentGeneration);
    public static event Action<string>? Failed;
    public override IBinder? OnBind(Intent? intent)=>null;
    public override StartCommandResult OnStartCommand(Intent? intent,StartCommandFlags flags,int startId)
    {
        if(intent?.Action=="stop"){StopSelf();return StartCommandResult.NotSticky;}
        try
        {
            generation=intent?.GetIntExtra("generation",0)??0;stopped=false;
            var manager=(NotificationManager)GetSystemService(NotificationService)!;manager.CreateNotificationChannel(new NotificationChannel("screen","Screen sharing",NotificationImportance.Low));
            var stop=PendingIntent.GetService(this,1,new Intent(this,typeof(CaptureService)).SetAction("stop"),PendingIntentFlags.UpdateCurrent|PendingIntentFlags.Immutable);
            var notification=new Notification.Builder(this,"screen").SetSmallIcon(Android.Resource.Drawable.IcMenuView).SetContentTitle("Aura One is sharing your screen").SetContentText("Tap Stop to end sharing.").SetOngoing(true).AddAction(new Notification.Action.Builder(null,"Stop",stop).Build()).Build();
            if(OperatingSystem.IsAndroidVersionAtLeast(29))StartForeground(900,notification,ForegroundService.TypeMediaProjection);else StartForeground(900,notification);
            var data=intent?.GetParcelableExtra("data") as Intent;if(data==null){StopSelf();return StartCommandResult.NotSticky;}
            var service=(MediaProjectionManager)GetSystemService(MediaProjectionService)!;projection=service.GetMediaProjection(intent!.GetIntExtra("result",-1),data);
            thread=new HandlerThread("AuraScreen");thread.Start();frames=new Handler(thread.Looper!);projection!.RegisterCallback(new ProjectionCallback(this,generation),frames);
            var metrics=Resources!.DisplayMetrics!;width=metrics.WidthPixels;height=metrics.HeightPixels;density=(int)metrics.DensityDpi;
            reader=NewReader(width,height);
            display=projection.CreateVirtualDisplay("AuraScreen",width,height,density,(Android.Views.DisplayFlags)(int)VirtualDisplayFlags.AutoMirror,reader.Surface,null,frames);
            Volatile.Write(ref activeGeneration,generation);
        }
        catch(Exception e){if(generation==Volatile.Read(ref currentGeneration))Failed?.Invoke("Screen sharing could not start ("+e.GetType().Name+"). Approve a new sharing session.");StopSelf();}
        return StartCommandResult.NotSticky;
    }
    ImageReader NewReader(int w,int h){var next=ImageReader.NewInstance(w,h,(ImageFormatType)1,2);next.SetOnImageAvailableListener(this,frames);return next;}
    sealed class ProjectionCallback(CaptureService owner,int id):MediaProjection.Callback
    {
        public override void OnStop(){if(owner.generation==id)owner.StopSelf();}
        public override void OnCapturedContentResize(int width,int height){if(owner.generation==id)owner.ResizeCapture(width,height);}
    }
    void ResizeCapture(int w,int h)
    {
        if(stopped||display==null||w<=0||h<=0||(w==width&&h==height))return;
        try{var next=NewReader(w,h);var old=reader;display.Resize(w,h,density);display.Surface=next.Surface;reader=next;width=w;height=h;old?.Close();}
        catch{Failed?.Invoke("Screen size changed and sharing stopped. Start sharing again.");StopSelf();}
    }
    public void OnImageAvailable(ImageReader? source)
    {
        try{using var image=source?.AcquireLatestImage();if(image==null||System.Environment.TickCount64-last<1500||stopped||generation!=Volatile.Read(ref currentGeneration))return;last=System.Environment.TickCount64;var plane=image.GetPlanes()![0];var buffer=plane.Buffer!;var pixel=plane.PixelStride;var paddedWidth=plane.RowStride/pixel;using var raw=Bitmap.CreateBitmap(paddedWidth,image.Height,Bitmap.Config.Argb8888!);raw.CopyPixelsFromBuffer(buffer);using var cropped=Bitmap.CreateBitmap(raw,0,0,image.Width,image.Height);var scale=Math.Min(1d,768d/Math.Max(cropped.Width,cropped.Height));using var resized=Bitmap.CreateScaledBitmap(cropped,(int)(cropped.Width*scale),(int)(cropped.Height*scale),true);using var output=new MemoryStream();resized.Compress(Bitmap.CompressFormat.Jpeg!,65,output);if(!stopped&&generation==Volatile.Read(ref currentGeneration)){Interlocked.Increment(ref capturedFrames);Frame?.Invoke("screen",Convert.ToBase64String(output.ToArray()));}}catch{/* Reader replacement or protected screens can invalidate one frame. */}
    }
    public override void OnDestroy(){stopped=true;Interlocked.CompareExchange(ref activeGeneration,0,generation);try{display?.Release();}catch{}try{reader?.Close();}catch{}try{projection?.Stop();}catch{}thread?.QuitSafely();if(generation==Volatile.Read(ref currentGeneration))Ended?.Invoke();base.OnDestroy();}
}
