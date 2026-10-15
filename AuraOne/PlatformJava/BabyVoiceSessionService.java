package za.co.auraone.assistant.bridge;
import android.service.voice.VoiceInteractionSessionService;
import android.service.voice.VoiceInteractionSession;
import android.content.Intent;
import android.os.Bundle;

/** Opens Aura One when Android invokes the selected assistant. */
public final class BabyVoiceSessionService extends VoiceInteractionSessionService {
    @Override public VoiceInteractionSession onNewSession(Bundle args){
        return new VoiceInteractionSession(this){
            @Override public void onShow(Bundle args,int flags){
                super.onShow(args,flags);
                Intent i=new Intent().setClassName("za.co.auraone.assistant","za.co.auraone.assistant.MainActivity").putExtra("aura_start_voice",true);
                i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_SINGLE_TOP);
                try{startAssistantActivity(i);}catch(Exception e){ /* system may not allow launch */ }
                finish();
            }
        };
    }
}
