using System.Text.Json;
using System.Text.RegularExpressions;
namespace AuraOne;
public sealed record OfflineAction(string Tool,JsonElement Arguments,string Reply);
public static class OfflineCommands
{
 public static OfflineAction? Parse(string text,DateTimeOffset now)
 {
  string value=text.Trim();var low=value.ToLowerInvariant();
  OfflineAction Device(string action,string argument=""){DeviceActions.Validate(action,argument);return new("device_action",JsonSerializer.SerializeToElement(new{action,value=argument}),"Device action completed.");}
  var reminder=Regex.Match(value,@"^remind me in (\d{1,5}) (minute|hour|day)s? to (.+)$",RegexOptions.IgnoreCase);
  if(reminder.Success){var number=int.Parse(reminder.Groups[1].Value);var unit=reminder.Groups[2].Value.ToLowerInvariant();var minutes=number*(unit=="day"?1440L:unit=="hour"?60L:1L);if(minutes<1||minutes>525600)throw new InvalidOperationException("Choose a reminder between one minute and one year away.");var at=now.AddMinutes(minutes);return new("persist_action",JsonSerializer.SerializeToElement(new{type="reminder",text=reminder.Groups[3].Value,at=at.ToString("o")}),"Reminder saved for "+at.ToString("dddd HH:mm")+".");}
  var volume=Regex.Match(low,@"^(?:set )?(?:the )?volume (?:to )?(\d{1,3})(?: percent)?$");if(volume.Success)return Device("volume",volume.Groups[1].Value);
  var torch=Regex.Match(low,@"^(?:turn |switch )?(?:the )?(?:flashlight|torch) (on|off)$");if(torch.Success)return Device("flashlight",torch.Groups[1].Value);
  var torchFirst=Regex.Match(low,@"^(?:turn|switch) (on|off) (?:the )?(?:flashlight|torch)$");if(torchFirst.Success)return Device("flashlight",torchFirst.Groups[1].Value);
  return low switch{
   "go home"=>Device("home"),"go back"=>Device("back"),"show recent apps"=>Device("recents"),
   "open quick settings"=>Device("quick_settings"),"open notifications"=>Device("notifications"),
   "scroll up"=>Device("scroll_up"),"scroll down"=>Device("scroll_down"),
   "pause music" or "pause media"=>Device("media_pause"),"play music" or "resume media"=>Device("media_play"),
   "next track"=>Device("media_next"),"previous track"=>Device("media_previous"),
   "battery status" or "what is my battery level"=>Device("battery"),"read notification sources"=>Device("notifications_summary"),_=>null};
 }
}
