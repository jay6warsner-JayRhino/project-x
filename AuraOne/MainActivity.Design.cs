using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Color=Android.Graphics.Color;
namespace AuraOne;

public sealed partial class MainActivity
{
 static readonly Color SoftBlue=Color.Rgb(229,237,255),Line=Color.Rgb(235,239,248);
 TextView Center(string value,float size,Color? color=null){var t=Text(value,size,color);t.SetPadding(0,D(3),0,D(3));t.Gravity=GravityFlags.Center;t.Typeface=Typeface.Create("sans-serif-medium",TypefaceStyle.Normal);return t;}
 void Put(LinearLayout parent,View child,int width,int height,float weight=0,int left=0){var p=new LinearLayout.LayoutParams(width<0?width:D(width),height<0?height:D(height),weight);p.LeftMargin=D(left);parent.AddView(child,p);}
 LinearLayout WhiteCard(){var box=Col();box.Background=Shape(Card);box.SetPadding(D(16),D(12),D(16),D(12));return box;}
 View IconButton(string kind,string description,Action action,Color? color=null){var icon=new AssistantIcon(this,kind,color??Ink){ContentDescription=description,Clickable=true,Focusable=true};icon.SetPadding(D(8),D(8),D(8),D(8));icon.Click+=(_,_)=>{try{action();}catch(Exception e){Error(e.Message);}};return icon;}
 void OpenPage(string name){tab=name;Render();}
 void RenderDesign()
 {
  status=null;orb=null;var root=Col();root.SetBackgroundColor(Bg);
  var inset=Resources!.GetIdentifier("status_bar_height","dimen","android");var top=inset>0?Resources.GetDimensionPixelSize(inset):D(24);
  root.SetPadding(D(19),top+D(10),D(19),D(20));
  Window!.DecorView.SystemUiVisibility=(StatusBarVisibility)(SystemUiFlags.LightStatusBar|SystemUiFlags.LightNavigationBar);
  Window.SetNavigationBarColor(Bg);Window.SetStatusBarColor(Bg);
  var header=Row();var title=Col();Add(title,Text("Assistant",20,Ink));var subtitle=Text("Your everyday AI companion",11,Subtle);subtitle.SetPadding(0,0,0,D(10));Add(title,subtitle);
  Put(header,title,0,-2,1);Put(header,IconButton("settings","Settings",()=>OpenPage("Settings")),36,40);
  var avatar=Center((brain.UserName.Length>0?brain.UserName[..1]:"A").ToUpperInvariant(),16,Accent);avatar.Background=Shape(SoftBlue);avatar.ContentDescription="Profile";avatar.Click+=(_,_)=>OpenPage("Settings");Put(header,avatar,34,34,0,10);Add(root,header);
  var scroll=new ScrollView(this){FillViewport=true};scroll.SetClipToPadding(false);page=Col();scroll.AddView(page);root.AddView(scroll,new LinearLayout.LayoutParams(-1,0,1));
  var nav=Row();nav.SetPadding(0,D(8),0,0);
  foreach(var entry in new[]{("Home","Talk","home"),("Chats","Chats","chats"),("Routines","Routines","routines")}){
   var selected=tab==entry.Item1||(entry.Item1=="Chats"&&tab=="History");var item=Col();item.SetGravity(GravityFlags.Center);item.ContentDescription=entry.Item2;
   var pill=Row();pill.SetGravity(GravityFlags.Center);pill.Background=selected?Shape(SoftBlue):null;Put(pill,new AssistantIcon(this,entry.Item3,selected?Accent:Subtle),23,23);Put(item,pill,58,32);
   var label=Center(entry.Item2,10,selected?Accent:Subtle);label.SetPadding(0,D(4),0,0);Put(item,label,-1,21);item.Click+=(_,_)=>OpenPage(entry.Item1);Put(nav,item,0,60,1);
  }
  Add(root,nav);SetContentView(root);
  switch(tab){case "Home":HomeDesign();break;case "Chats":ChatsDesign();break;case "Routines":RoutinesDesign();break;case "Brain":BrainPage();break;case "Tools":ToolsPage();break;case "History":HistoryPage();break;case "Accounts":AccountsPage();break;case "Offline":OfflinePage();break;case "Diagnostics":DiagnosticsPage();break;default:SettingsPage();break;}
 }
 void ToggleTalk()
 {
  if(listen||busy||speaking){StopVoice();if(backgroundWake&&!muted)NativeLocal.Resume();Render();return;}
  if(live!=null){StopLive();Render();return;}
  if(brain.LocalMode)StartOfflineVoice();else ToggleLive();
 }
 void HomeDesign()
 {
  var hour=DateTime.Now.Hour;var greeting=hour<12?"Good morning":hour<18?"Good afternoon":"Good evening";
  var hello=Center(greeting+(brain.UserName.Length>0?", "+brain.UserName:""),25);hello.SetPadding(0,D(27),0,0);Add(page,hello);
  status=Center(busy?"Thinking on your phone…":speaking?"Speaking…":listen||live?.Connected==true?"I'm listening…":"Ready when you are",17,Subtle);Add(page,status);
  orb=new OrbView(this){Active=listen||live?.Connected==true,Speaking=speaking,ShowFace=brain.CompanionFace,ContentDescription="Voice orb",Clickable=true};orb.Click+=(_,_)=>ToggleTalk();Add(page,orb,D(282));
  Add(page,Center("“Remind me to call Mom\n in ten minutes”",17,Ink));
  var chips=new HorizontalScrollView(this);chips.HorizontalScrollBarEnabled=false;var chipRow=Row();
  foreach(var chip in new[]{("Plan my day","calendar"),("Translate","translate"),("Set a timer","timer"),("Read a document","screen")}){
   var box=Row();box.Background=Shape(Card);box.SetPadding(D(10),D(8),D(10),D(8));Put(box,new AssistantIcon(this,chip.Item2,Accent),15,15);var text=Text(chip.Item1,10);text.SetPadding(D(5),0,0,0);Put(box,text,-2,20);
   var name=chip.Item1;box.Click+=(_,_)=>{if(name=="Plan my day")_=Ask("plan my day");else if(name=="Read a document")Pick(103);else{ToggleTalk();Status(name=="Translate"?"Say what you want translated":"Say: Set a timer for five minutes");}};
   Put(chipRow,box,-2,36,0,5);
  }
  chips.AddView(chipRow);Add(page,chips,D(44));
  var mode=Center(brain.LocalMode?"ON-DEVICE · NO API KEY OR CLOUD CREDITS":"OPTIONAL GEMINI LIVE · GOOGLE QUOTA APPLIES",10,Accent);Add(page,mode);
  var controls=Row();controls.Background=Shape(Color.Rgb(237,241,251));controls.SetPadding(D(15),D(4),D(8),D(4));
  var hint=Text(listen||live!=null?"Tap to end voice":"Tap the microphone to speak",13,Subtle);hint.ContentDescription=listen||live!=null?"End voice":"Start talking";hint.Click+=(_,_)=>ToggleTalk();Put(controls,hint,0,48,1);
  Put(controls,IconButton("mic",listen||live!=null?"End voice":"Start talking",ToggleTalk,Accent),40,44);
  Put(controls,IconButton("camera","Camera",ToggleCamera),38,44);Add(page,controls,D(56));
  if(listen||live!=null||backgroundWake){Add(page,Btn(muted?"Unmute":"Mute",()=>{SetMuted(!muted);Render();}),D(40));}
  if(source.Length>0){Add(page,Text("Sharing "+source,12,Accent));if(source=="camera"&&camera!=null){if(camera.Parent is ViewGroup parent)parent.RemoveView(camera);Add(page,camera,D(170));Add(page,Btn("Flip front / rear camera",()=>camera.Flip()),D(40));}Add(page,Btn("Stop sharing",()=>{StopVision();Render();}),D(40));}
  if(!brain.LocalMode){var online=Row();Add(online,Btn("Device voice",ToggleVoice),D(40),true);Add(online,Btn("Share screen",ToggleScreen),D(40),true);Add(page,online);Toggle("Extended thinking",brain.Deep,x=>brain.Deep=x);}
  Add(page,Text($"{brain.Memories.Count} memories · {brain.Reminders.Count(r=>!r.Done)} reminders",11,Subtle));
 }
 void ChatsDesign()
 {
  Add(page,Center("Your conversations",24));Add(page,Center("Saved on this phone",12,Subtle));
  if(brain.History.Count==0){var empty=WhiteCard();Add(empty,Center("A fresh start",20));Add(empty,Center("Your spoken conversations will appear here.\nNo typed chat is needed.",14,Subtle));Add(empty,Btn("Start talking",()=>{OpenPage("Home");ToggleTalk();},true),D(44));Add(page,empty);}
  foreach(var turn in brain.History.Take(35).Reverse()){
   Add(page,Center(turn.At.ToLocalTime().ToString("ddd, dd MMM · HH:mm"),10,Subtle));
   var user=WhiteCard();user.Background=Shape(SoftBlue);Add(user,Text(turn.You,14));Add(page,user);
   var reply=WhiteCard();Add(reply,Text(turn.Reply,14));Add(reply,Btn("Read aloud",()=>Speak(turn.Reply)),D(34));Add(page,reply);
  }
  Add(page,Btn("Search all history",()=>OpenPage("History")),D(42));
 }
 void RoutinesDesign()
 {
  Add(page,Center("Routines",27));Add(page,Center("Let your assistant do more for you",14,Subtle));
  Add(page,Text("Enable a routine, then say its trigger. Phone changes ask for your approval.",12,Subtle));
  var card=WhiteCard();
  foreach(var routine in brain.Skills){
   var row=Row();var kind=routine.Kind switch{"morning"=>"sun","bedtime"=>"moon","focus"=>"briefcase","driving"=>"driving",_=>"routines"};
   var badge=Row();badge.Background=Shape(SoftBlue);badge.SetGravity(GravityFlags.Center);Put(badge,new AssistantIcon(this,kind,Accent),23,23);Put(row,badge,40,40);
   var copy=Col();var title=Text(routine.Name,15);title.SetPadding(0,0,0,0);title.Typeface=Typeface.Create("sans-serif-medium",TypefaceStyle.Normal);Add(copy,title);var details=Text(routine.Steps,11,Subtle);details.SetPadding(0,0,0,D(5));Add(copy,details);Put(row,copy,0,-2,1,10);
   var enabled=new Switch(this){Checked=routine.Enabled,ContentDescription="Enable "+routine.Name};enabled.CheckedChange+=(_,e)=>{routine.Enabled=e.IsChecked;Save();};Put(row,enabled,48,45);Add(card,row);
   copy.Click+=(_,_)=>new Android.App.AlertDialog.Builder(this)!.SetTitle(routine.Name)!.SetMessage("Say: "+routine.Trigger+"\n\n"+routine.Steps)!.SetPositiveButton("Run",(_,_)=>_=RunSavedRoutine(routine))!.SetNegativeButton("Close",(_,_)=>{})!.SetNeutralButton("Remove",(_,_)=>Confirm("Remove routine?",routine.Name,()=>{brain.Skills.Remove(routine);Save();Render();}))!.Show();
   var divider=new View(this);divider.SetBackgroundColor(Line);Add(card,divider,D(1));
  }
  Add(page,card);var week=WhiteCard();Add(week,Text("This week",11,Subtle));var stats=Row();
  var talks=Col();Add(talks,Text(brain.History.Count(t=>t.At>=DateTimeOffset.Now.AddDays(-7)).ToString(),28));Add(talks,Text("voice conversations",11,Subtle));Add(stats,talks,weight:true);
  var reminders=Col();Add(reminders,Text(brain.Reminders.Count(r=>r.Done&&r.At>=DateTimeOffset.Now.AddDays(-7)).ToString(),28));Add(reminders,Text("reminders completed",11,Subtle));Add(stats,reminders,weight:true);Add(week,stats);Add(page,week);
  Add(page,Btn("+  New routine",NewSkill,true),D(48));
 }
 string DailyPlan(){var due=brain.Reminders.Where(r=>!r.Done&&r.At.ToLocalTime().Date==DateTime.Now.Date).OrderBy(r=>r.At).Select(r=>r.At.ToLocalTime().ToString("HH:mm")+", "+r.Text).ToArray();return "Today is "+DateTime.Now.ToString("dddd, dd MMMM")+". "+(due.Length>0?"Your reminders are: "+string.Join(". ",due):"You have no reminders for today.")+(brain.Notes.Count>0?" Your latest notes: "+string.Join(". ",brain.Notes.TakeLast(3)):"");}
 async Task<string> RoutineAnswer(Skill skill,CancellationToken ct)
 {
  if(!brain.Enabled("skills"))throw new InvalidOperationException("Routines are disabled in Tools and permissions.");
  if(!skill.Enabled)return "That routine is paused. Enable it in Routines first.";
  if(skill.Kind=="morning")return DailyPlan();
  var action=skill.Kind switch{"bedtime"=>("alarm","06:30"),"focus"=>("volume","20"),"driving"=>("notifications_summary",""),_=>("","")};
  if(action.Item1.Length>0){if(!brain.Enabled("device"))throw new InvalidOperationException("Device controls are disabled in Tools and permissions.");var result=await NativeDeviceTools.Run(this,action.Item1,action.Item2,ct);return System.Text.Json.JsonSerializer.SerializeToElement(result).GetProperty("result").GetString()??"Routine completed.";}
  if(brain.LocalMode)return await NativeLocal.Generate(this,OfflinePrompt("Follow this user-saved routine by explaining its steps aloud. Do not claim to perform actions: "+skill.Steps),ct);
  return "Say your routine trigger during a Gemini Live session to use this routine.";
 }
 async Task RunSavedRoutine(Skill skill){try{var reply=await RoutineAnswer(skill,CancellationToken.None);brain.Record(new Turn{You="Run routine: "+skill.Name,Reply=reply,Mode="Routine"});Save();Speak(reply);}catch(Exception e){Error(e.Message);}}
}

