using System.Net.Http.Json;
using System.Text.Json;
namespace AuraOne;

// Android recognition/TTS fallback: model requests and tool calls go directly to Google.
public static class DirectGeminiClient
{
    static readonly HttpClient DefaultHttp=new(){Timeout=TimeSpan.FromSeconds(90)};
    public static async Task<string> Ask(Brain brain,string text,string? frame,string key,
        Func<string,JsonElement,Task<object>> execute,CancellationToken cancellation,HttpClient? client=null)
    {
        key=key.Trim();
        if(key.Length==0)throw new InvalidOperationException("Save your Gemini API key in Settings. Device voice uses Google directly unless you select the optional relay.");
        var http=client??DefaultHttp;
        using var setupDocument=JsonDocument.Parse(JsonSerializer.Serialize(GeminiSetup.Build(brain,null),Brain.Json));
        var setup=setupDocument.RootElement.GetProperty("setup");
        var instruction=setup.GetProperty("systemInstruction").Clone();
        // Function calling stays separate from built-in search in this fallback.
        // Google Search grounding remains available in the primary Gemini Live session.
        var tools=setup.GetProperty("tools").EnumerateArray().Where(t=>t.TryGetProperty("functionDeclarations",out _)).Select(t=>(object)new{functionDeclarations=t.GetProperty("functionDeclarations").EnumerateArray().Where(d=>d.GetProperty("name").GetString()!="document_page").Select(d=>d.EnumerateObject().Where(p=>p.Name!="behavior").ToDictionary(p=>p.Name,p=>p.Value.Clone())).ToArray()}).ToArray();
        var contents=new List<object>();
        foreach(var turn in brain.History.Take(7).Reverse())
        {
            if(turn.You.Length>0)contents.Add(new{role="user",parts=new[]{new{text=turn.You}}});
            if(turn.Reply.Length>0)contents.Add(new{role="model",parts=new[]{new{text=turn.Reply}}});
        }
        var input=new List<object>{new{text}};
        if(frame!=null)input.Add(new{inlineData=new{mimeType="image/jpeg",data=frame}});
        contents.Add(new{role="user",parts=input});
        for(var round=0;round<8;round++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var request=new HttpRequestMessage(HttpMethod.Post,"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent");
            request.Headers.Add("x-goog-api-key",key);
            request.Content=JsonContent.Create(new{contents,systemInstruction=instruction,tools,generationConfig=new{thinkingConfig=new{thinkingLevel=brain.Deep?"high":"low"}}},options:Brain.Json);
            using var response=await http.SendAsync(request,cancellation);
            var raw=await response.Content.ReadAsStringAsync(cancellation);
            JsonDocument document;
            try{document=JsonDocument.Parse(raw);}catch{throw new InvalidOperationException($"Google returned HTTP {(int)response.StatusCode}. Check your connection and Gemini access.");}
            using(document)
            {
                var root=document.RootElement;
                if(!response.IsSuccessStatusCode)
                {
                    var error=GeminiConnection.ErrorMessage(root)??$"Google returned HTTP {(int)response.StatusCode}.";
                    throw new InvalidOperationException(error.Replace(key,"[redacted]",StringComparison.Ordinal));
                }
                if(!root.TryGetProperty("candidates",out var candidates)||candidates.GetArrayLength()==0||!candidates[0].TryGetProperty("content",out var content))
                    throw new InvalidOperationException("Gemini did not return an answer. The request may have been blocked.");
                var parts=content.GetProperty("parts");
                var calls=parts.EnumerateArray().Where(x=>x.TryGetProperty("functionCall",out _)).Select(x=>x.GetProperty("functionCall").Clone()).ToArray();
                if(calls.Length==0)
                {
                    var reply=string.Join("",parts.EnumerateArray().Where(x=>x.TryGetProperty("text",out _)&&!(x.TryGetProperty("thought",out var thought)&&thought.ValueKind==JsonValueKind.True)).Select(x=>x.GetProperty("text").GetString()));
                    if(string.IsNullOrWhiteSpace(reply))throw new InvalidOperationException("Gemini returned an empty spoken answer.");
                    return reply;
                }
                // Preserve the full model content, including thought signatures, for the next turn.
                contents.Add(content.Clone());
                var results=new List<object>();
                foreach(var call in calls)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var name=call.GetProperty("name").GetString()??"";
                    var arguments=call.TryGetProperty("args",out var a)?a:JsonSerializer.SerializeToElement(new{});
                    object result;
                    try{result=await execute(name,arguments);}catch(Exception e){result=new{error=e.Message};}
                    cancellation.ThrowIfCancellationRequested();
                    var function=new Dictionary<string,object>{{"name",name},{"response",result}};
                    if(call.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.String)function["id"]=id.GetString()!;
                    results.Add(new{functionResponse=function});
                }
                contents.Add(new{role="user",parts=results});
            }
        }
        throw new InvalidOperationException("Gemini reached the tool-call limit. Ask for a smaller task.");
    }
}
