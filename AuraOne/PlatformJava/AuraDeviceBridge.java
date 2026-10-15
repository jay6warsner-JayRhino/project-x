package za.co.auraone.assistant.bridge;
import android.app.Activity;
import android.app.AlertDialog;
import android.os.Handler;
import android.os.Looper;
import org.json.JSONObject;
import java.util.concurrent.ConcurrentHashMap;

/** Native Java adapter used by the .NET UI; no WebView or command-string execution. */
public final class AuraDeviceBridge {
    private static final Handler MAIN=new Handler(Looper.getMainLooper());
    private static final ConcurrentHashMap<String,Request> PENDING=new ConcurrentHashMap<>();
    private static final class Request { volatile String output; volatile AlertDialog dialog; }
    public static void begin(Activity activity,String json,String id){
        Request state=new Request();PENDING.put(id,state);
        if(PENDING.size()>8){state.output="Too many pending device requests";return;}
        MAIN.post(()->{
            if(PENDING.get(id)!=state)return;
            try {
                state.dialog=BabyDeviceController.request(activity,new JSONObject(json),r->state.output=r,()->PENDING.get(id)==state);
            } catch(Exception e){state.output="Device request unavailable";}
        });
    }
    public static String poll(String id){
        Request state=PENDING.get(id);
        if(state==null)return "Request cancelled";
        if(state.output==null)return null;
        PENDING.remove(id,state);
        return state.output;
    }
    public static void cancel(String id){
        Request state=PENDING.remove(id);
        if(state!=null)MAIN.post(()->{if(state.dialog!=null)state.dialog.dismiss();});
    }
    public static String status(){
        try {
            return new JSONObject().put("accessibility",BabyAccessibilityService.active!=null)
                .put("notifications",BabyNotificationService.active!=null)
                .put("shizuku",BabyShizuku.status())
                .put("model",android.os.Build.MANUFACTURER+" "+android.os.Build.MODEL)
                .put("androidApi",android.os.Build.VERSION.SDK_INT).toString();
        } catch(Exception e){return "{}";}
    }
}
