using System.Net.Http.Json;
using System.Text.Json;

namespace AuraOne;

public sealed class AssistantClient
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(3) };
    public async Task<(string Reply, JsonElement[] Actions)> Ask(Brain brain, string text, string? frame, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(brain.AssistantUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https") throw new InvalidOperationException("Choose an HTTPS assistant endpoint in Settings.");
        var messages = brain.History.Take(7).Reverse().SelectMany(t => new[] { new { role = "user", content = t.You }, new { role = "assistant", content = t.Reply } }).Concat([new { role = "user", content = text }]);
        var body = new { messages, profile = new { persona = brain.ProfilePersona(), instructions = brain.Instructions, memories = brain.Enabled("memory") ? brain.Memories : [], notes = brain.Enabled("notes") ? brain.Notes : [], reminders = brain.Enabled("reminders") ? brain.Reminders.Where(x => !x.Done).Select(x => new { text = x.Text, at = x.At.ToString("o") }) : [], skills = brain.Enabled("skills") ? brain.Skills.Select(x => new { name = x.Name, trigger = x.Trigger, steps = x.Steps }) : [] }, deep = brain.Deep, now = DateTimeOffset.Now.ToString("o"), timezone = TimeZoneInfo.Local.Id, enabledTools = brain.EnabledTools(), homeCity = brain.HomeCity, screen = frame == null ? null : "data:image/jpeg;base64," + frame };
        using var response = await http.PostAsJsonAsync(endpoint, body, Brain.Json, cancellation);
        var raw = await response.Content.ReadAsStringAsync(cancellation);
        JsonDocument json;
        try { json = JsonDocument.Parse(raw); } catch { throw new InvalidOperationException($"Assistant connection returned HTTP {(int)response.StatusCode}. Check the server URL and access."); }
        using (json)
        {
            var r = json.RootElement;
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(r.TryGetProperty("error", out var error) ? error.GetString() ?? "Assistant failed." : $"Assistant returned HTTP {(int)response.StatusCode}.");
            var reply = r.TryGetProperty("reply", out var p) ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(reply)) throw new InvalidOperationException("The assistant returned an empty answer.");
            return (reply, r.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array ? actions.EnumerateArray().Select(x => x.Clone()).ToArray() : []);
        }
    }
}
