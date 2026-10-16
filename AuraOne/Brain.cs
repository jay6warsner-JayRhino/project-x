using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AuraOne;

public sealed class Skill { public string Name { get; set; } = ""; public string Trigger { get; set; } = ""; public string Steps { get; set; } = ""; public bool Enabled { get; set; } = true; public string Kind { get; set; } = "custom"; }
public sealed class Reminder { public int Id { get; set; } = Random.Shared.Next(1, int.MaxValue); public string Text { get; set; } = ""; public DateTimeOffset At { get; set; } public bool Done { get; set; } }
public sealed class Turn { public string Id { get; set; } = Guid.NewGuid().ToString(); public DateTimeOffset At { get; set; } = DateTimeOffset.Now; public string You { get; set; } = ""; public string Reply { get; set; } = ""; public string Mode { get; set; } = "Assistant"; }
public sealed class Brain
{
    public string AssistantName { get; set; } = "Jarvis";
    public string UserName { get; set; } = "";
    public string Persona { get; set; } = "Calm, witty, caring British companion. Speak naturally and concisely.";
    public string Instructions { get; set; } = "";
    public string HomeCity { get; set; } = "Johannesburg";
    public string Language { get; set; } = "en-GB";
    public float VoiceRate { get; set; } = 1.02f;
    public float VoicePitch { get; set; } = 1f;
    public string LiveVoice { get; set; } = "Charon";
    public string AssistantUrl { get; set; } = "https://aura-your-pal.lovable.app/api/assistant";
    public string LiveUrl { get; set; } = "wss://aura-your-pal.lovable.app/api/gemini-live";
    public bool UseRelay { get; set; }
    public bool LocalMode { get; set; } = true;
    public bool StarterRoutinesAdded { get; set; }
    public bool WakeWord { get; set; }
    public bool Captions { get; set; }
    public bool Deep { get; set; }
    public bool CompanionFace { get; set; }
    public bool SpeakReminders { get; set; } = true;
    public List<string> Memories { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public List<Reminder> Reminders { get; set; } = [];
    public List<Skill> Skills { get; set; } = [];
    public List<string> Roles { get; set; } = [];
    public List<Turn> History { get; set; } = [];
    public Dictionary<string, bool> Tools { get; set; } = new() { ["weather"] = true, ["time"] = true, ["web"] = true, ["wikipedia"] = true, ["calculator"] = true, ["notes"] = true, ["reminders"] = true, ["memory"] = true, ["skills"] = true, ["phone"] = true, ["device"] = true, ["documents"] = true, ["history"] = true, ["gmail"] = false, ["calendar"] = false, ["drive"] = false, ["github"] = false };
    public bool Enabled(string tool) => Tools.TryGetValue(tool, out var enabled) && enabled;
    public string[] EnabledTools() => Tools.Where(x => x.Value).Select(x => x.Key).ToArray();
    public void RequireCloud(){if(LocalMode)throw new InvalidOperationException("Online AI is off. On-device mode uses no API key or cloud credits. Enable optional online AI in Settings to use this feature.");}
    public void AddStarterRoutines()
    {
        if(StarterRoutinesAdded)return;
        foreach(var item in new[]{("Good morning","good morning","Read today's reminders and notes","morning"),("Bedtime","bedtime","Prepare an alarm for 06:30","bedtime"),("Focus","focus time","Lower media volume to 20 percent","focus"),("Driving","driving mode","Read notification source summaries","driving")})
            if(!Skills.Any(s=>s.Name.Equals(item.Item1,StringComparison.OrdinalIgnoreCase)))Skills.Add(new Skill{Name=item.Item1,Trigger=item.Item2,Steps=item.Item3,Kind=item.Item4,Enabled=false});
        StarterRoutinesAdded=true;
    }
    public void Record(Turn turn) { History.Insert(0, turn); if (History.Count > 500) History.RemoveRange(500, History.Count - 500); }
    public IEnumerable<Turn> Search(string query) => History.Where(x => (x.You + " " + x.Reply).Contains(query, StringComparison.OrdinalIgnoreCase));
    public string ProfilePersona() => $"Your name is {AssistantName}. {Persona} The user's name is {UserName}. Active helper roles: {string.Join(", ", Roles)}.";
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string Export(Brain brain) => JsonSerializer.Serialize(brain, Json);
    public static Brain Import(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object) throw new FormatException("Choose a JSON object exported from Aura or Jarvis.");
        // Full native backups preserve all settings. Original localStorage values can be objects or JSON strings.
        if (r.TryGetProperty("AssistantUrl", out _) || r.TryGetProperty("assistantUrl", out _)) return Normalize(JsonSerializer.Deserialize<Brain>(text, Json) ?? new());
        var b = new Brain();
        static JsonElement Decode(JsonElement e) => e.ValueKind == JsonValueKind.String ? JsonDocument.Parse(e.GetString()!).RootElement.Clone() : e;
        foreach (var key in new[] { "mybaby.brain.v1", "brain", "jarvis-settings-v1", "settings" })
            if (r.TryGetProperty(key, out var node)) Merge(Decode(node), b);
        Merge(r, b);
        if (r.TryGetProperty("mybaby.settings.v1", out var settings)) Merge(Decode(settings), b);
        if (r.TryGetProperty("jarvis_mems", out var mems)) b.Memories.AddRange(Decode(mems).EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
        if (r.TryGetProperty("mybaby.history.v1", out var history)) foreach (var t in Decode(history).EnumerateArray()) b.Record(new Turn { You = Str(t, "you"), Reply = Str(t, "baby"), At = DateTimeOffset.TryParse(Str(t, "at"), out var d) ? d : DateTimeOffset.Now });
        b.Memories = b.Memories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        b.History=b.History.OrderByDescending(x=>x.At).Take(500).ToList();
        return Normalize(b);
    }
    static Brain Normalize(Brain b)
    {
        b.Memories??=[];b.Notes??=[];b.Reminders??=[];b.Skills??=[];b.Roles??=[];b.History??=[];b.Tools??=new Brain().Tools;
        foreach(var pair in new Brain().Tools)if(!b.Tools.ContainsKey(pair.Key))b.Tools[pair.Key]=pair.Value;
        b.AssistantName=string.IsNullOrWhiteSpace(b.AssistantName)?"Jarvis":b.AssistantName;b.Persona??="";b.UserName??="";b.Instructions??="";b.Language??="en-GB";b.HomeCity??="Johannesburg";
        b.LiveVoice=GeminiVoices.All.Contains(b.LiveVoice)?b.LiveVoice:"Charon";b.VoiceRate=Math.Clamp(b.VoiceRate,.5f,2.5f);b.VoicePitch=Math.Clamp(b.VoicePitch,.5f,2.5f);
        if(!Uri.TryCreate(b.AssistantUrl,UriKind.Absolute,out var a)||a.Scheme!="https")throw new FormatException("The backup has an invalid assistant endpoint.");
        if(!Uri.TryCreate(b.LiveUrl,UriKind.Absolute,out var l)||l.Scheme!="wss")b.LiveUrl=new Brain().LiveUrl;
        return b;
    }
    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    static void Merge(JsonElement e, Brain b)
    {
        foreach (var name in new[] { "persona", "personality", "instructions", "userName", "homeCity", "language", "voice" })
        {
            var v = Str(e, name); if (v.Length == 0) continue;
            switch (name) { case "persona": case "personality": b.Persona = v; break; case "instructions": b.Instructions = v; break; case "userName": b.UserName = v; break; case "homeCity": b.HomeCity = v; break; case "language": b.Language = v; break; case "voice": b.LiveVoice = v; break; }
        }
        foreach (var name in new[] { "memories", "notes" }) if (e.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array) foreach (var item in items.EnumerateArray()) if (item.ValueKind == JsonValueKind.String) (name == "notes" ? b.Notes : b.Memories).Add(item.GetString()!);
        if (e.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array) foreach (var s in skills.EnumerateArray()) { if (s.ValueKind == JsonValueKind.String) b.Roles.Add(s.GetString()!); else if (s.ValueKind == JsonValueKind.Object) b.Skills.Add(new Skill { Name = Str(s, "name"), Trigger = Str(s, "trigger"), Steps = Str(s, "steps") }); }
        if (e.TryGetProperty("reminders", out var reminders) && reminders.ValueKind == JsonValueKind.Array) foreach (var s in reminders.EnumerateArray()) if (DateTimeOffset.TryParse(Str(s, "at"), out var at)) b.Reminders.Add(new Reminder { Text = Str(s, "text"), At = at, Done = s.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True });
        if (e.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Object) foreach (var p in tools.EnumerateObject()) b.Tools[p.Name] = p.Value.ValueKind == JsonValueKind.True;
        if (e.TryGetProperty("plugins",out var plugins)&&plugins.ValueKind==JsonValueKind.Array){var enabled=plugins.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()).ToHashSet();foreach(var k in new[]{"weather","wikipedia","calculator","time","memory"})b.Tools[k]=enabled.Contains(k);}
        if (e.TryGetProperty("wakeWord", out var wake)) b.WakeWord = wake.ValueKind == JsonValueKind.True;
        if (e.TryGetProperty("voiceRate", out var rate) && rate.TryGetSingle(out var f)) b.VoiceRate = f;
        if (e.TryGetProperty("voicePitch", out var pitch) && pitch.TryGetSingle(out var f2)) b.VoicePitch = f2;
    }
    public static string? AfterWake(string text, string assistantName)
    {
        var m = Regex.Match(text, @"\b(?:hey\s+|ok(?:ay)?\s+)?(?:my\s+)?(?:jarvis|jervis|baby|" + Regex.Escape(assistantName) + @")\b[\s,.!?]*", RegexOptions.IgnoreCase);
        return m.Success ? text[(m.Index + m.Length)..].Trim() : null;
    }
    public bool Apply(JsonElement action, out Reminder? reminder)
    {
        reminder = null; var type = Str(action, "type");
        string? group = type switch { "note" or "clear_notes" => "notes", "remember" or "forget" => "memory", "reminder" => "reminders", "skill" => "skills", _ => null };
        if (group == null || !Enabled(group)) return false;
        switch (type)
        {
            case "note": if (Str(action, "text") is { Length: > 0 } note) Notes.Add(note); else return false; break;
            case "clear_notes": Notes.Clear(); break;
            case "remember": if (Str(action, "fact") is { Length: > 0 } fact && !Memories.Contains(fact)) Memories.Add(fact); else return false; break;
            case "forget": var forget = Str(action, "fact"); if (string.IsNullOrWhiteSpace(forget)) return false; Memories.RemoveAll(x => x.Contains(forget, StringComparison.OrdinalIgnoreCase)); break;
            case "reminder": if (!DateTimeOffset.TryParse(Str(action, "at"), out var at) || at <= DateTimeOffset.Now || string.IsNullOrWhiteSpace(Str(action, "text"))) return false; reminder = new Reminder { Text = Str(action, "text"), At = at }; Reminders.Add(reminder); break;
            case "skill": var skill = new Skill { Name = Str(action, "name"), Trigger = Str(action, "trigger"), Steps = Str(action, "steps") }; if (string.IsNullOrWhiteSpace(skill.Name) || string.IsNullOrWhiteSpace(skill.Trigger) || string.IsNullOrWhiteSpace(skill.Steps)) return false; Skills.RemoveAll(x => x.Name == skill.Name); Skills.Add(skill); break;
        }
        return true;
    }
}

public sealed class BrainStore(string directory)
{
    readonly string file = Path.Combine(directory, "aura-brain.json");
    public Brain Load() { try { return File.Exists(file) ? Brain.Import(File.ReadAllText(file)) : new(); } catch { if(File.Exists(file))File.Copy(file,file+".unreadable-"+DateTime.UtcNow.Ticks+".json",true);return new(); } }
    public void Save(Brain brain) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); var tmp = file + ".tmp"; File.WriteAllText(tmp, Brain.Export(brain)); File.Move(tmp, file, true); }
}

