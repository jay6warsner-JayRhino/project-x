using Android.Runtime;
using System.Text.Json;
namespace AuraOne;
public static class NativeDeviceTools
{
    const string Class="za/co/auraone/assistant/bridge/AuraDeviceBridge";
    static readonly SemaphoreSlim Requests=new(1,1);
    static void Invoke(string method,string signature,params JValue[] args)
    {var cls=JNIEnv.FindClass(Class);try{JNIEnv.CallStaticVoidMethod(cls,JNIEnv.GetStaticMethodID(cls,method,signature),args);}finally{JNIEnv.DeleteGlobalRef(cls);}}
    static string? Text(string method,string signature,params JValue[] args)
    {var cls=JNIEnv.FindClass(Class);try{return JNIEnv.GetString(JNIEnv.CallStaticObjectMethod(cls,JNIEnv.GetStaticMethodID(cls,method,signature),args),JniHandleOwnership.TransferLocalRef);}finally{JNIEnv.DeleteGlobalRef(cls);}}
    public static string Status()=>Text("status","()Ljava/lang/String;")??"{}";
    public static async Task<object> Run(Activity activity,string action,string value,CancellationToken cancellation,Func<bool>? stillAllowed=null)
    {
        await Requests.WaitAsync(cancellation);
        try{if(stillAllowed?.Invoke()==false)throw new OperationCanceledException("Device tool was cancelled.");return await RunCore(activity,action,value,cancellation,stillAllowed);}
        finally{Requests.Release();}
    }
    static async Task<object> RunCore(Activity activity,string action,string value,CancellationToken cancellation,Func<bool>? stillAllowed)
    {
        DeviceActions.Validate(action,value);cancellation.ThrowIfCancellationRequested();
        var id=Guid.NewGuid().ToString("N");var nativeId=JNIEnv.NewString(id);
        var request=JNIEnv.NewString(JsonSerializer.Serialize(new{action,value},Brain.Json));
        try
        {
            Invoke("begin","(Landroid/app/Activity;Ljava/lang/String;Ljava/lang/String;)V",new JValue(activity),new JValue(request),new JValue(nativeId));
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(90));
            while(true)
            {
                await Task.Delay(100,deadline.Token);
                if(stillAllowed?.Invoke()==false)throw new OperationCanceledException("Device tool was cancelled.");
                var result=Text("poll","(Ljava/lang/String;)Ljava/lang/String;",new JValue(nativeId));
                if(result!=null)return new{result};
            }
        }
        finally
        {
            Invoke("cancel","(Ljava/lang/String;)V",new JValue(nativeId));JNIEnv.DeleteLocalRef(request);JNIEnv.DeleteLocalRef(nativeId);
        }
    }
}
