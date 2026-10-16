package za.co.auraone.assistant.bridge;
import android.content.Context;
import java.io.*;
import java.util.zip.*;
import com.google.ai.edge.litertlm.*;
/** Local inference never sends audio or model prompts over the network. */
public final class AuraLocalModels {
 private AuraLocalModels(){} private static Engine engine;private static volatile Conversation current;private static volatile boolean cancelled;
 public static synchronized String generate(Context context,String prompt){
  File file;
  try{file=languageModel(context);}catch(Exception error){return "ERROR:The bundled language model could not be prepared ("+error.getClass().getSimpleName()+"). Check available phone storage.";}
  if(prompt==null||prompt.length()>16000)return "ERROR:Offline context exceeds 16,000 characters.";
  try{cancelled=false;
   if(engine==null){engine=new Engine(new EngineConfig(file.getPath(),new Backend.CPU(2,null),null,null,2048,null,context.getCacheDir().getPath(),null));engine.initialize();}
   if(cancelled)return "ERROR:Cancelled.";
   try(Conversation conversation=engine.createConversation(new ConversationConfig())){current=conversation;String answer=conversation.sendMessage(prompt+"\n/no_think",java.util.Collections.emptyMap(),null,null,null,256,new ThinkingConfig(false,0),null).toString();return answer.length()>16000?answer.substring(0,16000):answer;}finally{current=null;}
  }catch(Throwable error){android.util.Log.w("AuraLocal", "Inference failure: "+error.getClass().getSimpleName()+": "+error.getMessage());release();return "ERROR:Offline model could not run ("+error.getClass().getSimpleName()+"). Choose a compatible CPU model.";}
 }
 public static synchronized void release(){if(engine!=null){try{engine.close();}catch(Throwable ignored){}engine=null;}}
 public static void cancel(){cancelled=true;Conversation conversation=current;if(conversation!=null)try{conversation.cancelProcess();}catch(Throwable ignored){}}
 public static File languageModel(Context context)throws Exception{
  File file=new File(context.getFilesDir(),"offline-model.litertlm");if(file.isFile()&&file.length()>1024)return file;
  File temporary=new File(context.getFilesDir(),"offline-model.preparing");java.security.MessageDigest hash=java.security.MessageDigest.getInstance("SHA-256");long total=0;
  try(InputStream input=context.getAssets().open("offline-model.litertlm");OutputStream output=new FileOutputStream(temporary)){
   byte[] bytes=new byte[65536];int count;while((count=input.read(bytes))!=-1){total+=count;if(total>350000000L)throw new IOException("Model exceeds bound");hash.update(bytes,0,count);output.write(bytes,0,count);}
  }
  StringBuilder digest=new StringBuilder();for(byte b:hash.digest())digest.append(String.format(java.util.Locale.ROOT,"%02x",b&255));
  if(total!=344671744L||!digest.toString().equals("03e7da1eb1108b50dffaa9bb52cc7bcbad2eb0c66ca990267f480c1e545d2856"))throw new IOException("Model integrity failed");
  if(!temporary.renameTo(file))throw new IOException("Model installation failed");return file;
 }
 public static String testSpeech(Context context){
  try(org.vosk.Model model=new org.vosk.Model(speechModel(context).getAbsolutePath());org.vosk.Recognizer recognizer=new org.vosk.Recognizer(model,16000);InputStream input=context.getAssets().open("offline-test.wav")){
   byte[] header=new byte[44];int position=0;while(position<44){int count=input.read(header,position,44-position);if(count<0)throw new IOException("Invalid test WAV");position+=count;}
   byte[] buffer=new byte[4096];int count;StringBuilder transcript=new StringBuilder();while((count=input.read(buffer))>0){if(recognizer.acceptWaveForm(buffer,count))transcript.append(new org.json.JSONObject(recognizer.getResult()).optString("text")).append(' ');}
   transcript.append(new org.json.JSONObject(recognizer.getFinalResult()).optString("text"));
   return transcript.toString().contains("one zero")?"Offline speech engine check passed.":"Offline speech test did not produce the expected words.";
  }catch(Throwable error){return "Offline speech setup failed ("+error.getClass().getSimpleName()+").";}
 }
 public static File speechModel(Context context)throws Exception{
  File root=new File(context.getFilesDir(),"speech-model"),model=new File(root,"vosk-model-small-en-us-0.15"),ready=new File(root,".ready");
  if(ready.isFile()&&new File(model,"am/final.mdl").isFile())return model;
  if(!root.exists()&&!root.mkdirs())throw new IOException("Model folder unavailable");
  String prefix=root.getCanonicalPath()+File.separator;long total=0;int files=0;
  try(ZipInputStream zip=new ZipInputStream(context.getAssets().open("vosk-model-small-en-us-0.15.zip"))){
   ZipEntry entry;byte[] buffer=new byte[32768];while((entry=zip.getNextEntry())!=null){
    if(++files>1000)throw new IOException("Too many model entries");
    File target=new File(root,entry.getName());if(!target.getCanonicalPath().startsWith(prefix)||entry.getName().contains("\\"))throw new IOException("Invalid model path");
    if(entry.isDirectory()){target.mkdirs();continue;}target.getParentFile().mkdirs();
    try(OutputStream output=new FileOutputStream(target)){int size;while((size=zip.read(buffer))!=-1){total+=size;if(total>150L*1024*1024)throw new IOException("Model exceeds limit");output.write(buffer,0,size);}}
   }
  }
  if(!new File(model,"am/final.mdl").isFile())throw new IOException("Speech model is incomplete");
  try(OutputStream output=new FileOutputStream(ready)){output.write(1);}return model;
 }
}
