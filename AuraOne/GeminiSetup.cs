using System.Text.Json;
namespace AuraOne;
public static class GeminiSetup
{
    static object Schema(Dictionary<string,object> fields,params string[] required)=>new{type="OBJECT",properties=fields,required};
    static object String(params string[] values)=>values.Length>0?new{type="STRING",@enum=values}:(object)new{type="STRING"};
    public static object Build(Brain b,string? handle)
    {
        var declarations=new List<object>();
        void Tool(string name,string description,Dictionary<string,object> fields,params string[] required)=>declarations.Add(new{name,description,parameters=Schema(fields,required)});
        var types=new List<string>();if(b.Enabled("notes"))types.AddRange(["note","clear_notes"]);if(b.Enabled("memory"))types.AddRange(["remember","forget"]);if(b.Enabled("reminders"))types.Add("reminder");if(b.Enabled("skills"))types.Add("skill");
        if(types.Count>0)Tool("persist_action","Save or remove a fact, note, reminder or learned routine on the user's phone. Reminders use an absolute ISO8601 time with timezone offset.",new(){["type"]=String(types.ToArray()),["text"]=String(),["at"]=String(),["fact"]=String(),["name"]=String(),["trigger"]=String(),["steps"]=String()},"type");
        if(b.Enabled("phone"))Tool("phone_action","Open an installed app or prepare the phone dialler. The user confirms the call.",new(){["type"]=String("open_app","dial"),["app"]=String(),["number"]=String()},"type");
        if(b.Enabled("weather"))Tool("get_weather","Get current weather and a three-day forecast.",new(){["location"]=String()},"location");
        if(b.Enabled("calculator"))Tool("calculator","Evaluate arithmetic using + - * / % and parentheses.",new(){["expression"]=String()},"expression");
        if(b.Enabled("time"))Tool("get_time","Get the current date, time and timezone on the phone.",new());
        if(b.Enabled("wikipedia"))Tool("encyclopedia_search","Look up and read an encyclopedia topic.",new(){["query"]=String()},"query");
        var context=new{persona=b.ProfilePersona(),instructions=b.Instructions,user=b.UserName,homeCity=b.HomeCity,language=b.Language,now=DateTimeOffset.Now.ToString("o"),timezone=TimeZoneInfo.Local.Id,roles=b.Roles,memories=b.Enabled("memory")?b.Memories:[],notes=b.Enabled("notes")?b.Notes:[],reminders=b.Enabled("reminders")?b.Reminders.Where(x=>!x.Done).Select(x=>new{x.Text,x.At}):[],routines=b.Enabled("skills")?b.Skills:[]};
        var instruction="You are a voice-only personal assistant. Speak and listen naturally in the configured language with a refined British manner when speaking English. Keep answers conversational. You can see only the camera or screen frames the user actively shares. Use tools for persistence and phone actions; report success only when the tool succeeds. Use the user's timezone for reminders. Ask before clearing all notes. Follow learned routines when their trigger matches. Do not claim access to Gmail, Calendar, Drive or GitHub accounts; app shortcuts only open them. The user's saved configuration follows: "+JsonSerializer.Serialize(context,Brain.Json);
        var tools=new List<object>();if(b.Enabled("web"))tools.Add(new{googleSearch=new{}});if(declarations.Count>0)tools.Add(new{functionDeclarations=declarations});
        return new{setup=new{model=b.Deep?"models/gemini-3.8-live-extended-thinking":"models/gemini-3.8-live",generationConfig=new{responseModalities=new[]{"AUDIO"},speechConfig=new{voiceConfig=new{prebuiltVoiceConfig=new{voiceName=b.LiveVoice}}}},systemInstruction=new{parts=new[]{new{text=instruction}}},tools,realtimeInputConfig=new{automaticActivityDetection=new{disabled=false},activityHandling="START_OF_ACTIVITY_INTERRUPTS"},inputAudioTranscription=new{},outputAudioTranscription=new{},sessionResumption=handle==null?new Dictionary<string,string>():new(){["handle"]=handle},contextWindowCompression=new{slidingWindow=new{}}}};
    }
}
