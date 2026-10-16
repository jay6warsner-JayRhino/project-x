namespace AuraOne;
public sealed record KnowledgeHit(string Source,string Text);
public static class LocalKnowledge
{
 public static IReadOnlyList<KnowledgeHit> Search(Brain brain,string query)
 {
  query=query.Trim();if(query.Length>200)throw new InvalidOperationException("Use a search query of 200 characters or fewer.");
  var hits=new List<KnowledgeHit>();
  void Add(string source,IEnumerable<string> values){foreach(var value in values){if(hits.Count>=12)return;if(query.Length==0||value.Contains(query,StringComparison.OrdinalIgnoreCase))hits.Add(new(source,value.Length>1200?value[..1200]+" [excerpt]":value));}}
  if(brain.Enabled("memory"))Add("Memory",brain.Memories.AsEnumerable().Reverse());
  if(brain.Enabled("notes"))Add("Note",brain.Notes.AsEnumerable().Reverse());
  if(brain.Enabled("reminders"))Add("Reminder",brain.Reminders.OrderBy(r=>r.At).Select(r=>r.At.ToString("o")+" · "+(r.Done?"Completed: ":"Due: ")+r.Text));
  if(brain.Enabled("skills"))Add("Routine",brain.Skills.Select(s=>s.Name+" · Say: "+s.Trigger+" · "+s.Steps+" · "+(s.Enabled?"enabled":"paused")));
  if(brain.Enabled("history"))Add("Conversation",brain.History.Select(t=>t.At.ToString("o")+" · You: "+t.You+" · Assistant: "+t.Reply));
  return hits;
 }
}
