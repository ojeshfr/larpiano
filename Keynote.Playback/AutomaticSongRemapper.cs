using Keynote.Core;

namespace Keynote.Playback;

public sealed record AutomaticRemapResult(MidiSong Song, int AdjustedNotes, int SkippedNotes);

public static class AutomaticSongRemapper {
 public static AutomaticRemapResult Prepare(MidiSong song, KeyboardMapping mapping, CancellationToken cancellationToken = default) {
  cancellationToken.ThrowIfCancellationRequested();
  if(mapping.Keys.Count==0 || mapping.Duplicates().Any() || mapping.Keys.Any(k=>!KeyboardMapping.IsValidKey(k.Value)))
   throw new InvalidOperationException("The piano mapping is incomplete or invalid.");
  var notes=song.Notes.OrderBy(n=>n.Start).ThenBy(n=>n.Track).ThenBy(n=>n.Pitch).ToArray();
  if(notes.Length==0)throw new InvalidOperationException("The MIDI file contains no notes.");
  if(notes.Any(n=>n.Pitch is <0 or >127 || n.Start<TimeSpan.Zero || n.Duration<TimeSpan.Zero))throw new InvalidOperationException("The MIDI file contains invalid notes.");
  try { PlaybackEngine.BuildEvents(song,mapping); return new(song,0,0); } catch(InvalidOperationException) { }

  var candidates=new Dictionary<(int Pitch,int Octave),(int Pitch,char Key)[]>();
  foreach(var pitch in notes.Select(n=>n.Pitch).Distinct()) foreach(var octave in new[]{0,-1,1,-2,2}) {
   int desired=pitch+octave*12;
   candidates[(pitch,octave)]=mapping.Keys.Where(k=>(k.Key-pitch)%12==0 && k.Key is >=0 and <=127)
    .OrderBy(k=>Math.Abs(k.Key-desired)).ThenBy(k=>k.Key)
    .Select(k=>{ KeyboardMapping.TryParseKey(k.Value,out var key,out _); return (k.Key,key); }).ToArray();
  }
  List<(int Index,int Pitch)>? best=null; int bestChanged=int.MaxValue;
  foreach(var octave in new[]{0,-1,1,-2,2}) {
   var selected=new List<(int Index,int Pitch)>(); var held=new Dictionary<char,PriorityQueue<long,long>>(); int changed=0;
   for(int i=0;i<notes.Length;i++) {
    if((i & 1023)==0)cancellationToken.ThrowIfCancellationRequested();
    var note=notes[i];
    foreach(var candidate in candidates[(note.Pitch,octave)]) {
     if(!held.TryGetValue(candidate.Key,out var ends))held[candidate.Key]=ends=new();
     while(ends.TryPeek(out var end,out _) && end<=note.Start.Ticks)ends.Dequeue();
     if(ends.Count!=0)continue;
     selected.Add((i,candidate.Pitch)); long finish=checked(note.Start.Ticks+Math.Max(1,note.Duration.Ticks)); ends.Enqueue(finish,finish);
     if(candidate.Pitch!=note.Pitch)changed++;
     break;
    }
   }
   if(best==null || selected.Count>best.Count || selected.Count==best.Count && changed<bestChanged) { best=selected; bestChanged=changed; }
  }
  if(best==null || best.Count==0)throw new InvalidOperationException("None of this MIDI's notes can be played with the piano mapping.");
  var byTrack=best.Select(x=>notes[x.Index] with {Pitch=x.Pitch}).GroupBy(n=>n.Track).ToDictionary(g=>g.Key,g=>(IReadOnlyList<MidiNote>)g.ToArray());
  var result=song with {Tracks=song.Tracks.Select(t=>t with {Notes=byTrack.GetValueOrDefault(t.Index) ?? Array.Empty<MidiNote>()}).ToArray()};
  PlaybackEngine.BuildEvents(result,mapping);
  return new(result,bestChanged,notes.Length-best.Count);
 }
}
