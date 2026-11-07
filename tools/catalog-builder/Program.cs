using System.Security.Cryptography;
using System.Text.Json;
using Keynote.Core;
using Keynote.Midi;
using Keynote.Playback;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

if (args.Length != 3) {
 Console.Error.WriteLine("Usage: catalog-builder <source-manifest.json> <empty-staging-directory> <reference-profile.json>");
 return 2;
}
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
var manifestPath = Path.GetFullPath(args[0]);
var target = Path.GetFullPath(args[1]);
var profilePath = Path.GetFullPath(args[2]);
if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) {
 Console.Error.WriteLine("Staging directory must be empty; no existing files were changed.");
 return 2;
}
var inputs = JsonSerializer.Deserialize<SourceEntry[]>(await File.ReadAllTextAsync(manifestPath), options) ?? throw new InvalidDataException("Source manifest must be an array.");
var profile = JsonSerializer.Deserialize<KeyboardMapping>(await File.ReadAllTextAsync(profilePath), options) ?? throw new InvalidDataException("Invalid reference profile.");
if(profile.Verified || profile.Keys.Count==0 || profile.Duplicates().Any() || profile.Keys.Values.Any(k=>!KeyboardMapping.IsValidKey(k)))throw new InvalidDataException("Use a valid, explicitly unverified reference profile.");
Directory.CreateDirectory(target);
var importer = new MidiImporter();
var accepted = new List<CatalogEntry>();
var failures = new List<object>();
var seen = new HashSet<string>(StringComparer.Ordinal);
foreach (var source in inputs) {
 try {
  ValidateSource(source);
  byte[] originalBytes=await File.ReadAllBytesAsync(source.SourceMidi);
  var original=importer.Import(source.SourceMidi);
  if(!original.Notes.Any())throw new InvalidDataException("No notes in source MIDI.");
  byte[] bytes=originalBytes; int octave=0, adjusted=0, omitted=0; bool rewritten=false;
  int? whole=FindWholeOctave(original,profile);
  if(whole.HasValue) octave=whole.Value;
  else {
   var result=Rewrite(original,profile,importer,target);
   bytes=result.Bytes; adjusted=result.Adjusted; omitted=result.Omitted; rewritten=true;
  }
  var digest=Convert.ToHexStringLower(SHA256.HashData(bytes));
  if(!seen.Add(digest))throw new InvalidDataException("Duplicate MIDI output checksum.");
  var file=Path.Combine(target,digest+".mid");
  await File.WriteAllBytesAsync(file,bytes);
  MidiSong output;
  try { output=importer.Import(file); PlaybackEngine.BuildEvents(output,profile,octave); }
  catch { File.Delete(file); throw; }
  string description=rewritten
   ? $"{adjusted} notes adjusted by octaves; {omitted} notes omitted. Timing is preserved in normalized form; non-note MIDI events were removed."
   : octave==0 ? "Original MIDI; no notes omitted or adjusted." : $"Original MIDI; no notes omitted or adjusted; playback uses octave {octave:+0;-0;0}.";
  accepted.Add(new(digest,source.Title,source.Artist,source.Source,source.License,
   "Visual Pianos C2–C7 (calibration required)",octave,output.Notes.Count(),omitted,adjusted,
   source.Attribution,output.Duration.TotalSeconds,description));
  Console.WriteLine($"OK: {source.Title} | {output.Notes.Count()} notes, {adjusted} changed, {omitted} omitted, octave {octave:+0;-0;0}");
 } catch(Exception ex) { failures.Add(new { source.Title, Error=ex.Message }); Console.Error.WriteLine($"FAILED: {source.Title}: {ex.Message}"); }
}
await File.WriteAllTextAsync(Path.Combine(target,"manifest.json"),JsonSerializer.Serialize(accepted,options));
if(failures.Count>0)await File.WriteAllTextAsync(Path.Combine(target,"failures.json"),JsonSerializer.Serialize(failures,options));
Console.WriteLine($"Prepared {accepted.Count} arrangements; {failures.Count} failed.");
return failures.Count==0 ? 0 : 1;

