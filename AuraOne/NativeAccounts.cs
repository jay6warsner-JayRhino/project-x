using Android.Content;
using System.Text.Json;
using AuraOne.Integrations;
namespace AuraOne;
public sealed class NativeAccounts(Context context):IAccountAccessTokenProvider
{
    static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25)};
    static string Slot(AccountProvider provider)=>provider==AccountProvider.Google?"account-google":"account-github";
    public void Save(AccountProvider provider,AccountCredential credential)
    {
        if(credential.UsableToken(DateTimeOffset.Now)==null)throw new InvalidOperationException("Enter a valid, unexpired access token.");
        KeyVault.Save(context,JsonSerializer.Serialize(credential),Slot(provider));
    }
    public void Disconnect(AccountProvider provider)=>KeyVault.Save(context,"",Slot(provider));
    public bool Authorized(AccountProvider provider)=>Load(provider)?.UsableToken(DateTimeOffset.Now)!=null;
    AccountCredential? Load(AccountProvider provider)
    {try{var raw=KeyVault.Load(context,Slot(provider));return raw.Length==0?null:JsonSerializer.Deserialize<AccountCredential>(raw);}catch{return null;}}
    public Task<string?> GetTokenAsync(AccountProvider provider,CancellationToken cancellation)
    {cancellation.ThrowIfCancellationRequested();return Task.FromResult(Load(provider)?.UsableToken(DateTimeOffset.Now));}
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
