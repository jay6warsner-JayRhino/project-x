using System.Text.RegularExpressions;
namespace AuraOne;
public static class DeviceActions
{
    public static readonly string[] All=["battery","device_info","notifications_summary","shizuku_status","audit_log","open_app","volume","brightness","flashlight","media_pause","media_play","media_next","media_previous","timer","web_search","wifi_settings","bluetooth_settings","display_settings","dnd_settings","back","home","recents","notifications","quick_settings","scroll_up","scroll_down","click_text","tap_percent","dial","navigate","alarm","camera","open_settings","accessibility_settings","notification_settings","assistant_settings","shizuku_permission","shizuku_panel_expand","shizuku_panel_collapse","shizuku_menu"];
    public static bool ReadOnly(string action)=>action is "battery" or "device_info" or "notifications_summary" or "shizuku_status" or "audit_log";
    public static void Validate(string action,string value)
    {
        if(!All.Contains(action))throw new InvalidOperationException("Unsupported device action.");
        if(value.Length>140)throw new InvalidOperationException("Device action parameter is too long.");
        bool Number(int min,int max)=>int.TryParse(value,out var n)&&n>=min&&n<=max;
        var valid=action switch{
            "volume"=>Number(0,100),"brightness"=>Number(10,255),"timer"=>Number(1,240),
            "tap_percent"=>Regex.IsMatch(value,@"^\s*(?:100|[0-9]{1,2})\s*,\s*(?:100|[0-9]{1,2})\s*$"),
            "alarm"=>Regex.IsMatch(value,@"^(?:[01]?[0-9]|2[0-3]):[0-5][0-9]$"),
            "dial"=>Regex.IsMatch(value,@"^[0-9+*#() -]{3,24}$"),
            "click_text"=>value.Length is >=1 and <=70,"navigate"=>value.Length is >=1 and <=120,
            "web_search"=>value.Length is >=2 and <=140,
            "flashlight"=>value is "" or "on" or "off" or "toggle",_=>true};
        if(!valid)throw new InvalidOperationException("Invalid parameter for "+action.Replace('_',' ')+".");
    }
}
