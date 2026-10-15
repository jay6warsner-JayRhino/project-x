using AuraOne;
using AuraOne.Integrations;
using System.Net;
using System.Text;
using System.Text.Json;
public static class AccountChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        DeviceActions.Validate("volume","50");DeviceActions.Validate("tap_percent","50,50");
        bool rejected=false;try{DeviceActions.Validate("shell","rm -rf /");}catch(InvalidOperationException){rejected=true;}
        check(rejected,"native device actions reject arbitrary shell requests");
        rejected=false;try{DeviceActions.Validate("volume","101");}catch(InvalidOperationException){rejected=true;}
        check(rejected,"native device volume range enforced");
        rejected=false;try{DeviceActions.Validate("tap_percent","50,200");}catch(InvalidOperationException){rejected=true;}
        check(rejected,"native gesture coordinate range enforced");
        check(DeviceActions.ReadOnly("battery")&&!DeviceActions.ReadOnly("click_text"),"native state queries separated from device changes");
        var credential=new AccountCredential{AccessToken="test-access-token",ExpiresAt=DateTimeOffset.Now.AddHours(1)};
        check(credential.UsableToken(DateTimeOffset.Now)!=null,"current account token accepted");
        credential.ExpiresAt=DateTimeOffset.Now.AddSeconds(10);
        check(credential.UsableToken(DateTimeOffset.Now)==null,"expired or near-expiry account token rejected");
        credential.ExpiresAt=null;credential.AccessToken="bad\r\nheader";
        check(credential.UsableToken(DateTimeOffset.Now)==null,"account token header injection rejected");
        var requests=new List<Uri>();bool headerSafe=true;
        using(var http=new HttpClient(new AccountHttp(request=>{
            requests.Add(request.RequestUri!);headerSafe&=request.Headers.Authorization?.Parameter=="test-access-token"&&!request.RequestUri!.Query.Contains("test-access-token");
            return Response(HttpStatusCode.OK,"{}");
        })))
        {
            var api=new ReadOnlyAccountTools(http,new TokenProvider("test-access-token"));
            using var gmail=await api.GmailSearchAsync("hello&maxResults=999",100);
            using var message=await api.GmailMessageAsync("abcdef123");
            using var calendar=await api.CalendarUpcomingAsync(DateTimeOffset.Now);
            using var drive=await api.DriveListAsync("Bob's report");
            using var repositories=await api.GitHubRepositoriesAsync();
            using var issues=await api.GitHubIssuesAsync("owner","repository");
            check(requests.Count==6&&requests.All(u=>u.Scheme=="https"&&u.Host is "www.googleapis.com" or "api.github.com"),"all six account operations use fixed HTTPS provider endpoints");
            check(headerSafe,"account authorization stays in the HTTP header");
            check(requests[0].Query.Contains("%26maxResults%3D999")&&requests[0].Query.EndsWith("maxResults=30"),"Gmail query cannot inject API parameters and results are bounded");
            bool invalid=false;try{await api.GitHubIssuesAsync("owner","..");}catch(ArgumentException){invalid=true;}
            check(invalid&&requests.Count==6,"repository path traversal rejected before HTTP");
        }
        foreach(var code in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests})
        {
            using var http=new HttpClient(new AccountHttp(_=>Response(code,"secret provider body")));
            var api=new ReadOnlyAccountTools(http,new TokenProvider("test-access-token"));
            bool safe=false;try{await api.GitHubRepositoriesAsync();}catch(AccountApiException e){safe=e.StatusCode==code&&!e.Message.Contains("secret")&&!e.Message.Contains("test-access-token");}
            check(safe,"account HTTP "+(int)code+" returns a safe authorization or quota error");
        }
        using(var http=new HttpClient(new AccountHttp(_=>Response(HttpStatusCode.OK,"not-json-secret"))))
        {
            var api=new ReadOnlyAccountTools(http,new TokenProvider("test-access-token"));bool safe=false;
            try{await api.GitHubRepositoriesAsync();}catch(InvalidOperationException e){safe=e.Message=="Provider returned invalid account data.";}
            check(safe,"malformed account JSON has a safe error");
        }
        using(var http=new HttpClient(new AccountHttp(_=>Response(HttpStatusCode.OK,new string('x',1024*1024+1)))))
        {
            var api=new ReadOnlyAccountTools(http,new TokenProvider("test-access-token"));bool bounded=false;
            try{await api.GitHubRepositoriesAsync();}catch(InvalidOperationException e){bounded=e.Message.Contains("size");}
            check(bounded,"oversized account responses rejected");
        }
        int attempted=0;
        using(var http=new HttpClient(new AccountHttp(_=>{attempted++;return Response(HttpStatusCode.OK,"{}");})))
        {
            var api=new ReadOnlyAccountTools(http,new TokenProvider(null));try{await api.GitHubRepositoriesAsync();}catch(InvalidOperationException){}
            check(attempted==0,"unauthorized account tools do not make a provider request");
        }
        var brain=new Brain();brain.Tools["gmail"]=brain.Tools["calendar"]=brain.Tools["drive"]=brain.Tools["github"]=true;
        using var setup=JsonDocument.Parse(JsonSerializer.Serialize(GeminiSetup.Build(brain,null),Brain.Json));
        var functions=setup.RootElement.GetProperty("setup").GetProperty("tools").EnumerateArray().Where(x=>x.TryGetProperty("functionDeclarations",out _)).SelectMany(x=>x.GetProperty("functionDeclarations").EnumerateArray()).ToArray();
        check(functions.Any(x=>x.GetProperty("name").GetString()=="device_action")&&functions.Count(x=>x.GetProperty("name").GetString() is "gmail_search" or "gmail_message" or "calendar_upcoming" or "drive_search" or "github_repositories" or "github_issues")==6,"device and account operations registered in Gemini");
        check(functions.All(x=>x.GetProperty("behavior").GetString()=="NON_BLOCKING"),"Gemini tools explicitly use asynchronous execution");
        using var deep=JsonDocument.Parse(JsonSerializer.Serialize(GeminiSetup.Build(new Brain{Deep=true},null),Brain.Json));
        check(deep.RootElement.GetProperty("setup").GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString()=="high","extended thinking has a supported explicit thinking level");
    }
    static HttpResponseMessage Response(HttpStatusCode code,string content)=>new(code){Content=new StringContent(content,Encoding.UTF8,"application/json")};
}
sealed class AccountHttp(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();return Task.FromResult(send(request));}}
sealed class TokenProvider(string? token):IAccountAccessTokenProvider
{public Task<string?> GetTokenAsync(AccountProvider provider,CancellationToken cancellationToken)=>Task.FromResult(token);}
