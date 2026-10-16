using System.Text.Json;
using System.Text.RegularExpressions;
namespace AuraOne;
public sealed record OfflineAction(string Tool,JsonElement Arguments,string Reply);
public static class OfflineCommands
{
 public static int? Number(string value)
 {
  if(int.TryParse(value,out var digit))return digit;
  string[] small=["zero","one","two","three","four","five","six","seven","eight","nine","ten","eleven","twelve","thirteen","fourteen","fifteen","sixteen","seventeen","eighteen","nineteen"];
  string[] tens=["","","twenty","thirty","forty","fifty","sixty","seventy","eighty","ninety"];
  var words=value.ToLowerInvariant().Replace('-',' ').Split(' ',StringSplitOptions.RemoveEmptyEntries);int total=0,group=0;bool found=false;
  foreach(var word in words){if(word=="and")continue;var unit=Array.IndexOf(small,word);var ten=Array.IndexOf(tens,word);if(unit>=0){group+=unit;found=true;}else if(ten>=2){group+=ten*10;found=true;}else if(word=="hundred"&&group is >0 and <10)group*=100;else if(word=="thousand"&&group is >0 and <1000){total+=group*1000;group=0;}else return null;if(total+group>100000)return null;}
  return found?total+group:null;
 }
 public static int? DocumentPage(string value)
 {var match=Regex.Match(value.Trim(),@"^(?:show|read) (?:document|PDF) page (.+)$",RegexOptions.IgnoreCase);return match.Success?Number(match.Groups[1].Value):null;}
 public static OfflineAction? Parse(string text,DateTimeOffset now)
 {
  string value=text.Trim();var low=value.ToLowerInvariant();
  OfflineAction Device(string action,string argument=""){DeviceActions.Validate(action,argument);return new("device_action",JsonSerializer.SerializeToElement(new{action,value=argument}),"Device action completed.");}
  var reminder=Regex.Match(value,@"^remind me in (.+?) (minute|hour|day)s? to (.+)$",RegexOptions.IgnoreCase);
  if(reminder.Success&&Number(reminder.Groups[1].Value) is {} number){var unit=reminder.Groups[2].Value.ToLowerInvariant();var minutes=number*(unit=="day"?1440L:unit=="hour"?60L:1L);if(minutes<1||minutes>525600)throw new InvalidOperationException("Choose a reminder between one minute and one year away.");var at=now.AddMinutes(minutes);return new("persist_action",JsonSerializer.SerializeToElement(new{type="reminder",text=reminder.Groups[3].Value,at=at.ToString("o")}),"Reminder saved for "+at.ToString("dddd HH:mm")+".");}
  var volume=Regex.Match(low,@"^(?:set )?(?:the )?volume (?:to )?(.+?)(?: percent)?$");if(volume.Success&&Number(volume.Groups[1].Value) is {} level)return Device("volume",level.ToString());
  var timer=Regex.Match(low,@"^(?:set |start )?(?:a |the )?timer (?:for )?(.+?) minutes?$");if(timer.Success&&Number(timer.Groups[1].Value) is {} duration)return Device("timer",duration.ToString());
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
