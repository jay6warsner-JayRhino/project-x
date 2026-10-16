package za.co.auraone.assistant.bridge;
import android.service.notification.NotificationListenerService;
import android.service.notification.StatusBarNotification;
import java.util.LinkedHashSet;

/** Summarizes notification sources only; never extracts notification content or messages. */
public final class BabyNotificationService extends NotificationListenerService {
    static volatile BabyNotificationService active;
    @Override public void onListenerConnected(){active=this;super.onListenerConnected();}
    @Override public void onListenerDisconnected(){if(active==this)active=null;super.onListenerDisconnected();}
    @Override public void onDestroy(){if(active==this)active=null;super.onDestroy();}
    static String summary(){
        BabyNotificationService s=active;
        if(s==null)return "Enable Aura One Notification Access first";
        try{
            StatusBarNotification[] entries=s.getActiveNotifications();
            if(entries==null)return "No accessible notifications";
            LinkedHashSet<String> sources=new LinkedHashSet<>();
            for(StatusBarNotification n:entries){
                if(sources.size()==6)break;
                String pkg=n.getPackageName();
                if(!pkg.equals(s.getPackageName()))sources.add(pkg);
            }
            return entries.length+" notifications; source apps: "+String.join(", ",sources)+". Contents not read.";
        }catch(Exception e){return "Notification access unavailable";}
    }
}
