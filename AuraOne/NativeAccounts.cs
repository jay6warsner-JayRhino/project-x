using Android.Content;
using System.Text.Json;
using AuraOne.Integrations;
namespace AuraOne;
public sealed class NativeAccounts(Context context):IAccountAccessTokenProvider
{
    static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25)};
    static readonly HttpClient AuthorizationHttp=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(90)};
    static readonly object StorageGate=new();
    static readonly Dictionary<AccountProvider,int> Revisions=new(){{AccountProvider.Google,0},{AccountProvider.GitHub,0}};
    static readonly Dictionary<AccountProvider,SemaphoreSlim> RefreshGates=new(){{AccountProvider.Google,new(1,1)},{AccountProvider.GitHub,new(1,1)}};
    static string Slot(AccountProvider provider)=>provider==AccountProvider.Google?"account-google":"account-github";
    public void Save(AccountProvider provider,AccountCredential credential)
    {lock(StorageGate){SaveCore(provider,credential);Revisions[provider]++;}}
    void SaveCore(AccountProvider provider,AccountCredential credential)
    {
        if(credential.UsableToken(DateTimeOffset.Now)==null)throw new InvalidOperationException("Enter a valid, unexpired access token.");
        KeyVault.Save(context,JsonSerializer.Serialize(credential),Slot(provider));
        if(Load(provider)?.UsableToken(DateTimeOffset.Now)!=credential.UsableToken(DateTimeOffset.Now))
        {Disconnect(provider);throw new InvalidOperationException("Secure account storage verification failed. Authorization was not retained.");}
    }
    public int Revision(AccountProvider provider){lock(StorageGate)return Revisions[provider];}
    public void SaveIfUnchanged(AccountProvider provider,AccountCredential credential,int revision){lock(StorageGate){if(Revisions[provider]!=revision)throw new OperationCanceledException("Account connection changed during authorization.");SaveCore(provider,credential);Revisions[provider]++;}}
    public void Disconnect(AccountProvider provider){lock(StorageGate){Revisions[provider]++;KeyVault.Save(context,"",Slot(provider));KeyVault.Save(context,"","pending-oauth");}}
    public bool Authorized(AccountProvider provider){var credential=Load(provider);return credential?.UsableToken(DateTimeOffset.Now)!=null||credential?.CanRefresh==true;}
    AccountCredential? Load(AccountProvider provider)
    {try{var raw=KeyVault.Load(context,Slot(provider));return raw.Length==0?null:JsonSerializer.Deserialize<AccountCredential>(raw);}catch{return null;}}
    public async Task<string?> GetTokenAsync(AccountProvider provider,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();await RefreshGates[provider].WaitAsync(cancellation);
        try{
            AccountCredential? credential;int revision;lock(StorageGate){credential=Load(provider);revision=Revisions[provider];}
            var token=credential?.UsableToken(DateTimeOffset.Now);if(token!=null)return token;
            if(credential?.CanRefresh!=true)return null;
            var renewed=await new OAuthBrokerClient(AuthorizationHttp).Refresh(provider,credential,cancellation);
            cancellation.ThrowIfCancellationRequested();lock(StorageGate){if(Revisions[provider]!=revision)throw new OperationCanceledException("Account connection changed during renewal.");SaveCore(provider,renewed);}
            return renewed.UsableToken(DateTimeOffset.Now);
        }finally{RefreshGates[provider].Release();}
    }
    public async Task<object> Run(string name,JsonElement args,Brain brain,CancellationToken cancellation)
    {
        var group=name switch{"gmail_search" or "gmail_message"=>"gmail","calendar_upcoming"=>"calendar","drive_search"=>"drive","github_repositories" or "github_issues"=>"github",_=>""};
        if(group.Length==0||!brain.Enabled(group))throw new InvalidOperationException("This account tool is disabled.");
        string S(string key)=>args.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
        var api=new ReadOnlyAccountTools(Http,this);
        using var response=await (name switch{
            "gmail_search"=>api.GmailSearchAsync(S("query"),10,cancellation),
            "gmail_message"=>api.GmailMessageAsync(S("messageId"),cancellation),
            "calendar_upcoming"=>api.CalendarUpcomingAsync(DateTimeOffset.Now,10,cancellation),
            "drive_search"=>api.DriveListAsync(S("query"),20,cancellation),
            "github_repositories"=>api.GitHubRepositoriesAsync(20,cancellation),
            "github_issues"=>api.GitHubIssuesAsync(S("owner"),S("repository"),20,cancellation),
            _=>throw new InvalidOperationException("Unknown account tool.")});
        return response.RootElement.Clone();
    }
}
