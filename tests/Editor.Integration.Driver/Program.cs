using System.Text.Json;
using System.Text.Json.Nodes;
using Editor.Timeline;

// Test-only, local stdin/stdout adapter. No HTTP service or production UI changes.
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var session = new TimelineSession(new TimelineState(0, Array.Empty<Clip>()));
var projectId = Guid.NewGuid();
EditorProject Project() => new(projectId, "Integração", session.Current, session.Media);
var results = new List<object>();
var commands = JsonNode.Parse(Console.In.ReadToEnd())!.AsArray();
foreach (var command in commands)
{
    var c = command!;
    string Text(string key) => c[key]!.GetValue<string>();
    Guid Id(string key = "id") => Guid.Parse(Text(key));
    MediaTime Time(string key = "seconds") => MediaTime.FromSeconds(c[key]!.GetValue<decimal>());
    object? value = null;
    object? error = null;
    try
    {
        switch (Text("op"))
        {
            case "import":
                var asset = MediaReferences.InspectFile(Text("path"));
                var existing = session.Media.FirstOrDefault(m => MediaReferences.SameSource(m.SourcePath, asset.SourcePath));
                if (existing is not null) { value = "duplicate"; break; }
                var clip = MediaReferences.PlanClip(session.Current, asset, Time(), Id());
                session.Execute(s => TimelineEngine.Insert(s, s.Version, clip), session.Media.Append(asset).ToArray());
                break;
            case "insert":
                var known = session.Media.Single(m => m.Id == Id("mediaId"));
                var planned = MediaReferences.PlanClip(session.Current, known, Time(), Id());
                session.Execute(s => TimelineEngine.Insert(s, s.Version, planned));
                break;
            case "trimLeft":
                session.Execute(s => s.Find(Id()).Track == 0
                    ? TimelineEngine.TrimMainLeft(s, s.Version, Id(), Time())
                    : TimelineEngine.TrimSecondaryLeft(s, s.Version, Id(), Time())); break;
            case "resize":
                session.Execute(s => s.Find(Id()).Track == 0
                    ? TimelineEngine.ResizeMain(s, s.Version, Id(), Time())
                    : TimelineEngine.ResizeSecondary(s, s.Version, Id(), Time())); break;
            case "split": session.Execute(s => TimelineEngine.Split(s, s.Version, Id(), Time(), Id("rightId"))); break;
            case "reorder": session.Execute(s => TimelineEngine.ReorderMain(s, s.Version, Id(), c["index"]!.GetValue<int>())); break;
            case "independent": session.Execute(s => TimelineEngine.SetIndependent(s, s.Version, Id())); break;
            case "move": session.Execute(s => TimelineEngine.DragSecondary(s, s.Version, Id(), c["track"]!.GetValue<int>(), Time())); break;
            case "marker": session.Execute(s => TimelineEngine.AddMarker(s, s.Version, new TimelineMarker(Id(), Time(), "Marcador"))); break;
            case "delete": session.Execute(s => s.Find(Id()).Track == 0
                ? TimelineEngine.DeleteMain(s, s.Version, Id()) : TimelineEngine.DeleteSecondary(s, s.Version, Id())); break;
            case "removeMedia": session.RemoveUnusedMedia(Id()); break;
            case "relink": session.RelinkMedia(Id(), Text("path")); break;
            case "restore": session.FitClipToSource(Id()); break;
            case "undo": session.Undo(); break;
            case "redo": session.Redo(); break;
            case "save": ProjectStore.Save(Text("path"), Project()); break;
            case "load":
                var loaded = ProjectStore.Load(Text("path"));
                var candidate = new TimelineSession(loaded.Timeline, loaded.MediaItems);
                session = candidate; projectId = loaded.Id; break;
            case "availability": value = session.Media.Select(m => new { m.Id, available = MediaReferences.IsAvailable(m) }).ToArray(); break;
            default: throw new ArgumentException("Unknown driver command");
        }
    }
    // Only domain/expected I/O failures become results. Unexpected bugs crash the driver.
    catch (Exception ex) when (ex is TimelineException or MediaReferenceException or ProjectFileException or IOException or UnauthorizedAccessException)
    {
        error = new { type = ex.GetType().Name, code = (ex as TimelineException)?.Code, message = ex.Message };
    }
    results.Add(new {
        error, value, session.CanUndo, session.CanRedo,
        project = JsonNode.Parse(ProjectStore.Serialize(Project())),
        displayDuration = Timecode.Format(session.Current.Duration)
    });
}
Console.WriteLine(JsonSerializer.Serialize(results, options));
