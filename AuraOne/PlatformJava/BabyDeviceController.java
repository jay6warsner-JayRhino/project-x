package za.co.auraone.assistant.bridge;

import android.app.Activity;
import android.app.AlarmManager;
import android.app.AlertDialog;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageManager;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraManager;
import android.media.AudioManager;
import android.view.KeyEvent;
import android.net.Uri;
import android.os.BatteryManager;
import android.os.Build;
import android.os.StatFs;
import android.provider.AlarmClock;
import android.provider.Settings;
import android.widget.Toast;
import org.json.JSONObject;
import java.util.HashMap;
import java.util.Map;

/** Guarded, bounded commands. Executes on-device only after per-action user approval. */
public final class BabyDeviceController {
    public interface Result{void done(String message);}
    private static boolean torch=false;
    private static final Map<String,String> APPS=new HashMap<>();
    static{
        APPS.put("whatsapp","com.whatsapp");
        APPS.put("gmail","com.google.android.gm"); APPS.put("drive","com.google.android.apps.docs");
        APPS.put("calendar","com.google.android.calendar"); APPS.put("github","com.github.android");
        APPS.put("chrome","com.android.chrome");
        APPS.put("youtube","com.google.android.youtube");
        APPS.put("maps","com.google.android.apps.maps");
        APPS.put("settings","com.android.settings");
        APPS.put("camera","com.sec.android.app.camera");
        APPS.put("gallery","com.sec.android.gallery3d");
        APPS.put("spotify","com.spotify.music");
        APPS.put("phone","com.samsung.android.dialer");
    }
    private BabyDeviceController(){}
    static String summary(JSONObject request){
        String action=request.optString("action","");
        String val=request.optString("value","");
        if(val.length()>75)val=val.substring(0,75)+"…";
        return action.replace('_',' ')+(val.isEmpty()?"":" : "+val);
    }
    public static AlertDialog request(Activity activity,JSONObject request,Result reply,java.util.function.BooleanSupplier allowed){
        if(activity==null||activity.isFinishing()||activity.isDestroyed()){
            reply.done("Action denied: open Aura One to review the request");return null;
        }
        String action=request.optString("action","");
        // State-only queries do not change the device, so they do not need confirmation.
        if(action.equals("battery")||action.equals("device_info")||action.equals("notifications_summary")||action.equals("shizuku_status")||action.equals("audit_log")){
            perform(activity,request,reply);return null;
        }
        if(action.length()>48||!action.matches("[a-z_]+")){
            reply.done("Invalid command");return null;
        }
        // Never execute an LLM-originated action without a distinct human tap.
        return new AlertDialog.Builder(activity).setTitle("Aura One • device control")
            .setMessage("Allow this action on your phone?\n\n"+summary(request))
            .setPositiveButton("Allow once",(d,w)->{if(allowed.getAsBoolean())perform(activity,request,reply);else reply.done("Request cancelled");})
            .setNegativeButton("Deny",(d,w)->reply.done("User declined the device action"))
            .setOnCancelListener(d->reply.done("User cancelled the device action"))
            .show();
    }
    private static void perform(Activity activity,JSONObject request,Result reply){
        String action=request.optString("action","");
        String value=request.optString("value","").trim();
        Result downstream=reply;
        Result audited=message->{
            if(!action.equals("audit_log")){
                String old=activity.getSharedPreferences("device_audit",Context.MODE_PRIVATE).getString("entries","");
                String entry=java.time.LocalDateTime.now().toString().substring(0,16)+" "+action+" • "+message.replace('\n',' ');
                String combined=(entry+"\n"+old);
                if(combined.length()>4000)combined=combined.substring(0,4000);
                activity.getSharedPreferences("device_audit",Context.MODE_PRIVATE).edit().putString("entries",combined).apply();
            }
            downstream.done(message);
        };
        reply=audited;
        try{
            switch(action){
                case "battery":reply.done(battery(activity));return;
                case "audit_log":reply.done(activity.getSharedPreferences("device_audit",Context.MODE_PRIVATE).getString("entries","No actions yet"));return;
                case "device_info":reply.done("Device "+Build.MANUFACTURER+" "+Build.MODEL+"; Android SDK "+Build.VERSION.SDK_INT);return;
                case "notifications_summary":reply.done(BabyNotificationService.summary());return;
                case "shizuku_status":reply.done(BabyShizuku.status());return;
                case "shizuku_permission":reply.done(BabyShizuku.askPermission(activity));return;
                case "shizuku_panel_expand":BabyShizuku.execute("panel_expand",reply::done);return;
                case "shizuku_panel_collapse":BabyShizuku.execute("panel_collapse",reply::done);return;
                case "shizuku_menu":BabyShizuku.execute("menu_key",reply::done);return;
                case "accessibility_settings":open(activity,new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));reply.done("Accessibility settings opened");return;
                case "notification_settings":open(activity,new Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS));reply.done("Notification access settings opened");return;
                case "assistant_settings":open(activity,new Intent(Settings.ACTION_MANAGE_DEFAULT_APPS_SETTINGS));reply.done("Default apps settings opened; choose Digital assistant app if supported");return;
                case "back":case "home":case "recents":case "notifications":case "quick_settings":
                    reply.done(BabyAccessibilityService.global(action));return;
                case "scroll_down":case "scroll_up":reply.done(BabyAccessibilityService.scroll(action.equals("scroll_down")));return;
                case "click_text":reply.done(BabyAccessibilityService.clickText(value));return;
                case "tap_percent":{
                    String[] xy=value.split(",",2);
                    if(xy.length!=2){reply.done("Use x,y percentages, e.g. 50,50");return;}
                    reply.done(BabyAccessibilityService.tapPercent(Integer.parseInt(xy[0].trim()),Integer.parseInt(xy[1].trim())));return;
                }
                case "open_app":{
                    String pkg=APPS.get(value.toLowerCase(java.util.Locale.ROOT));
                    if(pkg==null){reply.done("Only approved app names can be opened");return;}
                    Intent i=activity.getPackageManager().getLaunchIntentForPackage(pkg);
                    if(i==null){reply.done("App not installed or hidden by Android package visibility");return;}
                    open(activity,i);reply.done("Launched "+value);return;
                }
                case "open_settings":open(activity,new Intent(Settings.ACTION_SETTINGS));reply.done("Opened Settings");return;
                case "wifi_settings":open(activity,new Intent(Settings.ACTION_WIFI_SETTINGS));reply.done("Opened Wi-Fi settings");return;
                case "bluetooth_settings":open(activity,new Intent(Settings.ACTION_BLUETOOTH_SETTINGS));reply.done("Opened Bluetooth settings");return;
                case "display_settings":open(activity,new Intent(Settings.ACTION_DISPLAY_SETTINGS));reply.done("Opened display settings");return;
                case "dnd_settings":open(activity,new Intent(Settings.ACTION_NOTIFICATION_POLICY_ACCESS_SETTINGS));reply.done("Opened Do Not Disturb access settings");return;
                case "media_pause":case "media_play":case "media_next":case "media_previous":{
                    AudioManager media=(AudioManager)activity.getSystemService(Context.AUDIO_SERVICE);
                    int key=action.equals("media_pause")?KeyEvent.KEYCODE_MEDIA_PAUSE:
                            action.equals("media_play")?KeyEvent.KEYCODE_MEDIA_PLAY:
                            action.equals("media_next")?KeyEvent.KEYCODE_MEDIA_NEXT:KeyEvent.KEYCODE_MEDIA_PREVIOUS;
                    long now=android.os.SystemClock.uptimeMillis();
                    media.dispatchMediaKeyEvent(new KeyEvent(now,now,KeyEvent.ACTION_DOWN,key,0));
                    media.dispatchMediaKeyEvent(new KeyEvent(now,now,KeyEvent.ACTION_UP,key,0));
                    reply.done("Media key sent (player may ignore it)");return;
                }
                case "timer":{
                    int mins=Integer.parseInt(value);
                    if(mins<1||mins>240){reply.done("Timer must be 1–240 minutes");return;}
                    open(activity,new Intent(AlarmClock.ACTION_SET_TIMER).putExtra(AlarmClock.EXTRA_LENGTH,mins*60));
                    reply.done("Timer request opened; confirm in Clock if required");return;
                }
                case "web_search":{
                    if(value.length()<2||value.length()>140){reply.done("Search must be 2–140 characters");return;}
                    open(activity,new Intent(Intent.ACTION_VIEW,Uri.parse("https://www.google.com/search?q="+Uri.encode(value))));
                    reply.done("Opened web search");return;
                }

                case "volume":{
                    int percent=Integer.parseInt(value);
                    if(percent<0||percent>100){reply.done("Volume must be 0-100");return;}
                    AudioManager a=(AudioManager)activity.getSystemService(Context.AUDIO_SERVICE);
                    int max=a.getStreamMaxVolume(AudioManager.STREAM_MUSIC);
                    a.setStreamVolume(AudioManager.STREAM_MUSIC,Math.round(max*percent/100f),0);
                    reply.done("Media volume set to "+percent+" percent");return;
                }
                case "brightness":{
                    int level=Integer.parseInt(value);
                    if(level<10||level>255){reply.done("Brightness must be 10-255");return;}
                    if(!Settings.System.canWrite(activity)){
                        open(activity,new Intent(Settings.ACTION_MANAGE_WRITE_SETTINGS,Uri.parse("package:"+activity.getPackageName())));
                        reply.done("Grant Modify system settings, then repeat brightness command");return;
                    }
                    Settings.System.putInt(activity.getContentResolver(),Settings.System.SCREEN_BRIGHTNESS,level);
                    reply.done("Brightness set to "+level);return;
                }
                case "flashlight":{
                    if(activity.checkSelfPermission(android.Manifest.permission.CAMERA)!=PackageManager.PERMISSION_GRANTED){
                        activity.requestPermissions(new String[]{android.Manifest.permission.CAMERA},4708);
                        reply.done("Camera permission requested. Repeat flashlight command afterward");return;
                    }
                    CameraManager cm=(CameraManager)activity.getSystemService(Context.CAMERA_SERVICE);
                    String camera=null;
                    for(String id:cm.getCameraIdList()){
                        CameraCharacteristics c=cm.getCameraCharacteristics(id);
                        Boolean flash=c.get(CameraCharacteristics.FLASH_INFO_AVAILABLE);
                        Integer facing=c.get(CameraCharacteristics.LENS_FACING);
                        if(Boolean.TRUE.equals(flash) && facing!=null&&facing==CameraCharacteristics.LENS_FACING_BACK){camera=id;break;}
                    }
                    if(camera==null){reply.done("No supported flashlight found");return;}
                    boolean next=value.equalsIgnoreCase("on")?true:value.equalsIgnoreCase("off")?false:!torch;cm.setTorchMode(camera,next);torch=next;
                    reply.done("Flashlight "+(torch?"on":"off"));return;
                }
                case "dial":{
                    if(!value.matches("[0-9+*#() -]{3,24}")){reply.done("Invalid phone number");return;}
                    open(activity,new Intent(Intent.ACTION_DIAL,Uri.fromParts("tel",value,null)));
                    reply.done("Dialer opened; you must place the call yourself");return;
                }
                case "navigate":{
                    if(value.isEmpty()||value.length()>120){reply.done("Destination missing or too long");return;}
                    Intent i=new Intent(Intent.ACTION_VIEW,Uri.parse("https://www.google.com/maps/dir/?api=1&destination="+Uri.encode(value)));
                    open(activity,i);reply.done("Navigation opened for "+value);return;
                }
                case "alarm":{
                    if(!value.matches("(?:[01]?[0-9]|2[0-3]):[0-5][0-9]")){reply.done("Use alarm time HH:MM");return;}
                    String[] hm=value.split(":");
                    Intent i=new Intent(AlarmClock.ACTION_SET_ALARM)
                        .putExtra(AlarmClock.EXTRA_HOUR,Integer.parseInt(hm[0]))
                        .putExtra(AlarmClock.EXTRA_MINUTES,Integer.parseInt(hm[1]))
                        .putExtra(AlarmClock.EXTRA_MESSAGE,"Aura One alarm");
                    open(activity,i);reply.done("Opened alarm creation; confirm with Clock app if requested");return;
                }
                case "camera":open(activity,new Intent("android.media.action.STILL_IMAGE_CAMERA"));reply.done("Opened camera");return;
                default:reply.done("Unknown or disallowed action: "+action);
            }
        }catch(Exception exc){reply.done("Action unavailable: "+exc.getClass().getSimpleName());}
    }
    private static void open(Activity activity,Intent i){
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        activity.startActivity(i);
    }
    private static String battery(Activity activity){
        Intent i=activity.registerReceiver(null,new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
        if(i==null)return "Battery status unavailable";
        int level=i.getIntExtra(BatteryManager.EXTRA_LEVEL,-1);
        int scale=i.getIntExtra(BatteryManager.EXTRA_SCALE,-1);
        return scale>0?"Battery "+(level*100/scale)+" percent":"Battery level unavailable";
    }
}
