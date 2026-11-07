using Keynote.Core;
using Keynote.Playback;
using Xunit;

namespace Keynote.Tests;

public class AutomaticSongRemapperTests {
 static readonly KeyboardMapping Mapping=new() {Keys=new() { [60]="A", [61]="SHIFT+A", [72]="S", [73]="SHIFT+S" }};
 static MidiSong Song(params MidiNote[] notes)=>new("test",new[]{new MidiTrack(0,notes)});
 static MidiNote Note(int pitch,int ms=0,int duration=1000)=>new(pitch,100,TimeSpan.FromMilliseconds(ms),TimeSpan.FromMilliseconds(duration),0);

 [Fact] public void PlayableSongIsUnchanged() {
  var song=Song(Note(60),Note(61,1000)); var result=AutomaticSongRemapper.Prepare(song,Mapping);
  Assert.Same(song,result.Song); Assert.Equal(0,result.AdjustedNotes); Assert.Equal(0,result.SkippedNotes);
 }
 [Fact] public void OutOfRangeNotesMoveByOctaveAndKeepTiming() {
  var original=Note(48,250,750); var result=AutomaticSongRemapper.Prepare(Song(original),Mapping);
  var mapped=Assert.Single(result.Song.Notes); Assert.Equal(60,mapped.Pitch); Assert.Equal(original.Start,mapped.Start); Assert.Equal(original.Duration,mapped.Duration); Assert.Equal(original.Velocity,mapped.Velocity); Assert.Equal(1,result.AdjustedNotes); Assert.Equal(0,result.SkippedNotes);
  PlaybackEngine.BuildEvents(result.Song,Mapping);
 }
 [Fact] public void ConflictingShiftedAndUnshiftedKeysUseAnotherOctave() {
  var result=AutomaticSongRemapper.Prepare(Song(Note(60),Note(61)),Mapping);
  Assert.Equal(2,result.Song.Notes.Count()); Assert.Equal(0,result.SkippedNotes); Assert.Equal(1,result.AdjustedNotes);
  PlaybackEngine.BuildEvents(result.Song,Mapping);
 }
 [Fact] public void UnavoidableConflictsAreReported() {
  var limited=new KeyboardMapping {Keys=new() {[60]="A",[61]="SHIFT+A"}};
  var result=AutomaticSongRemapper.Prepare(Song(Note(60),Note(61)),limited);
  Assert.Single(result.Song.Notes); Assert.Equal(1,result.SkippedNotes); PlaybackEngine.BuildEvents(result.Song,limited);
 }
 [Fact] public void RejectsEmptyMappingAndHonorsCancellation() {
  Assert.Throws<InvalidOperationException>(()=>AutomaticSongRemapper.Prepare(Song(Note(60)),new KeyboardMapping()));
  using var cts=new CancellationTokenSource(); cts.Cancel();
  Assert.Throws<OperationCanceledException>(()=>AutomaticSongRemapper.Prepare(Song(Note(60)),Mapping,cts.Token));
 }
}
