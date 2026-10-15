using System.Net.WebSockets;
using System.Text.Json;
namespace AuraOne;

// Shared by the Android audio session and connection check; contains no Android APIs.
public sealed class GeminiConnection : IDisposable
{
    readonly Brain brain;
    readonly string key;
    readonly Action<JsonElement> onEvent;
    readonly Func<Uri,string,CancellationToken,Task<WebSocket>> connect;
    readonly TimeSpan setupTimeout;
    readonly CancellationTokenSource life = new();
    readonly SemaphoreSlim sending = new(1,1);
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly ToolCallLedger tools = new();
    WebSocket? socket;
    string resumeHandle = "";
    bool resumable;
    int started, disposed;
    volatile bool connected;
    public bool Connected => connected && Volatile.Read(ref disposed)==0;
    public CancellationToken Cancellation => life.Token;
    public GeminiConnection(Brain brain,string key,Action<JsonElement> onEvent,
        Func<Uri,string,CancellationToken,Task<WebSocket>>? connector=null,TimeSpan? setupTimeout=null)
    {this.brain=brain;this.key=key.Trim();this.onEvent=onEvent;connect=connector??Open;this.setupTimeout=setupTimeout??TimeSpan.FromSeconds(30);}
    static async Task<WebSocket> Open(Uri endpoint,string key,CancellationToken cancellation)
    {
        var socket=new ClientWebSocket();socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(15);
        if(key.Length>0)socket.Options.SetRequestHeader("x-goog-api-key",key);
        else socket.Options.SetRequestHeader("Origin",new UriBuilder(endpoint){Scheme="https",Path="",Query="",Port=endpoint.IsDefaultPort?-1:endpoint.Port}.Uri.GetLeftPart(UriPartial.Authority));
        try{await socket.ConnectAsync(endpoint,cancellation);return socket;}catch{socket.Dispose();throw;}
    }
    public async Task Start()
    {
        if(Interlocked.Exchange(ref started,1)!=0)throw new InvalidOperationException("This Gemini session has already started.");
        if(!brain.UseRelay&&key.Length==0)throw new InvalidOperationException("Save your Gemini API key in Settings before starting voice. Direct Google access is the default.");
        _=Run();
        await ready.Task.WaitAsync(life.Token);
    }
    object Setup()=>brain.UseRelay
        ?new{type="app.start",settings=new{resumeHandle=resumable?resumeHandle:null,voice=brain.LiveVoice,persona=brain.ProfilePersona(),personality=brain.Persona,instructions=brain.Instructions,userName=brain.UserName,language=brain.Language,homeCity=brain.HomeCity,timezone=TimeZoneInfo.Local.Id,deep=brain.Deep,roles=brain.Roles,enabledTools=brain.EnabledTools(),memories=brain.Enabled("memory")?brain.Memories:[],notes=brain.Enabled("notes")?brain.Notes:[],reminders=brain.Enabled("reminders")?brain.Reminders.Where(x=>!x.Done).Select(x=>new{text=x.Text,at=x.At.ToString("o")}):[],customSkills=brain.Enabled("skills")?brain.Skills.Select(x=>new{name=x.Name,trigger=x.Trigger,steps=x.Steps}):[]}}
        :GeminiSetup.Build(brain,resumable?resumeHandle:null);
    async Task Run()
    {
        int retries=0;
        while(!life.IsCancellationRequested)
        {
            WebSocket? current=null;bool established=false;
            try
            {
                var endpoint=brain.UseRelay?new Uri(brain.LiveUrl):new Uri("wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent");
                if(endpoint.Scheme!="wss")throw new Rejected("Use a secure WSS relay URL.");
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                {timeout.CancelAfter(TimeSpan.FromSeconds(25));current=await connect(endpoint,brain.UseRelay?"":key,timeout.Token);}
                life.Token.ThrowIfCancellationRequested();socket=current;
                await SendRaw(current,Setup(),life.Token);
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(life.Token);
                deadline.CancelAfter(setupTimeout);
                while(!life.IsCancellationRequested)
                {
                    var e=await Read(current,deadline.Token);
                    if(ErrorMessage(e) is {} error)throw new Rejected(error);
                    if(e.TryGetProperty("setupComplete",out _)||(e.TryGetProperty("type",out var t)&&t.GetString()=="app.ready"))
                    {
                        established=true;connected=true;deadline.CancelAfter(Timeout.InfiniteTimeSpan);ready.TrySetResult();
                        Emit(new{type="app.connected"});
                    }
                    if(e.TryGetProperty("sessionResumptionUpdate",out var update))
                    {
                        resumable=update.TryGetProperty("resumable",out var can)&&can.ValueKind==JsonValueKind.True;
                        if(resumable&&update.TryGetProperty("newHandle",out var handle))resumeHandle=handle.GetString()??"";
                    }
                    tools.Observe(e);
                    onEvent(e);
                    if(e.TryGetProperty("goAway",out _))
                    {
                        if(resumable&&resumeHandle.Length>0)throw new WebSocketException("Gemini requested session renewal.");
                        Emit(new{type="app.status",message="Gemini is ending this session. Start a new voice session when it closes."});
                    }
                }
            }
            catch(OperationCanceledException) when(life.IsCancellationRequested){break;}
            catch(Exception error)
            {
                connected=false;
                var message=error is OperationCanceledException
                    ?(established?"Gemini connection timed out.":"Gemini did not complete connection setup before the timeout. Check your key, model access and internet connection.")
                    :error.Message;
                if(ready.Task.IsCompletedSuccessfully&&resumable&&resumeHandle.Length>0&&retries<3&&error is not Rejected)
                {
                    retries++;Emit(new{type="app.reconnecting",message=$"Reconnecting Gemini voice ({retries}/3)…"});
                    try{await Task.Delay(750*retries,life.Token);}catch(OperationCanceledException){break;}
                    continue;
                }
                message=SafeMessage(message);
                ready.TrySetException(new InvalidOperationException(message));
                Emit(new{type="app.error",message});
                Dispose();break;
            }
            finally
            {
                connected=false;
                if(ReferenceEquals(socket,current))socket=null;
                try{current?.Abort();current?.Dispose();}catch{}
            }
        }
    }
    static async Task<JsonElement> Read(WebSocket current,CancellationToken cancellation)
    {
        var bytes=new byte[65536];using var output=new MemoryStream();WebSocketReceiveResult message;
        do
        {
            message=await current.ReceiveAsync(bytes,cancellation);
            if(message.MessageType==WebSocketMessageType.Close)throw new WebSocketException("Gemini connection ended: "+(current.CloseStatusDescription??"Start a new voice session."));
            await output.WriteAsync(bytes.AsMemory(0,message.Count),cancellation);
            if(output.Length>2097152)throw new Rejected("Gemini event exceeded the size limit.");
        }while(!message.EndOfMessage);
        using var document=JsonDocument.Parse(output.ToArray());return document.RootElement.Clone();
    }
    public static string? ErrorMessage(JsonElement e)
    {
        if(e.TryGetProperty("error",out var error))return error.ValueKind==JsonValueKind.String?error.GetString()??"Gemini error":
            error.ValueKind==JsonValueKind.Object&&error.TryGetProperty("message",out var m)?m.GetString()??"Gemini error":"Gemini rejected this session.";
        if(e.TryGetProperty("type",out var type)&&type.GetString()=="app.error")
            return e.TryGetProperty("message",out var message)?message.GetString()??"Gemini relay rejected this session.":"Gemini relay rejected this session.";
        return null;
    }
    string SafeMessage(string message)=>key.Length==0?message:message.Replace(key,"[redacted]",StringComparison.Ordinal);
    void Emit(object value){using var json=JsonDocument.Parse(JsonSerializer.Serialize(value,Brain.Json));onEvent(json.RootElement.Clone());}
    public bool FirstToolCall(string id)=>Connected&&tools.Begin(id);
    public bool CanCompleteTool(string id)=>Connected&&tools.CanComplete(id);
    public async Task Send(object value)
    {
        if(!Connected)return;var current=socket;if(current==null)return;
        try{await SendRaw(current,value,life.Token);}
        catch(OperationCanceledException)when(life.IsCancellationRequested){}
        catch(WebSocketException){try{current.Abort();}catch{}}
    }
    async Task SendRaw(WebSocket current,object value,CancellationToken cancellation)
    {
        await sending.WaitAsync(cancellation);
        try{await current.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value,Brain.Json),WebSocketMessageType.Text,true,cancellation);}
        finally{sending.Release();}
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;connected=false;life.Cancel();ready.TrySetCanceled(life.Token);
        try{socket?.Abort();}catch{}
    }
    sealed class Rejected(string message):Exception(message);
}
public sealed class ToolCallLedger
{
    readonly HashSet<string> started=[],cancelled=[];
    readonly object gate=new();
    public bool Begin(string id){lock(gate)return id.Length>0&&!cancelled.Contains(id)&&started.Add(id);}
    public bool CanComplete(string id){lock(gate)return started.Contains(id)&&!cancelled.Contains(id);}
    public void Observe(JsonElement e)
    {
        if(!e.TryGetProperty("toolCallCancellation",out var c)||!c.TryGetProperty("ids",out var ids)||ids.ValueKind!=JsonValueKind.Array)return;
        lock(gate)foreach(var id in ids.EnumerateArray())if(id.ValueKind==JsonValueKind.String)cancelled.Add(id.GetString()!);
    }
}