static void ValidateSource(SourceEntry e) {
 if(string.IsNullOrWhiteSpace(e.Title) || string.IsNullOrWhiteSpace(e.Artist) || string.IsNullOrWhiteSpace(e.Attribution))throw new InvalidDataException("Title, artist and source attribution are required.");
 if(!string.Equals(e.License,"Public Domain",StringComparison.Ordinal))throw new InvalidDataException("Only reviewed Public Domain source arrangements are accepted.");
 if(!Uri.TryCreate(e.Source,UriKind.Absolute,out var uri) || uri.Scheme!=Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !(uri.Host.Equals("mutopiaproject.org",StringComparison.OrdinalIgnoreCase)||uri.Host.Equals("www.mutopiaproject.org",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Only official HTTPS Mutopia source references are accepted.");
 if(string.IsNullOrWhiteSpace(e.SourceMidi) || !Path.IsPathFullyQualified(e.SourceMidi) || !File.Exists(e.SourceMidi))throw new InvalidDataException("An existing absolute source MIDI path is required.");
}
static int? FindWholeOctave(MidiSong original,KeyboardMapping profile) {
 foreach(int octave in new[]{0,-1,1,-2,2}) try { PlaybackEngine.BuildEvents(original,profile,octave); return octave; } catch(InvalidOperationException) { }
 return null;
}
static (byte[] Bytes,int Adjusted,int Omitted) Rewrite(MidiSong original,KeyboardMapping profile,MidiImporter importer,string target) {
 var notes=original.Notes.OrderBy(n=>n.Start).ThenBy(n=>n.Track).ThenBy(n=>n.Pitch).ToArray();
 var best=new List<(int Index,int Pitch)>(); int bestMoved=int.MaxValue;
 foreach(int octave in new[]{0,-1,1,-2,2}) {
  var chosen=new List<(int Index,int Pitch)>(); var heldKeys=new Dictionary<char,List<long>>(); int moved=0;
  for(int i=0;i<notes.Length;i++) {
   var note=notes[i]; int desired=note.Pitch+12*octave;
   foreach(var candidate in profile.Keys.Where(p=>(p.Key-desired)%12==0).OrderBy(p=>Math.Abs(p.Key-desired)).ThenBy(p=>p.Key)) {
    KeyboardMapping.TryParseKey(candidate.Value,out char key,out _);
    if(!heldKeys.TryGetValue(key,out var held))heldKeys[key]=held=new();
    held.RemoveAll(end=>end<=note.Start.Ticks);
    if(held.Count>0)continue;
    chosen.Add((i,candidate.Key)); held.Add(note.Start.Ticks+Math.Max(500,note.Duration.Ticks));
    if(candidate.Key!=note.Pitch)moved++;
    break;
   }
  }
  if(chosen.Count>best.Count || (chosen.Count==best.Count && moved<bestMoved)) { best=chosen; bestMoved=moved; }
 }
 if(best.Count==0)throw new InvalidDataException("No playable notes remain after remapping.");
 var chunks=new List<TrackChunk>();
 foreach(var track in original.Tracks) {
  var edges=new List<(long Tick,bool On,int Pitch,int Velocity)>();
  foreach(var pair in best.Where(x=>notes[x.Index].Track==track.Index)) {
   var note=notes[pair.Index]; long start=(long)Math.Round(note.Start.Ticks/500.0); long end=Math.Max(start+1,(long)Math.Round((note.Start+note.Duration).Ticks/500.0));
   edges.Add((start,true,pair.Pitch,note.Velocity)); edges.Add((end,false,pair.Pitch,note.Velocity));
  }
  var chunk=new TrackChunk(); long last=0;
  foreach(var edge in edges.OrderBy(x=>x.Tick).ThenBy(x=>x.On?1:0)) {
   MidiEvent ev=edge.On?new NoteOnEvent((SevenBitNumber)edge.Pitch,(SevenBitNumber)edge.Velocity):new NoteOffEvent((SevenBitNumber)edge.Pitch,(SevenBitNumber)0);
   ev.DeltaTime=edge.Tick-last; chunk.Events.Add(ev); last=edge.Tick;
  }
  chunks.Add(chunk);
 }
 var temp=Path.Combine(target,"build-"+Guid.NewGuid().ToString("N")+".mid");
 try {
  new MidiFile(chunks.ToArray()){TimeDivision=new TicksPerQuarterNoteTimeDivision(10000)}.Write(temp,true);
  var output=importer.Import(temp); PlaybackEngine.BuildEvents(output,profile,0);
  var expected=best.Select(x=>(Note:notes[x.Index],Pitch:x.Pitch)).OrderBy(x=>x.Note.Track).ThenBy(x=>x.Note.Start).ThenBy(x=>x.Pitch).ToArray();
  var actual=output.Notes.OrderBy(x=>x.Track).ThenBy(x=>x.Start).ThenBy(x=>x.Pitch).ToArray();
  if(expected.Length!=actual.Length)throw new InvalidDataException("Output note count changed.");
  for(int i=0;i<actual.Length;i++) { var (note,pitch)=expected[i]; var got=actual[i];
   if(note.Track!=got.Track || pitch!=got.Pitch || note.Velocity!=got.Velocity || Math.Abs((note.Start-got.Start).TotalMilliseconds)>.026 || Math.Abs((note.Duration-got.Duration).TotalMilliseconds)>.051)throw new InvalidDataException($"Output timing, track, velocity or pitch changed at note {i}.");
  }
  return (File.ReadAllBytes(temp),best.Count(x=>notes[x.Index].Pitch!=x.Pitch),notes.Length-best.Count);
 } finally { if(File.Exists(temp))File.Delete(temp); }
}
sealed record SourceEntry(string Title,string Artist,string Source,string License,string Attribution,string SourceMidi);
sealed record CatalogEntry(string sha256,string title,string artist,string source,string license,string profile,int octave,int keptNotes,int excludedNotes,int notesAdjusted,string attribution,double durationSeconds,string modification);
