using Android.Content;
using Android.Runtime;
namespace AuraOne;
public static class NativeLocal
{
 const string Speech="za/co/auraone/assistant/bridge/AuraWakeService",Models="za/co/auraone/assistant/bridge/AuraLocalModels";
 static readonly SemaphoreSlim Inference=new(1,1);
 static string? Text(string name,string method,string signature,params JValue[] values)
 {var cls=JNIEnv.FindClass(name);try{return JNIEnv.GetString(JNIEnv.CallStaticObjectMethod(cls,JNIEnv.GetStaticMethodID(cls,method,signature),values),JniHandleOwnership.TransferLocalRef);}finally{JNIEnv.DeleteGlobalRef(cls);}}
 static void Void(string name,string method,string signature,params JValue[] values)
 {var cls=JNIEnv.FindClass(name);try{JNIEnv.CallStaticVoidMethod(cls,JNIEnv.GetStaticMethodID(cls,method,signature),values);}finally{JNIEnv.DeleteGlobalRef(cls);}}
 public static string Status()=>Text(Speech,"status","()Ljava/lang/String;")??"{}";
 public static string? Poll()=>Text(Speech,"poll","()Ljava/lang/String;");
 public static void Visible(bool visible)=>Void(Speech,"setVisible","(Z)V",new JValue(visible));
 public static void Pause()=>Void(Speech,"pause","()V");
 public static void Resume()=>Void(Speech,"resume","()V");
 public static void Stop(Context context)=>Void(Speech,"stop","(Landroid/content/Context;)V",new JValue(context));
 public static void Start(Context context,string mode,string name)
 {var m=JNIEnv.NewString(mode);var n=JNIEnv.NewString(name);try{Void(Speech,"start","(Landroid/content/Context;Ljava/lang/String;Ljava/lang/String;)V",new JValue(context),new JValue(m),new JValue(n));}finally{JNIEnv.DeleteLocalRef(m);JNIEnv.DeleteLocalRef(n);}}
 public static void Cancel()=>Void(Models,"cancel","()V");
 public static async Task<string> Generate(Context context,string prompt,CancellationToken ct)
 {
  await Inference.WaitAsync(ct);
  try{using var registration=ct.Register(Cancel);var answer=await Task.Run(()=>{var p=JNIEnv.NewString(prompt);try{return Text(Models,"generate","(Landroid/content/Context;Ljava/lang/String;)Ljava/lang/String;",new JValue(context),new JValue(p))??"";}finally{JNIEnv.DeleteLocalRef(p);}},ct);ct.ThrowIfCancellationRequested();if(answer.StartsWith("ERROR:"))throw new InvalidOperationException(answer[6..]);return answer;}
  finally{Inference.Release();}
 }
 public static async Task<string> TestSpeech(Context context)=>await Task.Run(()=>Text(Models,"testSpeech","(Landroid/content/Context;)Ljava/lang/String;",new JValue(context))??"Offline test failed.");
 public static async Task<string> PdfPage(Context context,string path,int page)=>await Task.Run(()=>{var p=JNIEnv.NewString(path);try{var result=Text("za/co/auraone/assistant/bridge/AuraDocumentBridge","render","(Landroid/content/Context;Ljava/lang/String;I)Ljava/lang/String;",new JValue(context),new JValue(p),new JValue(page))??"ERROR:PDF render failed.";if(result.StartsWith("ERROR:"))throw new InvalidOperationException(result[6..]);return result;}finally{JNIEnv.DeleteLocalRef(p);}});
 public static async Task ImportModel(Context context,Stream stream,CancellationToken ct)
 {
  await Inference.WaitAsync(ct);
  try{
   Void(Models,"release","()V");var target=Path.Combine(context.FilesDir!.AbsolutePath,"offline-model.litertlm");var temporary=target+".importing";
   using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){long total=0;var buffer=new byte[65536];int count;while((count=await stream.ReadAsync(buffer,ct))>0){total+=count;if(total>3L*1024*1024*1024)throw new InvalidOperationException("Offline model exceeds 3 GB.");await file.WriteAsync(buffer.AsMemory(0,count),ct);}if(total<1024)throw new InvalidOperationException("The selected model is too small.");}
   File.Move(temporary,target,true);
  }finally{Inference.Release();}
 }
}
