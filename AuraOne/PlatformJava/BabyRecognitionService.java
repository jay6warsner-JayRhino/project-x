package za.co.auraone.assistant.bridge;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.speech.RecognitionService;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import java.util.ArrayList;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.json.JSONObject;
import org.vosk.Model;
import org.vosk.Recognizer;
import org.vosk.android.SpeechService;

/** Real local English recognition provider required by Android's assistant metadata. */
public final class BabyRecognitionService extends RecognitionService implements org.vosk.android.RecognitionListener {
 private final Handler main=new Handler(Looper.getMainLooper());
 private final ExecutorService worker=Executors.newSingleThreadExecutor();
 private volatile int generation;private volatile boolean destroyed;
 private Model model;private Recognizer decoder;private SpeechService speech;private Callback receiver;private boolean began;
 @Override protected void onStartListening(Intent request,Callback callback){
  if(receiver!=null){try{callback.error(SpeechRecognizer.ERROR_RECOGNIZER_BUSY);}catch(Exception ignored){}return;}
  String language=request.getStringExtra(RecognizerIntent.EXTRA_LANGUAGE);
  if(language!=null&&!language.toLowerCase(Locale.ROOT).startsWith("en")){try{callback.error(SpeechRecognizer.ERROR_LANGUAGE_NOT_SUPPORTED);}catch(Exception ignored){}return;}
  receiver=callback;began=false;final int id=++generation;
  worker.execute(()->{
   try{
    if(model==null)model=new Model(AuraLocalModels.speechModel(this).getAbsolutePath());
    if(destroyed||id!=generation)return;
    final Recognizer nextDecoder=new Recognizer(model,16000);final SpeechService nextSpeech=new SpeechService(nextDecoder,16000);
    main.post(()->{
     if(destroyed||id!=generation||receiver!=callback){dispose(nextSpeech,nextDecoder);return;}
     decoder=nextDecoder;speech=nextSpeech;
     try{callback.readyForSpeech(new Bundle());if(!speech.startListening(this,15000))fail(SpeechRecognizer.ERROR_AUDIO);}catch(Exception e){fail(SpeechRecognizer.ERROR_AUDIO);}
    });
   }catch(Throwable e){main.post(()->{if(id==generation)fail(SpeechRecognizer.ERROR_CLIENT);});}
  });
 }
 @Override protected void onStopListening(Callback callback){if(receiver!=callback)return;try{if(speech!=null)speech.stop();else fail(SpeechRecognizer.ERROR_NO_MATCH);}catch(Exception e){fail(SpeechRecognizer.ERROR_AUDIO);}}
 @Override protected void onCancel(Callback callback){if(receiver==callback){receiver=null;generation++;closeSpeech();}}
 private static void dispose(SpeechService service,Recognizer recognizer){try{service.cancel();service.shutdown();}catch(Throwable ignored){}finally{try{recognizer.close();}catch(Throwable ignored){}}}
 private void closeSpeech(){SpeechService old=speech;Recognizer oldDecoder=decoder;speech=null;decoder=null;if(old!=null&&oldDecoder!=null)dispose(old,oldDecoder);}
 private void fail(int code){Callback callback=receiver;receiver=null;generation++;closeSpeech();if(callback!=null)try{callback.error(code);}catch(Exception ignored){}}
 private Bundle result(String text){Bundle bundle=new Bundle();ArrayList<String> matches=new ArrayList<>();matches.add(text);bundle.putStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION,matches);return bundle;}
 private void finish(String json){if(receiver==null)return;try{String text=new JSONObject(json).optString("text","").trim();if(text.isEmpty()){fail(SpeechRecognizer.ERROR_NO_MATCH);return;}Callback callback=receiver;receiver=null;generation++;closeSpeech();callback.endOfSpeech();callback.results(result(text));}catch(Exception e){fail(SpeechRecognizer.ERROR_CLIENT);}}
 @Override public void onPartialResult(String json){if(receiver==null)return;try{String text=new JSONObject(json).optString("partial","").trim();if(text.isEmpty())return;if(!began){began=true;receiver.beginningOfSpeech();}receiver.partialResults(result(text));}catch(Exception ignored){}}
 @Override public void onResult(String json){finish(json);}
 @Override public void onFinalResult(String json){finish(json);}
 @Override public void onError(Exception error){fail(SpeechRecognizer.ERROR_AUDIO);}
 @Override public void onTimeout(){fail(SpeechRecognizer.ERROR_SPEECH_TIMEOUT);}
 @Override public void onDestroy(){destroyed=true;generation++;receiver=null;closeSpeech();worker.execute(()->main.post(()->{if(model!=null){try{model.close();}catch(Throwable ignored){}model=null;}}));worker.shutdown();super.onDestroy();}
}
