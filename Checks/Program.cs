using AuraOne;
using System.Net.WebSockets;
using System.Text.Json;
using System.Net.Http.Json;
if(args.Contains("--github-live")){await LiveAccountProbe.Run();return;}
int passed=0;
void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
var brain=Brain.Import("""{"mybaby.brain.v1":{"persona":"British helper","memories":["Coffee"],"notes":["Call Alex"],"skills":[{"name":"Morning","trigger":"good morning","steps":"Read weather"}],"reminders":[{"text":"Plan","at":"2030-10-16T12:00:00+02:00"}]},"mybaby.settings.v1":{"tools":{"web":false},"language":"en-ZA","voiceRate":1.2},"mybaby.history.v1":[{"you":"old","baby":"old reply","at":"2026-10-12T08:00:00Z"},{"you":"new","baby":"new reply","at":"2026-10-14T08:00:00Z"}]}""");
Check(brain.Memories.SequenceEqual(["Coffee"])&&brain.Notes.Count==1&&brain.Skills.Count==1&&brain.Reminders.Count==1,"original Aura brain import");
Check(!brain.Enabled("web")&&brain.Language=="en-ZA","original Aura settings import");
Check(brain.History[0].You=="new","history chronological order");
var voice=Brain.Import("""{"jarvis-settings-v1":"{\"memories\":[\"Tea\"],\"voice\":\"vesper\",\"plugins\":[\"weather\"],\"skills\":[\"tutor\"]}"}""");
Check(voice.Memories[0]=="Tea"&&voice.Roles.Contains("tutor")&&voice.LiveVoice=="Charon"&&!voice.Enabled("calculator"),"Voice Connect migration and Gemini voice mapping");
var restored=Brain.Import(Brain.Export(brain));Check(restored.Skills[0].Steps=="Read weather"&&restored.History.Count==2,"native backup round trip");
Check(Brain.AfterWake("Hey Jarvis, tell me the time","Jarvis")=="tell me the time"&&Brain.AfterWake("unrelated speech","Jarvis")==null,"wake phrase parsing");
using(var json=JsonDocument.Parse("""{"type":"note","text":"Saved once"}""")){brain.Tools["notes"]=false;Check(!brain.Apply(json.RootElement,out _)&&brain.Notes.Count==1,"disabled persistence tool gate");brain.Tools["notes"]=true;Check(brain.Apply(json.RootElement,out _)&&brain.Notes.Count==2,"enabled note action");}
using(var json=JsonDocument.Parse("""{"type":"reminder","text":"Too late","at":"2020-01-01T00:00:00Z"}"""))Check(!brain.Apply(json.RootElement,out _),"reject past reminder");
Check(Calculator.Evaluate("2+3*4")=="14"&&Calculator.Evaluate("(2+3)*4")=="20","arithmetic precedence");
for(var i=0;i<510;i++)brain.Record(new Turn{You="turn "+i,Reply="reply"});Check(brain.History.Count==500&&brain.History[0].You=="turn 509","bounded history");Check(brain.Search("TURN 509").Count()==1,"history search");
using(var setup=JsonDocument.Parse(JsonSerializer.Serialize(GeminiSetup.Build(new Brain(),null),Brain.Json))){var value=setup.RootElement.GetProperty("setup");Check(value.GetProperty("model").GetString()=="models/gemini-3.8-live","default Gemini Live 3.8 model");Check(value.GetProperty("generationConfig").GetProperty("responseModalities")[0].GetString()=="AUDIO","audio-only Gemini replies");}
var disabled=new Brain();foreach(var key in disabled.Tools.Keys.ToArray())disabled.Tools[key]=false;using(var setup=JsonDocument.Parse(JsonSerializer.Serialize(GeminiSetup.Build(disabled,null),Brain.Json)))Check(setup.RootElement.GetProperty("setup").GetProperty("tools").EnumerateArray().Where(t=>t.TryGetProperty("functionDeclarations",out _)).SelectMany(t=>t.GetProperty("functionDeclarations").EnumerateArray()).All(d=>d.GetProperty("name").GetString()=="session_control"),"disabled task tools retain only voice session controls");
Check(!Brain.Export(new Brain()).Contains("apiKey",StringComparison.OrdinalIgnoreCase),"backup excludes provider key");
using(var setup=JsonDocument.Parse(JsonSerializer.Serialize(GeminiSetup.Build(new Brain(),null),Brain.Json)))Check(setup.RootElement.GetProperty("setup").GetProperty("tools").EnumerateArray().Any(x=>x.TryGetProperty("googleSearch",out _)),"Google Search grounding enabled");
Check(Brain.Import(Brain.Export(new Brain{SpeakReminders=false})).SpeakReminders==false,"spoken reminder preference backup");
await ProtocolChecks.Run(Check);
await AccountChecks.Run(Check);
await ModernChecks.Run(Check);Console.WriteLine($"{passed} data and protocol checks passed.");
if(!args.Contains("--network"))return;
using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(40)};
try{using var response=await http.PostAsJsonAsync("https://aura-your-pal.lovable.app/api/assistant",new{messages=new[]{new{role="user",content="Reply with exactly: Gemini connection ready."}},profile=new{persona="You are a voice-only personal assistant."},enabledTools=Array.Empty<string>()});Console.WriteLine("Assistant HTTP "+(int)response.StatusCode+" "+await response.Content.ReadAsStringAsync());}catch(Exception e){Console.WriteLine("Assistant ERROR "+e.Message);}
using var socket=new ClientWebSocket();socket.Options.SetRequestHeader("Origin","https://aura-your-pal.lovable.app");using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(65));
try{
await socket.ConnectAsync(new Uri("wss://aura-your-pal.lovable.app/api/gemini-live"),timeout.Token);Console.WriteLine("Gemini relay connected");
await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new{type="app.start",settings=new{voice="Charon",persona="You are Jarvis, a friendly British voice assistant.",enabledTools=new[]{"memory"}}}),WebSocketMessageType.Text,true,timeout.Token);
var buffer=new byte[65536];bool sent=false;using var pcm=new MemoryStream();
while(socket.State==WebSocketState.Open){using var incoming=new MemoryStream();WebSocketReceiveResult msg;do{msg=await socket.ReceiveAsync(buffer,timeout.Token);if(msg.MessageType==WebSocketMessageType.Close){Console.WriteLine("Gemini CLOSE "+socket.CloseStatus+" "+socket.CloseStatusDescription);return;}await incoming.WriteAsync(buffer.AsMemory(0,msg.Count),timeout.Token);}while(!msg.EndOfMessage);
using var document=JsonDocument.Parse(incoming.ToArray());var e=document.RootElement;
if(e.TryGetProperty("type",out var type)){Console.WriteLine("Event "+type.GetString()+(e.TryGetProperty("message",out var message)?": "+message.GetString():""));if(type.GetString()=="app.error")return;}
if(e.TryGetProperty("setupComplete",out _)||e.TryGetProperty("type",out var typ)&&typ.GetString()=="app.ready"){
Console.WriteLine("Gemini setup complete");if(!sent){sent=true;await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new{clientContent=new{turns=new[]{new{role="user",parts=new[]{new{text="Say exactly: Hello. Gemini voice is ready."}}}},turnComplete=true}}),WebSocketMessageType.Text,true,timeout.Token);}}
if(e.TryGetProperty("serverContent",out var content)){
if(content.TryGetProperty("outputTranscription",out var transcript))Console.WriteLine("Spoken transcription "+transcript.GetProperty("text").GetString());
if(content.TryGetProperty("modelTurn",out var turn))foreach(var part in turn.GetProperty("parts").EnumerateArray())if(part.TryGetProperty("inlineData",out var data))await pcm.WriteAsync(Convert.FromBase64String(data.GetProperty("data").GetString()!));
if(content.TryGetProperty("turnComplete",out var done)&&done.ValueKind==JsonValueKind.True){Console.WriteLine("Gemini audio bytes "+pcm.Length);await File.WriteAllBytesAsync("work/gemini-test.pcm",pcm.ToArray());Console.WriteLine(pcm.Length>0?"PASS Gemini native audio output":"FAIL no Gemini audio");await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"Test complete",timeout.Token);break;}}
}
}catch(Exception e){Console.WriteLine("Gemini ERROR "+e.GetType().Name+": "+e.Message);}