public sealed class AssistantIcon(Context context,string kind,Color color):View(context)
{
 protected override void OnDraw(Canvas canvas){base.OnDraw(canvas);canvas.Save();canvas.Translate(PaddingLeft,PaddingTop);canvas.Scale((Width-PaddingLeft-PaddingRight)/24f,(Height-PaddingTop-PaddingBottom)/24f);using var p=new Paint(PaintFlags.AntiAlias){Color=color,StrokeWidth=1.7f,StrokeCap=Paint.Cap.Round,StrokeJoin=Paint.Join.Round};p.SetStyle(Paint.Style.Stroke);using var path=new Android.Graphics.Path();
  switch(kind){
   case "home":path.MoveTo(3,11);path.LineTo(12,3);path.LineTo(21,11);path.MoveTo(5,10);path.LineTo(5,21);path.LineTo(10,21);path.LineTo(10,15);path.LineTo(14,15);path.LineTo(14,21);path.LineTo(19,21);path.LineTo(19,10);break;
   case "chats":canvas.DrawRoundRect(3,4,21,18,3,3,p);path.MoveTo(7,18);path.LineTo(6,22);path.LineTo(12,18);break;
   case "routines":canvas.DrawRoundRect(4,13,7,21,1,1,p);canvas.DrawRoundRect(10,5,13,21,1,1,p);canvas.DrawRoundRect(16,9,19,21,1,1,p);break;
   case "mic":canvas.DrawRoundRect(9,3,15,15,3,3,p);canvas.DrawArc(6,8,18,19,0,180,false,p);path.MoveTo(12,19);path.LineTo(12,22);break;
   case "camera":canvas.DrawRoundRect(3,7,21,21,3,3,p);canvas.DrawCircle(12,14,4,p);path.MoveTo(8,7);path.LineTo(9,3);path.LineTo(15,3);path.LineTo(16,7);break;
   case "settings":canvas.DrawCircle(12,12,6,p);canvas.DrawCircle(12,12,2,p);for(int i=0;i<8;i++){var a=i*Math.PI/4;canvas.DrawLine(12+(float)Math.Cos(a)*7,12+(float)Math.Sin(a)*7,12+(float)Math.Cos(a)*9,12+(float)Math.Sin(a)*9,p);}break;
   case "moon":path.MoveTo(16,3);path.CubicTo(2,0,0,21,15,21);path.CubicTo(18,21,21,19,22,16);path.CubicTo(9,20,6,7,16,3);break;
   case "sun":canvas.DrawCircle(12,12,4,p);for(int i=0;i<8;i++){var a=i*Math.PI/4;canvas.DrawLine(12+(float)Math.Cos(a)*7,12+(float)Math.Sin(a)*7,12+(float)Math.Cos(a)*9,12+(float)Math.Sin(a)*9,p);}break;
   case "briefcase":canvas.DrawRoundRect(3,7,21,21,2,2,p);canvas.DrawRoundRect(8,3,16,8,1,1,p);canvas.DrawLine(3,13,21,13,p);break;
   case "driving":path.MoveTo(3,18);path.LineTo(3,12);path.LineTo(6,4);path.LineTo(18,4);path.LineTo(21,12);path.LineTo(21,18);path.Close();canvas.DrawLine(4,11,20,11,p);canvas.DrawCircle(7,15,1,p);canvas.DrawCircle(17,15,1,p);break;
   case "timer":canvas.DrawCircle(12,14,8,p);canvas.DrawLine(9,2,15,2,p);canvas.DrawLine(12,6,12,2,p);canvas.DrawLine(12,14,15,10,p);break;
   case "calendar":canvas.DrawRoundRect(3,5,21,22,2,2,p);canvas.DrawLine(3,10,21,10,p);canvas.DrawLine(8,2,8,7,p);canvas.DrawLine(16,2,16,7,p);break;
   case "translate":canvas.DrawLine(2,6,13,6,p);canvas.DrawLine(7,2,7,6,p);path.MoveTo(11,6);path.CubicTo(10,12,6,16,2,18);canvas.DrawLine(4,9,11,16,p);path.MoveTo(12,22);path.LineTo(17,10);path.LineTo(22,22);canvas.DrawLine(14,18,20,18,p);break;
   default:canvas.DrawRoundRect(4,3,20,22,2,2,p);canvas.DrawLine(8,8,16,8,p);canvas.DrawLine(8,12,16,12,p);canvas.DrawLine(8,16,14,16,p);break;
  }
  canvas.DrawPath(path,p);canvas.Restore();
 }
}
