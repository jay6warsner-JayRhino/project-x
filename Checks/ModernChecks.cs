using AuraOne;
using AuraOne.Integrations;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
public static class ModernChecks
{
 public static async Task Run(Action<bool,string> check)
 {
  var local=new Brain();check(local.LocalMode&&!local.UseRelay,"new installations default to local AI without a relay");
  local.Notes.Add("Research artifact: blue notebook");local.History.Add(new Turn{You="Where is the blue folder?",Reply="On the desk."});
  check(LocalKnowledge.Search(local,"blue").Count==2,"knowledge search retrieves saved notes and conversation excerpts");
  local.Tools["notes"]=local.Tools["history"]=false;check(LocalKnowledge.Search(local,"blue").Count==0,"disabled knowledge groups are excluded");
  local.Tools["notes"]=true;local.Notes.AddRange(Enumerable.Repeat(new string('a',2000),20));check(LocalKnowledge.Search(local,"").Count==12&&LocalKnowledge.Search(local,"").All(h=>h.Text.Length<=1210),"knowledge results and excerpts are bounded");
  bool longQuery=false;try{LocalKnowledge.Search(local,new string('q',201));}catch(InvalidOperationException){longQuery=true;}check(longQuery,"knowledge search rejects excessive queries");
  check(Brain.Import("{\"AssistantUrl\":\"https://example.com\"}").LocalMode,"older backups migrate to local AI by default");
  bool cloudBlocked=false;try{local.RequireCloud();}catch(InvalidOperationException){cloudBlocked=true;}check(cloudBlocked,"local mode blocks cloud entry points");
  local.AddStarterRoutines();local.AddStarterRoutines();check(local.Skills.Count==4&&local.Skills.All(s=>!s.Enabled),"starter routines are added once and start paused");
  local.Skills[0].Enabled=true;var restored=Brain.Import(Brain.Export(local));check(restored.LocalMode&&restored.Skills[0].Enabled&&!restored.Skills[1].Enabled,"mode and routine toggles survive backup");
  using var text=new MemoryStream(Encoding.UTF8.GetBytes(new string('a',65000)));var plain=await DocumentReader.Read(text,"note.txt","text/plain");check(plain.Truncated&&plain.Text.Length==60000,"document text limit is explicit and bounded");
  using var word=Office(("word/document.xml","<document><p><t>Voice assistant document</t></p></document>"));var doc=await DocumentReader.Read(word,"sample.docx","application/vnd.openxmlformats-officedocument.wordprocessingml.document");check(doc.Text.Contains("Voice assistant document"),"DOCX text extracted without running Office code");
  using var slides=Office(("ppt/slides/slide1.xml","<slide><p><t>Slide one</t></p></slide>"));check((await DocumentReader.Read(slides,"sample.pptx","application/octet-stream")).Text.Contains("Slide one"),"PPTX slide text extracted");
  using var sheet=Office(("xl/sharedStrings.xml","<sst><si><t>Account label</t></si></sst>"),("xl/worksheets/sheet1.xml","<sheet><row><c t='s'><v>0</v></c><c><v>42</v></c></row></sheet>"));var cells=await DocumentReader.Read(sheet,"sample.xlsx","application/octet-stream");check(cells.Text.Contains("Account label")&&cells.Text.Contains("42"),"XLSX shared strings and numeric cells extracted");
  using var odt=Office(("content.xml","<document><p>OpenDocument text</p></document>"));check((await DocumentReader.Read(odt,"sample.odt","application/octet-stream")).Text.Contains("OpenDocument text"),"OpenDocument text extracted");
  bool blocked=false;using(var malicious=Office(("word/document.xml","<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///private'>]><document>&x;</document>"))){try{await DocumentReader.Read(malicious,"attack.docx","application/octet-stream");}catch(System.Xml.XmlException){blocked=true;}}check(blocked,"Office external entities are prohibited");
  var builder=new PdfDocumentBuilder();var page=builder.AddPage(PageSize.A4);var font=builder.AddStandard14Font(Standard14Font.Helvetica);page.AddText("Voice assistant document",12,new PdfPoint(25,700),font);var pdf=builder.Build();
  using(var input=new MemoryStream(pdf)){var result=await DocumentReader.Read(input,"sample.pdf","application/pdf");check(result.Pages==1&&result.Text.Contains("Voice")&&result.Text.Contains("document"),"real PDF text extracted using PdfPig");}
  var asset=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../AuraOne/Assets/document-test.pdf"));if(Directory.Exists(Path.GetDirectoryName(asset))&&!File.Exists(asset))File.WriteAllBytes(asset,pdf);
  var at=DateTimeOffset.Parse("2026-10-15T12:00:00+02:00");var reminder=OfflineCommands.Parse("remind me in 10 minutes to stretch",at)!;check(reminder.Arguments.GetProperty("at").GetString()==at.AddMinutes(10).ToString("o"),"offline reminder preserves the device timezone");
  check(OfflineCommands.Parse("set volume to 50 percent",at)?.Arguments.GetProperty("value").GetString()=="50","offline volume command uses fixed device tool");
  bool invalid=false;try{OfflineCommands.Parse("set volume to 200",at);}catch(InvalidOperationException){invalid=true;}check(invalid,"offline commands retain device bounds");
  check(OfflineCommands.Parse("set a timer for five minutes",at)?.Arguments.GetProperty("value").GetString()=="5","spoken timer uses a fixed bounded device action");
  bool timerBound=false;try{OfflineCommands.Parse("set a timer for three hundred minutes",at);}catch(InvalidOperationException){timerBound=true;}check(timerBound,"spoken timers enforce the device limit");
  check(OfflineCommands.Parse("run shell rm files",at)==null,"offline language does not introduce arbitrary shell commands");
  check(OfflineCommands.Parse("set volume to fifty percent",at)?.Arguments.GetProperty("value").GetString()=="50","offline volume understands spoken English numbers");
  check(OfflineCommands.Parse("remind me in ten minutes to stretch",at)?.Arguments.GetProperty("at").GetString()==at.AddMinutes(10).ToString("o"),"offline reminders understand speech-recognized number words");
  check(OfflineCommands.DocumentPage("read PDF page twenty two")==22,"document page selection understands spoken number words");
  check(OfflineCommands.Number("erase all files")==null,"unrecognized number input is not executed");
  using(var ordered=Office(("ppt/slides/slide10.xml","<slide><p>TENTH</p></slide>"),("ppt/slides/slide2.xml","<slide><p>SECOND</p></slide>"))){var result=await DocumentReader.Read(ordered,"ordered.pptx","application/octet-stream");check(result.Text.IndexOf("SECOND")<result.Text.IndexOf("TENTH"),"document sections retain numeric page order");}
  foreach(var broker in new[]{"http://broker.example","https://user:pass@broker.example","https://broker.example/path"}){bool rejected=false;try{OAuthBrokerClient.NormalizeBroker(broker);}catch(InvalidOperationException){rejected=true;}check(rejected,"OAuth broker rejects unsafe origin "+new Uri(broker).Scheme+" configuration");}
  using var handler=new OAuthHttp();using var http=new HttpClient(handler);var client=new OAuthBrokerClient(http);
  var pending=await client.Begin(AccountProvider.Google,"https://broker.example",false,CancellationToken.None);
  check(pending.Verifier.Length>=43&&pending.Nonce.Length>=32&&handler.StartChallenge.Length==43,"native OAuth uses random nonce and S256 PKCE");
  var callback=new Uri("za.co.auraone.assistant:/oauth/callback?state="+Uri.EscapeDataString(pending.State)+"&code=unit-code");
  bool mismatch=false;try{OAuthBrokerClient.ValidateReturn(pending,new Uri("za.co.auraone.assistant:/oauth/callback?state=wrong&code=unit"),DateTimeOffset.UtcNow);}catch(InvalidOperationException){mismatch=true;}check(mismatch,"foreign OAuth callback rejected before exchange");
  bool expired=false;try{OAuthBrokerClient.ValidateReturn(pending,callback,pending.ExpiresAt.AddSeconds(1));}catch(InvalidOperationException){expired=true;}check(expired,"expired OAuth callback rejected");
  var grant=await client.Complete(pending,callback,CancellationToken.None);check(grant.CanRefresh&&grant.UsableToken(DateTimeOffset.UtcNow)!=null,"OAuth exchange produces renewable credential lease");
  var renewed=await client.Refresh(AccountProvider.Google,grant,CancellationToken.None);check(renewed.RefreshToken==grant.RefreshToken&&renewed.AccessToken=="unit-renewed-access","OAuth refresh preserves refresh token when provider omits rotation");
  handler.Error=true;bool safe=false;try{await client.Begin(AccountProvider.Google,"https://broker.example",false,CancellationToken.None);}catch(InvalidOperationException e){safe=e.Message.Contains("registration")&&!e.Message.Contains("unit-secret");}check(safe,"OAuth registration failures do not expose provider response secrets");
 }
 static MemoryStream Office(params (string Path,string Xml)[] entries){var bytes=new MemoryStream();using(var archive=new ZipArchive(bytes,ZipArchiveMode.Create,true))foreach(var entry in entries){using var writer=new StreamWriter(archive.CreateEntry(entry.Path).Open());writer.Write(entry.Xml);}bytes.Position=0;return bytes;}
 sealed class OAuthHttp:HttpMessageHandler
 {
  public string StartChallenge="";public bool Error;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   if(Error)return new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{\"error\":\"registration_required\",\"secret\":\"unit-secret\"}")};
   using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));object value;
   if(request.RequestUri!.AbsolutePath.EndsWith("/start")){StartChallenge=body.RootElement.GetProperty("challenge").GetString()!;var state="signed-state-"+new string('s',64);value=new{state,authorizationUrl="https://accounts.google.com/o/oauth2/v2/auth?state="+state+"&code_challenge="+StartChallenge+"&code_challenge_method=S256"};}
   else if(request.RequestUri.AbsolutePath.EndsWith("/refresh"))value=new{access_token="unit-renewed-access",expires_in=3600};
   else value=new{access_token="unit-first-access",refresh_token="unit-long-refresh-token",expires_in=3600};
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))};
  }
 }
}
