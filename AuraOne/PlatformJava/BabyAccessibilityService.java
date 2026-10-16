package za.co.auraone.assistant.bridge;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.GestureDescription;
import android.graphics.Path;
import android.graphics.Rect;
import android.os.Handler;
import android.os.Looper;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import java.util.ArrayDeque;

/** User-enabled, restricted UI interactions. Does not collect passwords or screen text. */
public final class BabyAccessibilityService extends AccessibilityService {
    static volatile BabyAccessibilityService active;
    private final Handler ui = new Handler(Looper.getMainLooper());
    @Override protected void onServiceConnected() { super.onServiceConnected(); active=this; }
    @Override public void onAccessibilityEvent(AccessibilityEvent event) { /* no passive monitoring */ }
    @Override public void onInterrupt() { }
    @Override public void onDestroy(){ if(active==this) active=null; super.onDestroy(); }

    static String global(String name){
        BabyAccessibilityService s=active;
        if(s==null)return "Enable Aura One Accessibility in Settings first";
        int action;
        switch(name){
            case "back": action=GLOBAL_ACTION_BACK;break;
            case "home": action=GLOBAL_ACTION_HOME;break;
            case "recents": action=GLOBAL_ACTION_RECENTS;break;
            case "notifications": action=GLOBAL_ACTION_NOTIFICATIONS;break;
            case "quick_settings": action=GLOBAL_ACTION_QUICK_SETTINGS;break;
            default:return "Unsupported navigation";
        }
        return s.performGlobalAction(action)?"Requested "+name:"Android refused "+name;
    }
    static String scroll(boolean down){
        BabyAccessibilityService s=active;
        if(s==null)return "Enable Accessibility first";
        AccessibilityNodeInfo root=s.getRootInActiveWindow();
        if(root==null)return "Screen unavailable";
        ArrayDeque<AccessibilityNodeInfo> q=new ArrayDeque<>();q.add(root);
        int seen=0;
        while(!q.isEmpty() && seen++<250){
            AccessibilityNodeInfo n=q.removeFirst();
            if(n.isScrollable()){
                boolean ok=n.performAction(down?AccessibilityNodeInfo.ACTION_SCROLL_FORWARD:AccessibilityNodeInfo.ACTION_SCROLL_BACKWARD);
                return ok?"Scroll requested":"Scroll not accepted";
            }
            for(int i=0;i<n.getChildCount();i++){
                AccessibilityNodeInfo child=n.getChild(i);
                if(child!=null)q.add(child);
            }
        }
        return "No scrollable area accessible";
    }
    static String clickText(String raw){
        BabyAccessibilityService s=active;
        if(s==null)return "Enable Accessibility first";
        String wanted=raw.trim();
        if(wanted.length()<1||wanted.length()>70)return "Button text must be 1-70 characters";
        AccessibilityNodeInfo root=s.getRootInActiveWindow();
        if(root==null)return "Screen unavailable";
        ArrayDeque<AccessibilityNodeInfo> q=new ArrayDeque<>();q.add(root);
        int seen=0;
        while(!q.isEmpty()&&seen++<250){
            AccessibilityNodeInfo n=q.removeFirst();
            if(!n.isPassword() && n.isVisibleToUser()){
                CharSequence text=n.getText();
                CharSequence desc=n.getContentDescription();
                if((text!=null&&wanted.equalsIgnoreCase(text.toString().trim()))||
                   (desc!=null&&wanted.equalsIgnoreCase(desc.toString().trim()))){
                    AccessibilityNodeInfo target=n;
                    for(int i=0;i<5&&target!=null;i++){
                        if(target.isClickable()&&target.isEnabled())
                            return target.performAction(AccessibilityNodeInfo.ACTION_CLICK)?"Tapped labeled control":"Tap rejected";
                        target=target.getParent();
                    }
                    return "Matching label was not clickable";
                }
            }
            for(int i=0;i<n.getChildCount();i++){
                AccessibilityNodeInfo child=n.getChild(i);
                if(child!=null)q.add(child);
            }
        }
        return "Exact visible button text not found";
    }
    static String tapPercent(int x,int y){
        BabyAccessibilityService s=active;
        if(s==null)return "Enable Accessibility first";
        if(x<0||x>100||y<0||y>100)return "Coordinates must be 0-100";
        android.util.DisplayMetrics dm=s.getResources().getDisplayMetrics();
        Path path=new Path();path.moveTo(dm.widthPixels*x/100f,dm.heightPixels*y/100f);
        GestureDescription gesture=new GestureDescription.Builder().addStroke(new GestureDescription.StrokeDescription(path,0,90)).build();
        return s.dispatchGesture(gesture,null,null)?"Tap gesture requested":"Android rejected tap";
    }
}
