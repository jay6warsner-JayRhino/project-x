using Android;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media.Projection;
using Android.OS;
using Android.Speech;
using Android.Speech.Tts;
using Android.Views;
using Android.Widget;
using System.Text.Json;
using System.Text.RegularExpressions;
using Color = Android.Graphics.Color;
using OperationCanceledException = System.OperationCanceledException;
using AuraOne.Integrations;
namespace AuraOne;

[Activity(Name="za.co.auraone.assistant.MainActivity", LaunchMode=LaunchMode.SingleTop, Label="Aura One", MainLauncher=true, Exported=true, Theme="@android:style/Theme.Material.Light.NoActionBar", ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.ScreenSize)]
[IntentFilter([Intent.ActionView],Categories=[Intent.CategoryDefault,Intent.CategoryBrowsable],DataScheme="za.co.auraone.assistant")]
public sealed partial class MainActivity : Activity, IRecognitionListener, TextToSpeech.IOnInitListener
{
    BrainStore store=null!; Brain brain=null!; NativeAccounts accounts=null!; LinearLayout page=null!; TextView? status; OrbView? orb;
    readonly AssistantClient assistant=new(); readonly Handler handler=new(Looper.MainLooper!);
    SpeechRecognizer? recognizer; TextToSpeech? tts; GeminiLive? live; CameraFrames? camera;
    CancellationTokenSource? asking; Action? permissionAction; Task audioStopped=Task.CompletedTask;
    bool listen,awake=true,speaking,busy,foreground=true,muted,disposed; int errors; string tab="Home",frame="",source="",liveUser="",liveReply="";
    static readonly Color Bg=Color.Rgb(247,248,254),Card=Color.White,Ink=Color.Rgb(24,34,57),Accent=Color.Rgb(56,119,255),Subtle=Color.Rgb(126,139,161);
    int D(float n)=>(int)(Resources!.DisplayMetrics!.Density*n+.5f);
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state); store=new BrainStore(FilesDir!.AbsolutePath); brain=store.Load(); brain.AddStarterRoutines(); Save(); accounts=new NativeAccounts(this); tts=new TextToSpeech(this,this);
        Window!.SetSoftInputMode(SoftInput.AdjustResize);
        if(SpeechRecognizer.IsRecognitionAvailable(this)){recognizer=SpeechRecognizer.CreateSpeechRecognizer(this);recognizer.SetRecognitionListener(this);}
        LiveService.StopRequested+=OnLiveStopRequested; ReminderReceiver.Delivered+=OnReminderDelivered;CaptureService.Frame+=OnFrame; CaptureService.Failed+=OnCaptureFailed; CaptureService.Ended+=OnCaptureEnded; ReminderReceiver.Reschedule(this,brain); Render();InitializeLocalFeatures();if(Intent?.Data?.Scheme=="za.co.auraone.assistant")handler.Post(()=>_=CompleteAccountAuthorization(Intent.Data.ToString()));else if(Intent?.GetBooleanExtra("aura_start_voice",false)==true)handler.PostDelayed(()=>{if(!disposed)ToggleTalk();},700);
    }
    public void OnInit(OperationResult result){if(result==OperationResult.Success)Voice();}
    void Voice(){tts?.SetLanguage(Java.Util.Locale.ForLanguageTag(brain.Language));tts?.SetSpeechRate(brain.VoiceRate);tts?.SetPitch(brain.VoicePitch);}
    void OnReminderDelivered(int id)=>RunOnUiThread(()=>{var reminder=brain.Reminders.FirstOrDefault(x=>x.Id==id);if(reminder!=null)reminder.Done=true;});
    void Save()=>store.Save(brain);
    LinearLayout Col()=>new(this){Orientation=Orientation.Vertical};
    LinearLayout Row(){var row=new LinearLayout(this){Orientation=Orientation.Horizontal};row.SetGravity(GravityFlags.CenterVertical);return row;}
    void Add(LinearLayout parent,View view,int height=-2,bool weight=false){var lp=new LinearLayout.LayoutParams(weight?0:-1,height,weight?1:0);lp.SetMargins(0,D(4),D(weight?5:0),D(4));parent.AddView(view,lp);}
    TextView Text(string value,float size=14,Color? color=null){var t=new TextView(this){Text=value,TextSize=size};t.SetTextColor(color??Ink);t.SetPadding(0,D(7),0,D(7));return t;}
    GradientDrawable Shape(Color color){var d=new GradientDrawable();d.SetColor(color);d.SetCornerRadius(D(22));return d;}
    Button Btn(string label,Action action,bool primary=false){var b=new Button(this){Text=label,TextSize=12};b.SetAllCaps(false);b.SetTextColor(primary?Color.White:Ink);b.Typeface=Typeface.Create("sans-serif-medium",TypefaceStyle.Normal);b.Background=Shape(primary?Accent:Card);b.Click+=(_,_)=>{try{action();}catch(Exception e){Error(e.Message);}};return b;}
    EditText Field(string value,string hint,bool multi=false){var f=new EditText(this){Text=value,Hint=hint,TextSize=14,InputType=Android.Text.InputTypes.ClassText|(multi?Android.Text.InputTypes.TextFlagMultiLine:Android.Text.InputTypes.TextFlagCapSentences)};f.SetTextColor(Ink);f.SetHintTextColor(Subtle);f.Background=Shape(Card);f.SetPadding(D(12),D(10),D(12),D(10));f.SetSingleLine(!multi);return f;}
    void Section(string title,string? note=null){Add(page,Text(title,18,Accent));if(note!=null)Add(page,Text(note,12,Subtle));}
    void Toggle(string label,bool value,Action<bool> action){var s=new Switch(this){Text=label,Checked=value,TextSize=14};s.SetTextColor(Ink);s.SetPadding(0,D(10),0,D(10));s.CheckedChange+=(_,e)=>{action(e.IsChecked);Save();};Add(page,s);}
    void Render()=>RenderDesign();
    void Home()=>HomeDesign();
    void BrainPage()
    {
        Section("Memory","Facts saved on this device.");var memory=Field("","Remember something…",true);Add(page,memory);Add(page,Btn("Remember",()=>{if(!string.IsNullOrWhiteSpace(memory.Text)){brain.Memories.Add(memory.Text.Trim());Save();Render();}}),D(44));foreach(var m in brain.Memories.ToArray())Item(m,()=>brain.Memories.Remove(m),"Forget");
        Section("Notes");var note=Field("","Write a note…",true);Add(page,note);Add(page,Btn("Save note",()=>{if(!string.IsNullOrWhiteSpace(note.Text)){brain.Notes.Add(note.Text.Trim());Save();Render();}}),D(44));foreach(var n in brain.Notes.ToArray())Item(n,()=>brain.Notes.Remove(n));
        Section("Reminders","Android notifications can work after leaving the app. Battery saving and Doze may delay delivery.");Toggle("Speak reminders aloud",brain.SpeakReminders,x=>brain.SpeakReminders=x);Add(page,Btn("Add reminder",NewReminder),D(44));foreach(var r in brain.Reminders.OrderBy(x=>x.At).ToArray())Item($"{(r.Done?"✓ ":"")}{r.Text}\n{r.At.ToLocalTime():ddd, dd MMM · HH:mm}",()=>{ReminderReceiver.Cancel(this,r);brain.Reminders.Remove(r);});
        Section("Custom skills","Teach a trigger and steps. Enabled routines are included in the assistant's context.");Add(page,Btn("Teach a routine",NewSkill),D(44));foreach(var s in brain.Skills.ToArray())Item(s.Name+"\nWhen: "+s.Trigger+"\n"+s.Steps,()=>brain.Skills.Remove(s));
        Section("Backup & migration","Import a native backup or original Aura/Jarvis JSON. Data saved in the original browser needs an export from that browser.");Add(page,Btn("Export backup",Export),D(44));Add(page,Btn("Import backup / original data",()=>Pick(102)),D(44));
    }
    void Item(string text,Action remove,string label="Remove"){var c=Col();c.Background=Shape(Card);c.SetPadding(D(12),D(8),D(12),D(8));Add(c,Text(text));Add(c,Btn(label,()=>Confirm("Remove this item?",text,()=>{remove();Save();Render();})),D(38));Add(page,c);}
    void ToolsPage()
    {
        Section("Tools & plugins","Choose what the assistant may use.");foreach(var (key,value) in brain.Tools.ToArray()){var k=key;Toggle(char.ToUpper(k[0])+k[1..],value,x=>brain.Tools[k]=x);}
        Section("Helper roles");foreach(var role in new[]{"tutor","coder","translator","coach","chef","shopper"}){var r=role;Toggle(char.ToUpper(r[0])+r[1..],brain.Roles.Contains(r),x=>{if(x&&!brain.Roles.Contains(r))brain.Roles.Add(r);if(!x)brain.Roles.Remove(r);});}
        Section("Phone & browser shortcuts");Add(page,Btn("Open dialler",()=>Open("tel:")),D(42));foreach(var name in new[]{"YouTube","Google","GitHub","Gmail","Calendar","Drive","Maps"}){var n=name;Add(page,Btn("Open "+n,()=>OpenShortcut(n.ToLowerInvariant())),D(42));}
        Section("Native device controls","Device changes use an Allow once confirmation. Enable Accessibility or Notification access only for the functions you want.");
        Add(page,Btn("Set up Accessibility",()=>OpenNativeSettings(Android.Provider.Settings.ActionAccessibilitySettings)),D(44));
        Add(page,Btn("Set up notification summaries",()=>OpenNativeSettings(Android.Provider.Settings.ActionNotificationListenerSettings)),D(44));
        Add(page,Btn("Choose Aura One as assistant",ChooseAssistant),D(44));
        foreach(var tool in new[]{("Battery","battery",""),("Device information","device_info",""),("Notification sources","notifications_summary",""),("Shizuku status","shizuku_status",""),("Request Shizuku permission","shizuku_permission",""),("Device action history","audit_log","")})
        {var t=tool;Add(page,Btn(t.Item1,()=>_=RunDevice(t.Item2,t.Item3)),D(42));}
        Add(page,Btn("Device controls",DevicePanel),D(44));
        Section("Account tools","Optional read-only Gmail, Calendar, Drive and GitHub tools. Authorization belongs to this app; Lovable tokens do not automatically transfer.");
        Add(page,Btn("Account authorization",()=>{tab="Accounts";Render();}),D(44));
        Add(page,Btn("Connection and permissions status",()=>{tab="Diagnostics";Render();}),D(44));
        Add(page,Btn("Offline voice and wake setup",()=>{tab="Offline";Render();}),D(44));
        Section("Sources","Aura Your Pal: memory, notes, reminders, skills, history and research.\nAura Voice Connect: live voice, visual context, helper roles and plugins.\nJarvis chats: wake word, personality and device shortcuts.");
    }
    void OpenNativeSettings(string action)=>StartActivity(new Intent(action));
    void ChooseAssistant()
    {
        if(OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var roles=(Android.App.Roles.RoleManager)GetSystemService(RoleService)!;
            if(roles.IsRoleAvailable(Android.App.Roles.RoleManager.RoleAssistant)){StartActivityForResult(roles.CreateRequestRoleIntent(Android.App.Roles.RoleManager.RoleAssistant),105);return;}
        }
        OpenNativeSettings(Android.Provider.Settings.ActionManageDefaultAppsSettings);
    }
    async Task RunDevice(string action,string value="")
    {
        try{var result=await NativeDeviceTools.Run(this,action,value,CancellationToken.None);var text=JsonSerializer.SerializeToElement(result).GetProperty("result").GetString()??"";Status(text);Speak(text);}
        catch(OperationCanceledException){Status("Device action stopped.");}
        catch(Exception e){Error(e.Message);}
    }
    void DevicePanel()
    {
        var c=Col();c.SetPadding(D(18),0,D(18),0);
        var actions=new Spinner(this);actions.Adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerDropDownItem,DeviceActions.All);
        var value=Field("","Value, e.g. 50, on, 07:30, or button text");Add(c,actions);Add(c,value);
        new AlertDialog.Builder(this)!.SetTitle("Native device controls")!.SetView(c)!.SetNegativeButton("Cancel",(_,_)=>{})!.SetPositiveButton("Request",(_,_)=>_=RunDevice(DeviceActions.All[actions.SelectedItemPosition],value.Text?.Trim()??""))!.Show();
    }
    void AccountsPage()
    {
        AuthorizationOptions();
        Section("Read-only account authorization","Accounts connected inside Lovable remain there. This independent APK needs its own user-authorized access token or OAuth registration. Credentials stay encrypted on this phone and never enter Gemini context or backups.");
        Add(page,Text("Google: "+(accounts.Authorized(AccountProvider.Google)?"Authorization saved and unexpired":"No current authorization"),14,Accent));
        Add(page,Text("Scopes: Gmail read-only, Calendar read-only and Drive metadata read-only. A pasted Google OAuth access token is temporary; automatic Google sign-in still needs the app's OAuth registration.",12,Subtle));
        var google=Field("","Google OAuth access token");google.ContentDescription="Google OAuth access token";google.InputType=Android.Text.InputTypes.ClassText|Android.Text.InputTypes.TextVariationPassword;Add(page,google);
        var lifetime=Field("60","Token validity in minutes");lifetime.InputType=Android.Text.InputTypes.ClassNumber;Add(page,lifetime);
        Add(page,Btn("Save Google authorization",()=>{if(!int.TryParse(lifetime.Text,out var minutes)||minutes<1||minutes>1440){Error("Use a validity between 1 and 1,440 minutes.");return;}accounts.Save(AccountProvider.Google,new AccountCredential{AccessToken=google.Text?.Trim()??"",ExpiresAt=DateTimeOffset.Now.AddMinutes(minutes),Scope="gmail.readonly calendar.readonly drive.metadata.readonly"});brain.Tools["gmail"]=brain.Tools["calendar"]=brain.Tools["drive"]=true;Save();google.Text="";Render();}),D(44));
        Add(page,Btn("Disconnect Google from Aura",()=>{accounts.Disconnect(AccountProvider.Google);foreach(var name in new[]{"gmail","calendar","drive"})brain.Tools[name]=false;Save();Render();}),D(42));
        Section("GitHub",accounts.Authorized(AccountProvider.GitHub)?"GitHub authorization saved":"GitHub not authorized");
        Add(page,Text("Use a user-authorized token with read permissions for the repositories and issues you want Aura to read. No GitHub write operations are exposed.",12,Subtle));
        var github=Field("","GitHub access token");github.ContentDescription="GitHub access token";github.InputType=Android.Text.InputTypes.ClassText|Android.Text.InputTypes.TextVariationPassword;Add(page,github);
        Add(page,Btn("Save GitHub authorization",()=>{accounts.Save(AccountProvider.GitHub,new AccountCredential{AccessToken=github.Text?.Trim()??"",Scope="read-only account adapter"});brain.Tools["github"]=true;Save();github.Text="";Render();}),D(44));
        Add(page,Btn("Disconnect GitHub from Aura",()=>{accounts.Disconnect(AccountProvider.GitHub);brain.Tools["github"]=false;Save();Render();}),D(42));
        Add(page,Text("Enable only the account tools you need in Tools. Private account reads are disabled in the optional public-relay mode. Token expiry or revocation is reported as an authorization error.",12,Subtle));
    }
    void DiagnosticsPage()
    {
        Section("Ready to talk");
        Add(page,Text("Voice mode: "+(brain.LocalMode?"On-device":"Gemini Live 3.8")));
        Add(page,Text("Screen capture active: "+CaptureService.Active)); Add(page,Text("Captured frames: "+CaptureService.CapturedFrames));
        Add(page,Text("Google Search grounding: "+(brain.Enabled("web")?"Enabled":"Disabled")));
        Add(page,Text("Extended reasoning: "+(brain.Deep?"Enabled":"Disabled")));
        Add(page,Text($"Local knowledge: {brain.Memories.Count} memories · {brain.Notes.Count} notes · {brain.History.Count} conversations · {DeviceActions.All.Length} fixed device operations"));
        Add(page,Text("Gemini key: "+(KeyVault.Load(this).Length>0?"Saved on this phone":"Required for direct voice")));
        Add(page,Text("Connection: "+(live?.Connected==true?"Live voice connected":live!=null?"Connecting":"No active session")));
        Add(page,Text("Microphone permission: "+CheckSelfPermission(Manifest.Permission.RecordAudio)));
        Add(page,Text("Camera permission: "+CheckSelfPermission(Manifest.Permission.Camera)));
        try{using var state=JsonDocument.Parse(NativeDeviceTools.Status());foreach(var p in state.RootElement.EnumerateObject())Add(page,Text(p.Name+": "+p.Value.ToString()));}catch{Add(page,Text("Native device status unavailable."));}
        Add(page,Btn("Check Gemini Live setup",()=>_=Check()),D(44));
        Add(page,Btn("Check secure storage",()=>Error(KeyVault.Verify(this)?"Secure storage check passed.":"Secure storage check failed. No real credential was used.")),D(44));
        Add(page,Btn("Check PDF reader",()=>_=CheckPdfReader()),D(44));
        Add(page,Btn("Account authorization",()=>{tab="Accounts";Render();}),D(44));
    }
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if(intent?.Data?.Scheme=="za.co.auraone.assistant"){_=CompleteAccountAuthorization(intent.Data.ToString());return;}
        if(intent?.GetBooleanExtra("aura_start_voice",false)==true&&live==null)handler.PostDelayed(()=>{if(!disposed)ToggleTalk();},300);
    }
    void HistoryPage()
    {
        Section("Conversation history","Search the latest 500 saved turns.");var search=Field("","Search history");Add(page,search);var list=Col();Add(page,list);
        void Populate(){list.RemoveAllViews();foreach(var t in brain.Search(search.Text??"")){var c=Col();c.Background=Shape(Card);c.SetPadding(D(12),D(8),D(12),D(8));Add(c,Text($"{t.At.ToLocalTime():dd MMM · HH:mm} · {t.Mode}",11,Subtle));Add(c,Text("You: "+t.You));Add(c,Text(brain.AssistantName+": "+t.Reply,14,Accent));Add(c,Btn("Read aloud",()=>Speak(t.Reply)),D(38));Add(list,c);}}
        search.TextChanged+=(_,_)=>Populate();Populate();Add(page,Btn("Clear history",()=>Confirm("Clear history?","Export a backup first to keep a copy.",()=>{brain.History.Clear();Save();Render();})),D(44));
    }
    void SettingsPage()
    {
        Section("Free on-device AI","Speech recognition and the bundled language model run on this phone. No API keys, subscriptions, cloud credits or metered AI requests are needed. Phone storage, memory and battery still limit performance.");
        Toggle("Use on-device AI",brain.LocalMode,x=>{StopVoice();StopLive();StopVision();brain.LocalMode=x;brain.UseRelay=false;Save();Render();});
        Add(page,Btn("Tools and permissions",()=>OpenPage("Tools")),D(44));Add(page,Btn("Memory, notes and backup",()=>OpenPage("Brain")),D(44));Add(page,Btn("Offline voice setup",()=>OpenPage("Offline")),D(44));
        Section("Your companion");var name=Field(brain.AssistantName,"Assistant name");var user=Field(brain.UserName,"What should I call you?");var persona=Field(brain.Persona,"Personality",true);var instruction=Field(brain.Instructions,"Standing instructions",true);var city=Field(brain.HomeCity,"Home city");var lang=Field(brain.Language,"Speech locale, e.g. en-GB, en-ZA, zu-ZA, af-ZA");foreach(var f in new[]{name,user,persona,instruction,city,lang})Add(page,f);
        Toggle("Wake word while the app is visible",brain.WakeWord,x=>{brain.WakeWord=x;if(x&&brain.LocalMode){StartBackgroundWake();return;}if(x)Permission(Manifest.Permission.RecordAudio,()=>{listen=true;awake=false;ScheduleListen();});else StopVoice();});Add(page,Text("Recognises Jarvis, Baby, My Baby or your assistant name using Android speech recognition. On-device mode uses the bundled local wake-word service. Background wake-word controls are in Settings > Offline voice setup.",12,Subtle));
        Section("Voice");Add(page,Text("Device speech rate",12));var rate=new SeekBar(this){Max=100,Progress=(int)((brain.VoiceRate-.5f)*50)};Add(page,rate);Add(page,Text("Device speech pitch",12));var pitch=new SeekBar(this){Max=100,Progress=(int)((brain.VoicePitch-.5f)*50)};Add(page,pitch);
        var voices=new Spinner(this);var options=GeminiVoices.All;voices.Adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerDropDownItem,options);voices.SetSelection(Math.Max(0,Array.IndexOf(options,brain.LiveVoice)));Add(page,voices);Add(page,Text("Gemini Live uses the selected native audio voice. Device voice uses Android speech services.",12,Subtle));
        Section("Optional Gemini API key","On-device AI is the default. Enable optional online AI only if you want Gemini. Google API quota/billing applies to online mode. Keys are encrypted on the phone and excluded from backups.");
        var key=Field("",KeyVault.Load(this).Length>0?"Device key saved · enter a new key to replace":"Enter your Gemini API key on this phone");key.InputType=Android.Text.InputTypes.ClassText|Android.Text.InputTypes.TextVariationPassword;key.ContentDescription="Gemini API key";key.SetSingleLine(true);key.ImeOptions=Android.Views.InputMethods.ImeAction.Done;key.EditorAction+=(_,e)=>{if(e.ActionId==Android.Views.InputMethods.ImeAction.Done||e.Event?.KeyCode==Keycode.Enter){e.Handled=true;if(e.Event==null||e.Event.Action==KeyEventActions.Down)SaveGeminiKey(key);}};Add(page,key);
        Add(page,Btn("Save device key",()=>{SaveGeminiKey(key);}),D(44));
        Add(page,Btn("Remove device key",()=>Confirm("Remove device key?","Direct voice will need a new key. You can choose the optional server relay in Connections.",()=>KeyVault.Save(this,""))),D(42));
        Section("Connections","Use the optional server relay only if it has a working Gemini key. Direct mode sends audio and requests to Google using the encrypted device key.");Toggle("Use optional server relay",brain.UseRelay,x=>brain.UseRelay=x);var url=Field(brain.AssistantUrl,"HTTPS assistant endpoint");var liveUrl=Field(brain.LiveUrl,"WSS live relay endpoint");Add(page,url);Add(page,liveUrl);
        Add(page,Btn("Save settings",()=>{if(!Uri.TryCreate(url.Text,UriKind.Absolute,out var u)||u.Scheme!="https"){Error("Use an HTTPS assistant URL.");return;}if(!string.IsNullOrWhiteSpace(liveUrl.Text)&&(!Uri.TryCreate(liveUrl.Text,UriKind.Absolute,out var l)||l.Scheme!="wss")){Error("Use a WSS live relay URL.");return;}brain.AssistantName=string.IsNullOrWhiteSpace(name.Text)?"Jarvis":name.Text.Trim();brain.UserName=user.Text??"";brain.Persona=persona.Text??"";brain.Instructions=instruction.Text??"";brain.HomeCity=city.Text??"";brain.Language=lang.Text??"en-GB";brain.VoiceRate=.5f+rate.Progress/50f;brain.VoicePitch=.5f+pitch.Progress/50f;brain.LiveVoice=options[voices.SelectedItemPosition];brain.AssistantUrl=url.Text!.Trim();brain.LiveUrl=liveUrl.Text?.Trim()??"";Save();Voice();tab="Home";Render();},true),D(48));
        Add(page,Btn("Check Gemini Live connection",()=>_=Check()),D(44));Add(page,Text("Aura One 1.5 · Native Android assistant\nAccount integrations and device-specific behaviour still need end-to-end validation.",12,Subtle));
    }
    void SaveGeminiKey(EditText field){var value=field.Text?.Trim()??"";if(value.Length==0){Error("Enter your Gemini API key first.");return;}KeyVault.Save(this,value);field.Text="";Toast.MakeText(this,"Gemini device key saved",ToastLength.Short)?.Show();}
    void NewReminder(){var c=Col();c.SetPadding(D(18),0,D(18),0);var text=Field("","Reminder");var at=Field(DateTimeOffset.Now.AddMinutes(5).ToString("yyyy-MM-dd HH:mm zzz"),"Date and time with offset");Add(c,text);Add(c,at);new AlertDialog.Builder(this)!.SetTitle("New reminder")!.SetView(c)!.SetNegativeButton("Cancel",(_,_)=>{})!.SetPositiveButton("Save",(_,_)=>{if(string.IsNullOrWhiteSpace(text.Text)||!DateTimeOffset.TryParse(at.Text,out var when)||when<=DateTimeOffset.Now){Error("Enter a reminder and a future date/time.");return;}var r=new Reminder{Text=text.Text.Trim(),At=when};brain.Reminders.Add(r);Save();Notifications(()=>ReminderReceiver.Schedule(this,r));Render();})!.Show();}
    void NewSkill(){var c=Col();c.SetPadding(D(18),0,D(18),0);var name=Field("","Routine name");var trigger=Field("","When I say…");var steps=Field("","Follow these steps",true);foreach(var f in new[]{name,trigger,steps})Add(c,f);new AlertDialog.Builder(this)!.SetTitle("Teach a routine")!.SetView(c)!.SetNegativeButton("Cancel",(_,_)=>{})!.SetPositiveButton("Save",(_,_)=>{if(new[]{name.Text,trigger.Text,steps.Text}.Any(string.IsNullOrWhiteSpace)){Error("Complete all three fields.");return;}brain.Skills.Add(new Skill{Name=name.Text!.Trim(),Trigger=trigger.Text!.Trim(),Steps=steps.Text!.Trim()});Save();Render();})!.Show();}
    void Permission(string permission,Action action){if(CheckSelfPermission(permission)==Android.Content.PM.Permission.Granted)action();else{permissionAction=action;RequestPermissions([permission],42);}}
    void Notifications(Action action){if(OperatingSystem.IsAndroidVersionAtLeast(33))Permission(Manifest.Permission.PostNotifications,action);else action();}
    public override void OnRequestPermissionsResult(int code,string[] permissions,Android.Content.PM.Permission[] grants){base.OnRequestPermissionsResult(code,permissions,grants);var a=permissionAction;permissionAction=null;if(code==42&&grants.Length>0&&grants[0]==Android.Content.PM.Permission.Granted)a?.Invoke();else Error("Permission was not granted. Other features remain available.");}
    void ToggleVoice(){if(brain.LocalMode){ToggleTalk();return;}if(listen){StopVoice();if(backgroundWake)NativeLocal.Resume();Render();}else Permission(Manifest.Permission.RecordAudio,()=>{listen=true;StopLive();NativeLocal.Pause();awake=true;muted=false;errors=0;ScheduleListen();Render();});}
    void StopVoice(){asking?.Cancel();if(offlineVoice)NativeLocal.Stop(this);offlineVoice=false;listen=false;recognizer?.Cancel();tts?.Stop();speaking=false;}
    void ScheduleListen(int delay=600){handler.PostDelayed(()=>{if(disposed||!foreground||!listen||busy||speaking||muted||live!=null)return;if(offlineVoice){NativeLocal.Resume();return;}if(brain.LocalMode){listen=false;StartOfflineVoice();return;}if(recognizer==null){listen=false;Error("No Android speech recognition service is installed. Use Start Gemini Live for direct microphone conversation.");return;}try{var intent=new Intent(RecognizerIntent.ActionRecognizeSpeech);intent.PutExtra(RecognizerIntent.ExtraLanguageModel,RecognizerIntent.LanguageModelFreeForm);intent.PutExtra(RecognizerIntent.ExtraLanguage,brain.Language);intent.PutExtra(RecognizerIntent.ExtraPartialResults,true);intent.PutExtra(RecognizerIntent.ExtraMaxResults,1);recognizer.StartListening(intent);Status(awake?"Listening…":"Waiting for "+brain.AssistantName+"…");}catch{ScheduleListen(1500);}},delay);}
    public void OnReadyForSpeech(Bundle? p){errors=0;}public void OnBeginningOfSpeech(){}public void OnBufferReceived(byte[]? b){}public void OnEvent(int type,Bundle? p){}public void OnEndOfSpeech()=>Status("Processing speech…");
    public void OnRmsChanged(float value){if(orb!=null){orb.Level=Math.Clamp(value/12,0,1);orb.Invalidate();}}
    public void OnError(SpeechRecognizerError error){if(!listen)return;errors++;if(errors>6&&error!=SpeechRecognizerError.NoMatch&&error!=SpeechRecognizerError.SpeechTimeout){StopVoice();Error("Speech recognition stopped: "+error+". Tap Device voice to retry.");return;}ScheduleListen(Math.Min(4000,700+errors*300));}
    public void OnResults(Bundle? results){var text=results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault()?.Trim();if(!listen||string.IsNullOrEmpty(text)){ScheduleListen();return;}if(brain.WakeWord&&!awake){var after=Brain.AfterWake(text,brain.AssistantName);if(after==null){ScheduleListen();return;}awake=true;if(after.Length==0){Speak("At your service.");return;}text=after;}_=Ask(text);}
    public void OnPartialResults(Bundle? results){}
    async Task Ask(string text,string? attachment=null,string? historyLabel=null)
    {
        if(busy)return;if((brain.LocalMode||offlineVoice)&&attachment!=null){Error("Offline voice supports text and local commands. Use Gemini Live or device voice for visual reading.");ScheduleListen();return;}if(OfflineCommands.DocumentPage(text) is {} pageNumber&&attachment==null){try{await SharePdfPage(pageNumber);}catch(Exception e){Error(e.Message);}return;}busy=true;recognizer?.Cancel();NativeLocal.Pause();Status("Thinking…");asking=new CancellationTokenSource();
        try{var routine=brain.Enabled("skills")?brain.Skills.FirstOrDefault(s=>s.Trigger.Equals(text.Trim(),StringComparison.OrdinalIgnoreCase)):null;var reply=routine!=null?await RoutineAnswer(routine,asking.Token):await OfflineAction(text,asking.Token)??Local(text);if(reply==null&&(brain.LocalMode||offlineVoice)){Status("Thinking on your phone…");reply=await NativeLocal.Generate(this,OfflinePrompt(text),asking.Token);}if(reply==null){var answer=brain.UseRelay?await assistant.Ask(brain,text,attachment??(frame.Length==0?null:frame),asking.Token):(await DirectGeminiClient.Ask(brain,text,attachment??(frame.Length==0?null:frame),KeyVault.Load(this),(name,args)=>ExecuteTool(name,args,asking.Token),asking.Token),Array.Empty<JsonElement>());reply=answer.Item1;foreach(var action in answer.Item2)if(brain.Apply(action,out var r)&&r!=null)Notifications(()=>ReminderReceiver.Schedule(this,r));}brain.Record(new Turn{You=historyLabel??text,Reply=reply,Mode=brain.LocalMode||offlineVoice?"On-device":"Assistant"});Save();Status("Ready.");Speak(reply);}
        catch(System.OperationCanceledException){Status("Stopped.");}catch(Exception e){Error(e.Message);}finally{busy=false;asking?.Dispose();asking=null;if(brain.WakeWord)awake=false;if(!speaking)ScheduleListen();}
    }
    string? Local(string text)
    {
        if(text.Trim().Equals("plan my day",StringComparison.OrdinalIgnoreCase))return DailyPlan();var wake=Brain.AfterWake(text,brain.AssistantName);if(!string.IsNullOrEmpty(wake))text=wake;var low=text.Trim().ToLowerInvariant();
        if(brain.Enabled("time")&&Regex.IsMatch(low,@"^(what(?:'s| is) (?:the )?time|tell me (?:the )?time)"))return "It is "+DateTime.Now.ToString("HH:mm")+".";
        if(brain.Enabled("time")&&Regex.IsMatch(low,@"^(what(?:'s| is) (?:today's |the )?date|what day is it)"))return "Today is "+DateTime.Now.ToString("dddd, dd MMMM yyyy")+".";
        if(brain.Enabled("memory")&&low.StartsWith("remember ")){var fact=Regex.Replace(text,@"^remember\s+(?:that\s+)?","",RegexOptions.IgnoreCase).Trim();if(fact.Length==0)return "What should I remember?";brain.Memories.Add(fact);return "I have saved that to memory.";}
        if(brain.Enabled("memory")&&(low=="what do you remember"||low=="recall memories"))return brain.Memories.Count==0?"I have no saved memories yet.":string.Join(". ",brain.Memories);
        if(brain.Enabled("memory")&&low.StartsWith("forget ")){var fact=text[7..].Trim();if(fact.Length==0)return "Which fact should I forget?";var count=brain.Memories.RemoveAll(x=>x.Contains(fact,StringComparison.OrdinalIgnoreCase));return $"Removed {count} matching memories.";}
        if(brain.Enabled("notes")&&(low.StartsWith("take a note ")||low.StartsWith("save note "))){brain.Notes.Add(text[(low.StartsWith("take")?12:10)..].Trim());return "Note saved.";}
        if(brain.Enabled("notes")&&low=="read my notes")return brain.Notes.Count==0?"You have no saved notes.":string.Join(". ",brain.Notes);
        if(brain.Enabled("calculator")&&low.StartsWith("calculate "))return Calculator.Evaluate(text[10..]);
        if(brain.Enabled("phone")&&low.StartsWith("open ")&&OpenShortcut(low[5..].Trim()))return "Opening "+text[5..]+".";
        if(brain.Enabled("phone")&&(low.StartsWith("dial ")||low.StartsWith("call "))){var number=Regex.Replace(text[5..],@"[^0-9+*#]","");if(number.Length==0)return "Please say a phone number.";Open("tel:"+Uri.EscapeDataString(number));return "The dialler is ready. Confirm the call on your phone.";}
        if(low is "power down" or "goodbye jarvis" or "end session"){listen=false;return "Standing by. Until next time.";}return null;
    }
    bool OpenShortcut(string name){var uri=name switch{"youtube"=>"https://www.youtube.com","google"=>"https://www.google.com","github"=>"https://github.com","gmail"=>"https://mail.google.com","drive"=>"https://drive.google.com","calendar"=>"https://calendar.google.com","maps"=>"geo:0,0?q="+Uri.EscapeDataString(brain.HomeCity),_=>null};if(uri!=null){Open(uri);return true;}var i=new Intent(Intent.ActionMain);i.AddCategory(Intent.CategoryLauncher);var app=PackageManager!.QueryIntentActivities(i,PackageInfoFlags.MatchAll)?.FirstOrDefault(x=>(x.LoadLabel(PackageManager)?.ToLowerInvariant()??"")==name);var launch=app?.ActivityInfo?.PackageName is {} p?PackageManager.GetLaunchIntentForPackage(p):null;if(launch==null)return false;StartActivity(launch);return true;}
    void Open(string uri){try{StartActivity(new Intent(Intent.ActionView,Android.Net.Uri.Parse(uri)));}catch{Error("No installed app can open this shortcut.");}}
    void Speak(string text){if(muted||tts==null){ScheduleListen();return;}if(brain.LocalMode||offlineVoice){var offline=tts.Voices?.FirstOrDefault(v=>!v.IsNetworkConnectionRequired&&v.Locale?.Language=="en"&&(v.Features==null||!v.Features.Contains("notInstalled")));if(offline==null){StopVoice();Error("Install an offline English text-to-speech voice in Android speech settings.");return;}tts.SetVoice(offline);}NativeLocal.Pause();speaking=true;if(orb!=null){orb.Speaking=true;orb.Invalidate();}tts.SetOnUtteranceProgressListener(new Progress(this));tts.Speak(text,QueueMode.Flush,new Bundle(),Guid.NewGuid().ToString());}
    sealed class Progress(MainActivity app):UtteranceProgressListener{public override void OnStart(string? id){}public override void OnDone(string? id)=>app.RunOnUiThread(()=>{app.speaking=false;if(app.orb!=null){app.orb.Speaking=false;app.orb.Invalidate();}if(app.backgroundWake&&!app.listen&&!app.muted&&app.live==null)NativeLocal.Resume();app.ScheduleListen();});public override void OnError(string? id)=>OnDone(id);}
    void Status(string text)=>RunOnUiThread(()=>{if(status!=null)status.Text=text;});
    void Error(string text){Status(text);RunOnUiThread(()=>new AlertDialog.Builder(this)!.SetTitle("Aura One")!.SetMessage(text)!.SetPositiveButton("OK",(_,_)=>{})!.Show());}
    void Confirm(string title,string text,Action action)=>new AlertDialog.Builder(this)!.SetTitle(title)!.SetMessage(text)!.SetNegativeButton("Cancel",(_,_)=>{})!.SetPositiveButton("Continue",(_,_)=>action())!.Show();
    async Task Check()
    {
        if(brain.LocalMode){Error("Gemini is optional. Enable online AI in Settings to check its connection.");return;}if(live!=null){Status(live.Connected?"Gemini Live is already connected.":"Gemini Live is connecting.");return;}
        try
        {
            using var probe=new GeminiConnection(brain,KeyVault.Load(this),_=>{});
            await probe.Start();Speak("Gemini Live connection ready.");Status(brain.UseRelay?"Gemini relay setup confirmed.":"Direct Gemini Live setup confirmed.");
        }
        catch(OperationCanceledException){Error("Gemini connection check stopped before setup completed.");}
        catch(Exception e){Error(e.Message);}
    }
    void ToggleLive()
    {
        if(brain.LocalMode){ToggleTalk();return;}if(live!=null){StopLive();Render();return;}NativeLocal.Pause();
        if(!brain.UseRelay&&KeyVault.Load(this).Length==0){tab="Settings";Render();Error("Save your Gemini API key here, then start Gemini Live. Direct mode does not use Lovable credits.");return;}
        Permission(Manifest.Permission.RecordAudio,()=>{
            StopVoice();StartForegroundService(new Intent(this,typeof(LiveService)));
            GeminiLive? next=null;next=new GeminiLive(this,brain,e=>{if(next!=null)GeminiEvent(next,e);});
            next.SetMuted(muted);live=next;_=StartLive(next);Render();
        });
    }
    async Task StartLive(GeminiLive session)
    {
        try{await audioStopped;if(ReferenceEquals(live,session))await Task.Run(()=>session.Start());}
        catch(OperationCanceledException){}
        catch(Exception e){if(ReferenceEquals(live,session)){StopLive();Error("Live voice: "+e.Message);}}
    }
    void OnLiveStopRequested()=>RunOnUiThread(()=>{StopLive();Render();});
    void StopLive(){StopService(new Intent(this,typeof(LiveService)));var old=live;live=null;if(old!=null)audioStopped=old.StopAsync();FlushLive();if(backgroundWake)_=ResumeWake(audioStopped);}
    void GeminiEvent(GeminiLive session,JsonElement e)=>RunOnUiThread(()=>{
        if(!ReferenceEquals(live,session)||disposed)return;
        if(e.TryGetProperty("type",out var eventType)){var type=eventType.GetString();if(type=="app.connected"){Status("Gemini Live · Listening");if(frame.Length>0&&source.Length>0)session.SendFrame(frame);if(orb!=null){orb.Active=true;orb.Invalidate();}}else if(type is "app.status" or "app.reconnecting"&&e.TryGetProperty("message",out var statusMessage))Status(statusMessage.GetString()??"");else if(type=="app.error"){StopLive();var message=e.TryGetProperty("message",out var msg)?msg.GetString():e.TryGetProperty("error",out var err)&&err.ValueKind==JsonValueKind.Object&&err.TryGetProperty("message",out var nested)?nested.GetString():"Gemini connection failed.";Error(message??"Gemini connection failed.");}return;}
        if(e.TryGetProperty("serverContent",out var content)){
            if(content.TryGetProperty("inputTranscription",out var input)&&input.TryGetProperty("text",out var inputText)){if(liveReply.Length>0)FlushLive();liveUser+=inputText.GetString()??"";}
            if(content.TryGetProperty("outputTranscription",out var output)&&output.TryGetProperty("text",out var outputText))liveReply+=outputText.GetString()??"";
            if(content.TryGetProperty("interrupted",out var interrupted)&&interrupted.ValueKind==JsonValueKind.True)Status("Listening…");
            if(content.TryGetProperty("turnComplete",out var complete)&&complete.ValueKind==JsonValueKind.True){if(!brain.Deep){FlushLive();Status("Gemini Live · Listening");}else Status("Gemini Live · Background reasoning");}
            if(content.TryGetProperty("interactionStatus",out var interaction)){var state=interaction.GetString();if(state=="IDLE"){FlushLive();Status("Gemini Live · Listening");}else if(state=="IN_PROGRESS")Status("Gemini Live · Working");}
        }
        if(e.TryGetProperty("toolCall",out var toolCall)&&toolCall.TryGetProperty("functionCalls",out var calls))foreach(var call in calls.EnumerateArray())HandleGeminiTool(session,call);
    });
    async void HandleGeminiTool(GeminiLive current,JsonElement call)
    {
        if(!ReferenceEquals(live,current))return;
        var id=call.TryGetProperty("id",out var i)?i.GetString()??"":"";
        if(!current.FirstToolCall(id))return;
        if(!call.TryGetProperty("name",out var toolName)||toolName.ValueKind!=JsonValueKind.String)return;
        var name=toolName.GetString()??"";
        if(!current.CanCompleteTool(id))return;
        var response=await ExecuteTool(name,call.TryGetProperty("args",out var args)?args:JsonSerializer.SerializeToElement(new{}),current.Cancellation,()=>ReferenceEquals(live,current)&&current.CanCompleteTool(id));
        if(ReferenceEquals(live,current)&&current.CanCompleteTool(id))
            current.Send(new{toolResponse=new{functionResponses=new[]{new{id,name,response}}}});
    }
    async Task<object> ExecuteTool(string name,JsonElement args,CancellationToken cancellation=default,Func<bool>? stillAllowed=null)
    {
        object response;
        try{
            cancellation.ThrowIfCancellationRequested();
            if(name=="session_control"){
                var action=args.TryGetProperty("action",out var command)?command.GetString():"";
                switch(action){
                    case "sleep":case "end":StopLive();StopVoice();StopVision();Speak("Standing by. Until next time.");Render();break;
                    case "mute_microphone":SetMuted(true);Render();break;
                    case "stop_visuals":StopVision();Render();break;
                    case "share_camera":if(!foreground)throw new InvalidOperationException("Return to Aura One to share the camera.");if(source!="camera")ToggleCamera();break;
                    case "share_screen":if(!foreground)throw new InvalidOperationException("Return to Aura One to approve screen sharing.");if(source!="screen")ToggleScreen();break;
                    default:throw new InvalidOperationException("Unknown session control.");
                }response=new{requested=action};
            }else if(name=="document_page"){
                if(!brain.Enabled("documents"))throw new InvalidOperationException("Document tools are disabled.");
                if(!args.TryGetProperty("page",out var selectedPage)||!selectedPage.TryGetInt32(out var number))throw new InvalidOperationException("Specify a PDF page number.");
                response=await SharePdfPage(number);
            }else if(name=="local_search"){
                var query=args.TryGetProperty("query",out var q)?q.GetString()??"":"";response=new{results=LocalKnowledge.Search(brain,query)};
            }else if(name=="device_action"){
                if(!brain.Enabled("device"))throw new InvalidOperationException("Device controls are disabled.");
                var action=args.TryGetProperty("action",out var a)?a.GetString()??"":"";var value=args.TryGetProperty("value",out var v)?v.GetString()??"":"";
                if(!DeviceActions.ReadOnly(action)&&!foreground)throw new InvalidOperationException("Return to Aura One to approve the device action.");
                response=await NativeDeviceTools.Run(this,action,value,cancellation,stillAllowed);
            }else if(name is "gmail_search" or "gmail_message" or "calendar_upcoming" or "drive_search" or "github_repositories" or "github_issues"){
                brain.RequireCloud();if(brain.UseRelay)throw new InvalidOperationException("Account tools use direct Google mode to avoid the public relay.");
                response=await accounts.Run(name,args,brain,cancellation);
            }else if(name=="persist_action"){
                var action=args.TryGetProperty("action",out var nested)&&nested.ValueKind==JsonValueKind.Object?nested:args;
                var saved=brain.Apply(action,out var reminder);Save();if(reminder!=null)Notifications(()=>ReminderReceiver.Schedule(this,reminder));response=new{saved,notificationPermission=!OperatingSystem.IsAndroidVersionAtLeast(33)||CheckSelfPermission(Manifest.Permission.PostNotifications)==Android.Content.PM.Permission.Granted};
            }else if(name=="phone_action"){
                if(!brain.Enabled("phone"))throw new InvalidOperationException("Phone actions are disabled.");if(!foreground)throw new InvalidOperationException("Return to Aura One to open another app or the dialler.");
                var action=args.TryGetProperty("action",out var a)?a.GetString():args.TryGetProperty("type",out var actionType)?actionType.GetString():"";var target=args.TryGetProperty("target",out var targetValue)?targetValue.GetString()??"":args.TryGetProperty("app",out var app)?app.GetString()??"":args.TryGetProperty("number",out var number)?number.GetString()??"":"";
                if(action is "dial" or "call"){var digits=Regex.Replace(target,@"[^0-9+*#]","");if(digits.Length==0)throw new InvalidOperationException("A phone number is required.");Open("tel:"+Uri.EscapeDataString(digits));response=new{opened=true,requiresUserToPlaceCall=true};}
                else if(action=="open_app"){response=new{opened=OpenShortcut(target.ToLowerInvariant())};}else throw new InvalidOperationException("Unsupported phone action.");
            }else{if(brain.LocalMode&&name is not ("calculator" or "get_time"))throw new InvalidOperationException("This internet tool requires optional online mode.");response=await PublicTools.Run(name,args,brain);}
        }catch(Exception error){response=new{error=error.Message};}
        return response;
    }
    void FlushLive(){if(liveUser.Length>0||liveReply.Length>0){brain.Record(new Turn{You=liveUser,Reply=liveReply,Mode="Live"});Save();}liveUser=liveReply="";}
    void ToggleCamera(){if(source=="camera"){StopVision();Render();return;}if(brain.LocalMode||offlineVoice){Error("Offline voice supports text and local commands. Use Gemini Live or device voice for visual reading.");return;}Permission(Manifest.Permission.Camera,()=>{StopVision();camera=new CameraFrames(this,f=>OnFrame("camera",f));source="camera";tab="Home";Render();});}
    void ToggleScreen(){if(source=="screen"){StopVision();Render();return;}if(brain.LocalMode||offlineVoice){Error("Offline voice supports text and local commands. Use Gemini Live or device voice for visual reading.");return;}Permission(Manifest.Permission.RecordAudio,()=>{var manager=(MediaProjectionManager)GetSystemService(MediaProjectionService)!;StartActivityForResult(OperatingSystem.IsAndroidVersionAtLeast(34)?manager.CreateScreenCaptureIntent(MediaProjectionConfig.CreateConfigForDefaultDisplay()):manager.CreateScreenCaptureIntent(),100);});}
    void StopVision(){camera?.Close();camera=null;StopService(new Intent(this,typeof(CaptureService)));source=frame="";}
    void OnFrame(string kind,string value){if(kind!=source||disposed)return;frame=value;live?.SendFrame(value);}
    void OnCaptureFailed(string message)=>RunOnUiThread(()=>{Status(message);if(foreground)Error(message);});
    void OnCaptureEnded()=>RunOnUiThread(()=>{if(source=="screen"){source=frame="";Status("Screen sharing ended. Android requires fresh approval to start again.");}});
    void Pick(int code){var i=new Intent(Intent.ActionOpenDocument);i.AddCategory(Intent.CategoryOpenable);i.SetType(code==102?"application/json":"*/*");StartActivityForResult(i,code);}
    void Export(){var i=new Intent(Intent.ActionCreateDocument);i.AddCategory(Intent.CategoryOpenable);i.SetType("application/json");i.PutExtra(Intent.ExtraTitle,"aura-backup-"+DateTime.Now.ToString("yyyy-MM-dd")+".json");StartActivityForResult(i,101);}
    protected override async void OnActivityResult(int code,Result result,Intent? data)
    {
        base.OnActivityResult(code,result,data);if(result!=Result.Ok||data==null)return;
        try{
            if(code==100){brain.RequireCloud();var generation=CaptureService.NewSession();StopVision();source="screen";var i=new Intent(this,typeof(CaptureService));i.PutExtra("generation",generation);i.PutExtra("result",(int)result);i.PutExtra("data",data);StartForegroundService(i);tab="Home";Render();}
            else if(data.Data!=null&&code==101){using var output=ContentResolver!.OpenOutputStream(data.Data)!;await output.WriteAsync(System.Text.Encoding.UTF8.GetBytes(Brain.Export(brain)));Toast.MakeText(this,"Backup exported",ToastLength.Short)?.Show();}
            else if(data.Data!=null&&code==102){using var stream=ContentResolver!.OpenInputStream(data.Data)!;using var reader=new StreamReader(stream);var imported=Brain.Import(await reader.ReadToEndAsync());Confirm("Replace app data?","Export a backup first to keep your current data.",()=>{foreach(var r in brain.Reminders)ReminderReceiver.Cancel(this,r);brain=imported;Save();ReminderReceiver.Reschedule(this,brain);Voice();Render();});}
            else if(data.Data!=null&&code==104){if(!FileName(data.Data).EndsWith(".litertlm",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Choose a .litertlm model file.");using var model=ContentResolver!.OpenInputStream(data.Data)!;await NativeLocal.ImportModel(this,model,CancellationToken.None);Error("Offline language model imported.");}
            else if(data.Data!=null&&code==103){using var stream=ContentResolver!.OpenInputStream(data.Data)!;var type=ContentResolver.GetType(data.Data)??"";if(type.StartsWith("image/")){if(brain.LocalMode)throw new InvalidOperationException("Image reading needs optional online AI. On-device mode reads document text.");using var bitmap=await BitmapFactory.DecodeStreamAsync(stream);if(bitmap==null){Error("Could not read that image.");return;}var scale=Math.Min(1d,1280d/Math.Max(bitmap.Width,bitmap.Height));using var resized=Bitmap.CreateScaledBitmap(bitmap,(int)(bitmap.Width*scale),(int)(bitmap.Height*scale),true);using var bytes=new MemoryStream();resized.Compress(Bitmap.CompressFormat.Jpeg!,75,bytes);var attachment=Convert.ToBase64String(bytes.ToArray());Confirm("Share this image?","The image will be sent to Gemini. Ask your question aloud.",()=>{if(live?.Connected==true){live.SendFrame(attachment);live.Send(new{clientContent=new{turns=new[]{new{role="user",parts=new[]{new{text="I shared a photo. Briefly acknowledge it and wait for my spoken question."}}}},turnComplete=true}});}else _=Ask("Briefly describe this image aloud.",attachment);});}else await ShareDocument(data.Data);}
        }catch(Exception e){Error(e.Message);}
    }
    protected override void OnResume(){base.OnResume();foreground=true;NativeLocal.Visible(true);if(brain!=null&&!brain.LocalMode&&brain.WakeWord&&!listen&&live==null){listen=CheckSelfPermission(Manifest.Permission.RecordAudio)==Android.Content.PM.Permission.Granted;awake=false;}ScheduleListen();}
    protected override void OnPause(){foreground=false;NativeLocal.Visible(false);if(offlineVoice)NativeLocal.Pause();recognizer?.Cancel();if(source=="camera"){camera?.Close();camera=null;source=frame="";}base.OnPause();}
    protected override void OnDestroy(){disposed=true;localPolling=false;NativeLocal.Visible(false);asking?.Cancel();StopVoice();StopLive();StopVision();LiveService.StopRequested-=OnLiveStopRequested;ReminderReceiver.Delivered-=OnReminderDelivered;CaptureService.Frame-=OnFrame; CaptureService.Failed-=OnCaptureFailed;CaptureService.Ended-=OnCaptureEnded;recognizer?.Destroy();tts?.Shutdown();base.OnDestroy();}
}




