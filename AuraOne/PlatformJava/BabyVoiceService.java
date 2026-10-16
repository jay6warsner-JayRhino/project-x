package za.co.auraone.assistant.bridge;
import android.service.voice.VoiceInteractionService;
/** Entry point for Android's optional default-assistant role; no hotword bypass. */
public final class BabyVoiceService extends VoiceInteractionService {
 static volatile BabyVoiceService active;
 @Override public void onReady(){super.onReady();active=this;}
 @Override public void onShutdown(){if(active==this)active=null;super.onShutdown();}
}
