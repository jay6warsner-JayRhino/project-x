using AuraOne;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

public static class ProtocolChecks
{
    static async Task Until(Func<bool> condition){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));while(!condition())await Task.Delay(10,timeout.Token);}
    public static async Task Run(Action<bool,string> check)
    {
        check(!new Brain().UseRelay,"public relay is off by default");
        check(GeminiFailure.Code("RESOURCE_EXHAUSTED: quota exceeded")=="quota"&&GeminiFailure.Code("HTTP 429")=="quota","provider quota errors are classified without claiming unlimited access");
        check(GeminiFailure.Code("HTTP 403")=="authorization"&&GeminiFailure.Code("network interrupted")=="connection","authorization and transient network failures are distinguished");
        int blockedRequests=0;using(var blockedHttp=new HttpClient(new FakeHttp(_=>{blockedRequests++;return Task.FromResult(Ok("{}"));}))){bool blocked=false;try{await DirectGeminiClient.Ask(new Brain(),"hello",null,"unused",(_,_)=>throw new Exception("Must not execute"),CancellationToken.None,blockedHttp);}catch(InvalidOperationException){blocked=true;}check(blocked&&blockedRequests==0,"local mode prevents a real HTTP client from making a cloud request");}
        var ledger=new ToolCallLedger();
        check(ledger.Begin("one")&&!ledger.Begin("one"),"duplicate tool call is executed once");
        using(var cancelled=JsonDocument.Parse("""{"toolCallCancellation":{"ids":["one","two"]}}"""))ledger.Observe(cancelled.RootElement);
        check(!ledger.CanComplete("one")&&!ledger.Begin("two"),"cancelled calls cannot start or complete");
        using(var flat=JsonDocument.Parse("""{"type":"app.error","message":"Not enough credits"}"""))
            check(GeminiConnection.ErrorMessage(flat.RootElement)=="Not enough credits","flat relay errors are surfaced");
        using(var nested=JsonDocument.Parse("""{"error":{"message":"Invalid key"}}"""))
            check(GeminiConnection.ErrorMessage(nested.RootElement)=="Invalid key","Google errors are surfaced");
        var events=new System.Collections.Concurrent.ConcurrentQueue<JsonElement>();
        var first=new FakeSocket();var second=new FakeSocket();int opens=0;
        using(var connection=new GeminiConnection(new Brain{LocalMode=false},"test-key",e=>events.Enqueue(e),(_,key,_)=>{
            if(key!="test-key")throw new Exception("Key header missing");
            return Task.FromResult<WebSocket>(Interlocked.Increment(ref opens)==1?first:second);
        },TimeSpan.FromMilliseconds(500)))
        {
            var start=connection.Start();await Until(()=>first.Sent.Count>0);
            await connection.Send(new{realtimeInput=new{video=new{data="frame"}}});
            check(first.Sent.Count==1&&!connection.Connected,"input is blocked until setup completes");
            first.Push("""{"setupComplete":{}}""");await start;
            await connection.Send(new{realtimeInput=new{text="hello"}});
            check(connection.Connected&&first.Sent.Count==2,"setup enables client input");
            first.Push("""{"sessionResumptionUpdate":{"resumable":true,"newHandle":"resume-test"}}""");
            first.PushClose();await Until(()=>second.Sent.Count>0);
            using(var setup=JsonDocument.Parse(second.Sent.First()))
                check(setup.RootElement.GetProperty("setup").GetProperty("sessionResumption").GetProperty("handle").GetString()=="resume-test","reconnect sends the session handle");
            second.Push("""{"setupComplete":{}}""");await Until(()=>connection.Connected);
            check(opens==2,"resumed connection waits for new setup");
        }
        var cycles=Enumerable.Range(0,5).Select(_=>new FakeSocket()).ToArray();opens=0;
        using(var connection=new GeminiConnection(new Brain{LocalMode=false},"test-key",_=>{},(_,_,_)=>Task.FromResult<WebSocket>(cycles[Interlocked.Increment(ref opens)-1]),TimeSpan.FromMilliseconds(500)))
        {
            var start=connection.Start();await Until(()=>cycles[0].Sent.Count>0);cycles[0].Push("""{"setupComplete":{}}""");await start;
            cycles[0].Push("""{"sessionResumptionUpdate":{"resumable":true,"newHandle":"long-session"}}""");
            for(int n=0;n<4;n++){cycles[n].PushClose();await Until(()=>cycles[n+1].Sent.Count>0);cycles[n+1].Push("""{"setupComplete":{}}""");await Until(()=>connection.Connected);}
            check(opens==5&&connection.Connected,"successful renewal resets the consecutive retry budget for long sessions");
        }
        var quotaSocket=new FakeSocket{CloseReason="You exceeded your current quota"};opens=0;string quotaCode="";
        using(var connection=new GeminiConnection(new Brain{LocalMode=false},"test-key",e=>{if(e.TryGetProperty("type",out var t)&&t.GetString()=="app.error")quotaCode=e.GetProperty("code").GetString()!;},(_,_,_)=>{opens++;return Task.FromResult<WebSocket>(quotaSocket);},TimeSpan.FromMilliseconds(500)))
        {
            var start=connection.Start();await Until(()=>quotaSocket.Sent.Count>0);quotaSocket.Push("""{"setupComplete":{}}""");await start;
            quotaSocket.Push("""{"sessionResumptionUpdate":{"resumable":true,"newHandle":"quota-session"}}""");quotaSocket.PushClose();await Until(()=>quotaCode.Length>0);
            check(opens==1&&quotaCode=="quota","quota rejection stops without reconnect loops or provider substitution");
        }
        var silent=new FakeSocket();string failure="";
        using(var connection=new GeminiConnection(new Brain{LocalMode=false},"test-key",e=>{if(e.TryGetProperty("type",out var t)&&t.GetString()=="app.error")failure=e.GetProperty("message").GetString()!;},
            (_,_,_)=>Task.FromResult<WebSocket>(silent),TimeSpan.FromMilliseconds(100)))
        {
            try{await connection.Start();}catch(Exception){}
            await Until(()=>failure.Length>0);
            check(!connection.Connected&&failure.Contains("timeout"),"silent setup fails with a deadline");
        }
        var initial=new FakeSocket();var resumed=new FakeSocket();opens=0;failure="";
        using(var connection=new GeminiConnection(new Brain{LocalMode=false},"test-key",e=>{if(e.TryGetProperty("type",out var t)&&t.GetString()=="app.error")failure=e.GetProperty("message").GetString()!;},
            (_,_,_)=>Task.FromResult<WebSocket>(Interlocked.Increment(ref opens)==1?initial:resumed),TimeSpan.FromMilliseconds(100)))
        {
            var start=connection.Start();await Until(()=>initial.Sent.Count>0);
            initial.Push("""{"setupComplete":{}}""");await start;
            initial.Push("""{"sessionResumptionUpdate":{"resumable":true,"newHandle":"resume-test"}}""");initial.PushClose();
            // No setupComplete from the replacement connection: all three retries are bounded.
            await Until(()=>resumed.Sent.Count>0);
            await Until(()=>failure.Length>0);
            check(!connection.Connected&&opens==4,"resume setup has a deadline and three bounded retries");
        }
        var requests=new List<JsonElement>();int executions=0;string secret="dummy-provider-key";bool safeAuth=true;
        using(var http=new HttpClient(new FakeHttp(async request=>{
            safeAuth&=!request.RequestUri!.Query.Contains(secret)&&request.Headers.GetValues("x-goog-api-key").Single()==secret;
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());requests.Add(json.RootElement.Clone());
            return Ok(requests.Count==1
                ?"""{"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"calculator","args":{"expression":"2+2"}},"thoughtSignature":"preserve-me"}]}}]}"""
                :"""{"candidates":[{"content":{"role":"model","parts":[{"text":"Four."}]}}]}""");
        })))
        {
            var reply=await DirectGeminiClient.Ask(new Brain{LocalMode=false},"Calculate two plus two",null,secret,(name,args)=>{
                executions++;return Task.FromResult<object>(new{result=Calculator.Evaluate(args.GetProperty("expression").GetString()!)});
            },CancellationToken.None,http);
            check(reply=="Four."&&executions==1&&requests.Count==2,"direct fallback completes a tool round trip");
            check(requests[1].GetProperty("contents")[1].GetProperty("parts")[0].GetProperty("thoughtSignature").GetString()=="preserve-me","fallback preserves model thought signatures");
            check(safeAuth,"provider key uses the header and not the URL");
        }
        using(var http=new HttpClient(new FakeHttp(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("""{"error":{"message":"Rejected dummy-provider-key"}}""")}))))
        {
            string error="";try{await DirectGeminiClient.Ask(new Brain{LocalMode=false},"hello",null,secret,(_,_)=>throw new Exception("Must not execute"),CancellationToken.None,http);}catch(Exception e){error=e.Message;}
            check(error.Contains("[redacted]")&&!error.Contains(secret),"fallback errors redact the device key");
        }
        using(var cancellation=new CancellationTokenSource())
        using(var http=new HttpClient(new FakeHttp(_=>{cancellation.Cancel();return Task.FromResult(Ok("""{"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"persist_action","args":{"type":"note","text":"No"}}}]}}]}"""));})))
        {
            executions=0;try{await DirectGeminiClient.Ask(new Brain{LocalMode=false},"hello",null,secret,(_,_)=>{executions++;return Task.FromResult<object>(new{});},cancellation.Token,http);}catch(OperationCanceledException){}
            check(executions==0,"cancelled fallback cannot perform a tool action");
        }
    }
    static HttpResponseMessage Ok(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
}
sealed class FakeHttp(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request);
}
sealed class FakeSocket:WebSocket
{
    readonly Channel<string?> incoming=Channel.CreateUnbounded<string?>();
    volatile WebSocketState state=WebSocketState.Open;
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> Sent=new();
    public void Push(string json)=>incoming.Writer.TryWrite(json);
    public void PushClose()=>incoming.Writer.TryWrite(null);
    public override WebSocketCloseStatus? CloseStatus=>WebSocketCloseStatus.NormalClosure;
    public string CloseReason="test close";
    public override string? CloseStatusDescription=>CloseReason;
    public override WebSocketState State=>state;
    public override string? SubProtocol=>null;
    public override void Abort(){state=WebSocketState.Aborted;incoming.Writer.TryComplete();}
    public override Task CloseAsync(WebSocketCloseStatus closeStatus,string? statusDescription,CancellationToken cancellationToken){state=WebSocketState.Closed;return Task.CompletedTask;}
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus,string? statusDescription,CancellationToken cancellationToken)=>CloseAsync(closeStatus,statusDescription,cancellationToken);
    public override void Dispose()=>Abort();
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,CancellationToken cancellationToken)
    {
        var value=await incoming.Reader.ReadAsync(cancellationToken);
        if(value==null){state=WebSocketState.CloseReceived;return new(0,WebSocketMessageType.Close,true);}
        var bytes=Encoding.UTF8.GetBytes(value);bytes.AsSpan().CopyTo(buffer.AsSpan());return new(bytes.Length,WebSocketMessageType.Text,true);
    }
    public override Task SendAsync(ArraySegment<byte> buffer,WebSocketMessageType messageType,bool endOfMessage,CancellationToken cancellationToken)
    {cancellationToken.ThrowIfCancellationRequested();Sent.Enqueue(Encoding.UTF8.GetString(buffer));return Task.CompletedTask;}
}
