using System.Text.Json;
namespace AuraOne.Integrations;
public sealed class AccountCredential
{
    public string AccessToken {get;set;}="";
    public DateTimeOffset? ExpiresAt {get;set;}
    public string Scope {get;set;}="";
    public string? UsableToken(DateTimeOffset now)=>string.IsNullOrWhiteSpace(AccessToken)||AccessToken.Length>8192||AccessToken.Any(char.IsWhiteSpace)||(ExpiresAt is {} at&&at<=now.AddSeconds(30))?null:AccessToken;
    public static AccountCredential Import(string text)
    {
        var grant=JsonSerializer.Deserialize<AccountCredential>(text)??throw new FormatException("Invalid account authorization.");
        if(grant.UsableToken(DateTimeOffset.Now)==null)throw new InvalidOperationException("Account authorization is empty, invalid or expired.");
        return grant;
    }
}
