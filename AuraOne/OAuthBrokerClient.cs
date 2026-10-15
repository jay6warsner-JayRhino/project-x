using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
namespace AuraOne.Integrations;
public sealed class PendingAuthorization
{
 public string Provider{get;set;}="";public string BrokerUrl{get;set;}="";public string Verifier{get;set;}="";
 public string Nonce{get;set;}="";public string State{get;set;}="";public string AuthorizationUrl{get;set;}="";
 public DateTimeOffset ExpiresAt{get;set;}
}
public sealed class OAuthBrokerClient(HttpClient http)
{
 public const string DefaultBroker="https://aura-one-oauth-jayrhino.onrender.com";
 static string B64(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
 public static string NormalizeBroker(string value)
 {
  if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host.Length==0||uri.UserInfo.Length>0||uri.AbsolutePath!="/"||uri.Query.Length>0||uri.Fragment.Length>0)throw new InvalidOperationException("Use an HTTPS broker origin without a path, query or password.");
  return uri.GetLeftPart(UriPartial.Authority);
 }
 static string Provider(AccountProvider provider)=>provider==AccountProvider.Google?"google":"github";
 public async Task<PendingAuthorization> Begin(AccountProvider provider,string broker,bool privateRepositories,CancellationToken ct)
 {
  var pending=new PendingAuthorization{Provider=Provider(provider),BrokerUrl=NormalizeBroker(broker),Verifier=B64(RandomNumberGenerator.GetBytes(48)),Nonce=B64(RandomNumberGenerator.GetBytes(32)),ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10)};
  var challenge=B64(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(pending.Verifier)));
  using var result=await Post(pending.BrokerUrl+"/oauth/"+pending.Provider+"/start",new{challenge,nonce=pending.Nonce,includePrivate=privateRepositories},ct);
  pending.State=String(result.RootElement,"state");pending.AuthorizationUrl=String(result.RootElement,"authorizationUrl");
  if(pending.State.Length is <40 or >1800||!Uri.TryCreate(pending.AuthorizationUrl,UriKind.Absolute,out var url)||url.Scheme!="https"||url.UserInfo.Length>0||url.Host!=(provider==AccountProvider.Google?"accounts.google.com":"github.com"))throw new InvalidOperationException("The broker returned an invalid authorization destination.");
  var query=Query(url);
  if(query.GetValueOrDefault("state")!=pending.State||query.GetValueOrDefault("code_challenge")!=challenge||query.GetValueOrDefault("code_challenge_method")!="S256")throw new InvalidOperationException("The broker did not bind authorization to this device request.");
  return pending;
 }
 public static string ValidateReturn(PendingAuthorization pending,Uri uri,DateTimeOffset now)
 {
  if(uri.Scheme!="za.co.auraone.assistant"||uri.Host.Length!=0||uri.AbsolutePath!="/oauth/callback"||pending.ExpiresAt<=now)throw new InvalidOperationException("Authorization callback is invalid or expired. Connect again.");
  var query=Query(uri);
  if(query.GetValueOrDefault("state")!=pending.State)throw new InvalidOperationException("Authorization belongs to a different connection request.");
  if(query.ContainsKey("error"))throw new InvalidOperationException("Account authorization was declined.");
  var code=query.GetValueOrDefault("code");if(string.IsNullOrEmpty(code)||code.Length>2048)throw new InvalidOperationException("Authorization code is missing or invalid.");
  return code;
 }
 static Dictionary<string,string> Query(Uri uri)
 {
  var values=new Dictionary<string,string>(StringComparer.Ordinal);
  foreach(var pair in uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
  {var parts=pair.Split('=',2);var name=Uri.UnescapeDataString(parts[0].Replace('+',' '));var value=parts.Length==2?Uri.UnescapeDataString(parts[1].Replace('+',' ')):"";if(!values.TryAdd(name,value))throw new InvalidOperationException("Duplicate authorization parameter.");}
  return values;
 }
 public async Task<AccountCredential> Complete(PendingAuthorization pending,Uri uri,CancellationToken ct)
 {
  if(pending.Provider is not ("google" or "github"))throw new InvalidOperationException("Unknown account provider.");
  var code=ValidateReturn(pending,uri,DateTimeOffset.UtcNow);
  using var result=await Post(NormalizeBroker(pending.BrokerUrl)+"/oauth/"+pending.Provider+"/exchange",new{code,state=pending.State,nonce=pending.Nonce,verifier=pending.Verifier},ct);
  return Credential(result.RootElement,pending.BrokerUrl,null);
 }
 public async Task<AccountCredential> Refresh(AccountProvider provider,AccountCredential existing,CancellationToken ct)
 {
  if(!existing.CanRefresh)throw new InvalidOperationException("Reconnect this account to renew its authorization.");
  using var result=await Post(NormalizeBroker(existing.BrokerUrl)+"/oauth/"+Provider(provider)+"/refresh",new{refresh_token=existing.RefreshToken},ct);
  return Credential(result.RootElement,existing.BrokerUrl,existing);
 }
 static AccountCredential Credential(JsonElement result,string broker,AccountCredential? old)
 {
  DateTimeOffset? expiry=null;
  if(result.TryGetProperty("expires_in",out var seconds)&&seconds.ValueKind==JsonValueKind.Number)
  {if(!seconds.TryGetInt32(out var duration)||duration<60||duration>31536000)throw new InvalidOperationException("Provider returned an invalid authorization expiry.");expiry=DateTimeOffset.UtcNow.AddSeconds(duration);}
  var refresh=String(result,"refresh_token");
  var credential=new AccountCredential{AccessToken=String(result,"access_token"),ExpiresAt=expiry,Scope=String(result,"scope"),RefreshToken=refresh.Length>0?refresh:old?.RefreshToken??"",BrokerUrl=NormalizeBroker(broker)};
  if(credential.Scope.Length==0&&old!=null)credential.Scope=old.Scope;
  if(credential.UsableToken(DateTimeOffset.UtcNow)==null||credential.RefreshToken.Length>8192)throw new InvalidOperationException("Provider returned an invalid account authorization.");
  return credential;
 }
 static string String(JsonElement element,string property)=>element.TryGetProperty(property,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
 async Task<JsonDocument> Post(string url,object body,CancellationToken ct)
 {
  using var response=await http.PostAsJsonAsync(url,body,ct);
  if(response.Content.Headers.ContentLength is >32768)throw new InvalidOperationException("Authorization response exceeded its size limit.");
  using var stream=await response.Content.ReadAsStreamAsync(ct);using var bytes=new MemoryStream();var buffer=new byte[4096];
  int count;while((count=await stream.ReadAsync(buffer,ct))>0){if(bytes.Length+count>32768)throw new InvalidOperationException("Authorization response exceeded its size limit.");await bytes.WriteAsync(buffer.AsMemory(0,count),ct);}
  JsonDocument document;try{document=JsonDocument.Parse(bytes.ToArray());}catch{throw new InvalidOperationException("The authorization service returned an invalid response.");}
  if(!response.IsSuccessStatusCode)
  {var error=String(document.RootElement,"error");document.Dispose();throw new InvalidOperationException(error=="registration_required"?"The broker is running, but the provider OAuth client registration is still required.":error=="rate_limited"?"Too many authorization requests. Try again shortly.":"Account authorization or renewal failed. Connect the account again.");}
  return document;
 }
}
