package za.co.auraone.assistant.bridge;
import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.os.*;
import org.json.*;
import org.vosk.*;
import org.vosk.android.*;
import java.util.concurrent.*;
/** User-started local microphone service; no boot auto-start or lock-screen bypass. */
public final class AuraWakeService extends Service implements RecognitionListener {
 private static final Handler MAIN=new Handler(Looper.getMainLooper());
 private static final ConcurrentLinkedQueue<String> EVENTS=new ConcurrentLinkedQueue<>();
 private static volatile AuraWakeService active;private static volatile boolean visible;
 private final ExecutorService worker=Executors.newSingleThreadExecutor();
 private SpeechService speech;private Recognizer decoder;private Model model;private boolean paused,stopped,loading;private String mode="wake",assistant="jarvis",error="";private long lastWake;private int generation;
 public static void setVisible(boolean value){visible=value;}
 public static String poll(){return EVENTS.poll();}
 private static void event(String type,String text){try{if(EVENTS.size()<16)EVENTS.add(new JSONObject().put("type",type).put("text",text).toString());}catch(Exception ignored){}}
 public static String status(){try{AuraWakeService s=active;return new JSONObject().put("running",s!=null).put("mode",s==null?"stopped":s.mode).put("paused",s!=null&&s.paused).put("loading",s!=null&&s.loading).put("error",s==null?"":s.error).toString();}catch(Exception e){return "{}";}}
 public static void start(Context context,String mode,String name){EVENTS.clear();context.startForegroundService(new Intent(context,AuraWakeService.class).putExtra("mode",mode).putExtra("name",name));}
 public static void stop(Context context){context.stopService(new Intent(context,AuraWakeService.class));EVENTS.clear();}
 public static void pause(){AuraWakeService s=active;if(s!=null){s.paused=true;s.generation++;s.closeSpeech();s.notice("Paused during conversation");}}
 public static void resume(){AuraWakeService s=active;if(s!=null){s.paused=false;s.listen();}}
 @Override public IBinder onBind(Intent intent){return null;}
 @Override public void onCreate(){super.onCreate();active=this;getSystemService(NotificationManager.class).createNotificationChannel(new NotificationChannel("wake","Local voice and wake word",NotificationManager.IMPORTANCE_LOW));}
 @Override public int onStartCommand(Intent intent,int flags,int id){
  if(intent!=null&&"stop".equals(intent.getAction())){stopSelf();return START_NOT_STICKY;}
  mode=intent==null?"wake":intent.getStringExtra("mode");if(!"offline".equals(mode))mode="wake";
  assistant=intent==null?"jarvis":intent.getStringExtra("name");if(assistant==null||assistant.length()>60)assistant="jarvis";
  stopped=false;paused=false;generation++;closeSpeech();notice("Preparing local speech model");listen();return START_NOT_STICKY;
 }
 private void notice(String text){
  PendingIntent open=PendingIntent.getActivity(this,906,new Intent().setClassName(getPackageName(),"za.co.auraone.assistant.MainActivity").putExtra("aura_start_voice",true).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_SINGLE_TOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  PendingIntent stop=PendingIntent.getService(this,906,new Intent(this,AuraWakeService.class).setAction("stop"),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  Notification notification=new Notification.Builder(this,"wake").setSmallIcon(android.R.drawable.ic_btn_speak_now).setContentTitle("Aura One · Local "+mode).setContentText(text).setContentIntent(open).setOngoing(true).addAction(new Notification.Action.Builder(null,"Stop",stop).build()).build();
  if(Build.VERSION.SDK_INT>=30)startForeground(906,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE);else startForeground(906,notification);
 }
 private void listen(){
  if(stopped||paused||loading||speech!=null)return;loading=true;int request=++generation;
  worker.execute(()->{try{
   if(model==null)model=new Model(AuraLocalModels.speechModel(this).getAbsolutePath());final Model ready=model;
   MAIN.post(()->{loading=false;if(stopped){if(model!=null){model.close();model=null;}return;}if(paused||request!=generation){if(!paused)listen();return;}
    try{Recognizer recognizer;
     if("wake".equals(mode)){JSONArray words=new JSONArray();for(String word:new String[]{"hey jarvis","jarvis","my baby","hey baby","baby","hey aura","aura",assistant.toLowerCase(),"[unk]"})words.put(word);recognizer=new Recognizer(ready,16000,words.toString());}
     else recognizer=new Recognizer(ready,16000);
     decoder=recognizer;recognizer.setWords(true);speech=new SpeechService(recognizer,16000);speech.startListening(this);notice("wake".equals(mode)?"Listening locally for Hey Baby or your wake phrase":"Listening locally; audio stays on this phone");event("ready",mode);
    }catch(Throwable e){closeSpeech();error="Local microphone unavailable ("+e.getClass().getSimpleName()+")";event("error",error);stopSelf();}
   });
  }catch(Throwable e){MAIN.post(()->{loading=false;error="Local speech setup failed ("+e.getClass().getSimpleName()+")";event("error",error);stopSelf();});}});
 }
 private void closeSpeech(){SpeechService old=speech;speech=null;Recognizer previous=decoder;decoder=null;try{if(old!=null){old.cancel();old.shutdown();}}catch(Throwable ignored){}finally{if(previous!=null)try{previous.close();}catch(Throwable ignored){}}}
 @Override public void onResult(String result){MAIN.post(()->recognized(result));}
 @Override public void onFinalResult(String result){MAIN.post(()->recognized(result));}
 @Override public void onPartialResult(String result){}
 private void recognized(String result){
  if(paused||stopped)return;
  try{JSONObject parsed=new JSONObject(result);String text=parsed.optString("text","").trim();if(text.isEmpty())return;
   if("offline".equals(mode)){pause();event("speech",text);return;}
   String low=text.toLowerCase();boolean found=false;for(String phrase:new String[]{"hey jarvis","jarvis","my baby","hey baby","baby","hey aura","aura",assistant.toLowerCase()})if(low.equals(phrase))found=true;
   JSONArray words=parsed.optJSONArray("result");double confidence=0;int count=words==null?0:words.length();for(int i=0;i<count;i++)confidence+=words.getJSONObject(i).optDouble("conf",0);
   if(!found||count==0||confidence/count<0.75||SystemClock.elapsedRealtime()-lastWake<6000)return;lastWake=SystemClock.elapsedRealtime();pause();
   if(visible)event("wake",text);
   else if(BabyVoiceService.active!=null){try{BabyVoiceService.active.showSession(new Bundle(),0);}catch(Exception e){notice("Wake heard. Tap to talk.");}}
   else notice("Wake heard. Tap to talk, or choose Aura as default assistant for hands-free launch.");
  }catch(Exception ignored){}
 }
 @Override public void onError(Exception exception){MAIN.post(()->{error="Local speech error ("+exception.getClass().getSimpleName()+")";event("error",error);pause();});}
 @Override public void onTimeout(){MAIN.post(()->{closeSpeech();listen();});}
 @Override public void onDestroy(){stopped=true;generation++;closeSpeech();worker.shutdownNow();if(model!=null&&!loading){model.close();model=null;}if(active==this)active=null;stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();}
}
