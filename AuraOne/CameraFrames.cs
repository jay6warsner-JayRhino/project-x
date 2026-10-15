using Android.Content;
using Android.Graphics;
using Android.Views;
using Camera=Android.Hardware.Camera;
namespace AuraOne;
#pragma warning disable CS0618
public sealed class CameraFrames:SurfaceView,ISurfaceHolderCallback,Camera.IPreviewCallback
{
    readonly Action<string> onFrame;Camera? camera;int cameraId;long last;
    public CameraFrames(Context context,Action<string> frame):base(context){onFrame=frame;Holder!.AddCallback(this);}
    public void SurfaceCreated(ISurfaceHolder holder)=>Start();
    public void SurfaceChanged(ISurfaceHolder holder,Format format,int width,int height){}
    public void SurfaceDestroyed(ISurfaceHolder holder)=>Close();
    void Start(){try{camera=Camera.Open(cameraId);var p=camera.GetParameters()!;var size=p.SupportedPreviewSizes?.Where(x=>x.Width<=1280).OrderByDescending(x=>x.Width).FirstOrDefault();if(size!=null)p.SetPreviewSize(size.Width,size.Height);p.PreviewFormat=ImageFormatType.Nv21;camera.SetParameters(p);camera.SetDisplayOrientation(90);camera.SetPreviewDisplay(Holder);camera.SetPreviewCallback(this);camera.StartPreview();}catch(Exception e){Android.Widget.Toast.MakeText(Context,"Camera: "+e.Message,Android.Widget.ToastLength.Long)?.Show();Close();}}
    public void Flip(){Close();cameraId=(cameraId+1)%Math.Max(1,Camera.NumberOfCameras);Start();}
    public void Close(){try{camera?.SetPreviewCallback(null);camera?.StopPreview();camera?.Release();camera?.Dispose();}catch{}camera=null;}
    public void OnPreviewFrame(byte[]? bytes,Camera? cam)
    {
        if(bytes==null||cam==null||Environment.TickCount64-last<1500)return;last=Environment.TickCount64;
        try{var size=cam.GetParameters()!.PreviewSize!;using var yuv=new YuvImage(bytes,ImageFormatType.Nv21,size.Width,size.Height,null);using var jpeg=new MemoryStream();yuv.CompressToJpeg(new Rect(0,0,size.Width,size.Height),65,jpeg);using var bitmap=BitmapFactory.DecodeByteArray(jpeg.ToArray(),0,(int)jpeg.Length);if(bitmap==null)return;using var matrix=new Matrix();matrix.PostRotate(90);using var rotated=Bitmap.CreateBitmap(bitmap,0,0,bitmap.Width,bitmap.Height,matrix,true);using var output=new MemoryStream();rotated.Compress(Bitmap.CompressFormat.Jpeg!,65,output);onFrame(Convert.ToBase64String(output.ToArray()));}catch{/* Skip an unusable frame. */}
    }
}
