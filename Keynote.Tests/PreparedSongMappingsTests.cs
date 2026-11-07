using Keynote.Core;
using Xunit;

namespace Keynote.Tests;

public class PreparedSongMappingsTests {
 [Fact] public void UnrelatedFileIsLeftUnchanged() {
  var song=new MidiSong("test",new[]{new MidiTrack(0,new[]{new MidiNote(60,80,TimeSpan.Zero,TimeSpan.FromSeconds(1),0)})});
  Assert.Same(song,PreparedSongMappings.Apply(new string('0',64),song));
 }
 [Fact] public void KnownFileWithUnexpectedNoteCountIsRejected() {
  var song=new MidiSong("bad",Array.Empty<MidiTrack>());
  Assert.Throws<InvalidDataException>(()=>PreparedSongMappings.Apply("eb78ac1bac84f3cc6030e4075f78845a2a0c37d01286047a439e4e4141da2e44",song));
 }
 [Fact] public void CatalogueMappingRejectsWrongNoteCount() {
  var song=new MidiSong("bad",Array.Empty<MidiTrack>());
  Assert.Throws<InvalidDataException>(()=>PreparedSongMappings.Apply("c91a977918bae9fc245257452b2253cc834018c9f7fba9f4f11e911aae9335f6",song));
 }
 [Fact] public void NewCatalogueMappingRejectsWrongNoteCount() {
  var song=new MidiSong("bad",Array.Empty<MidiTrack>());
  Assert.Throws<InvalidDataException>(()=>PreparedSongMappings.Apply("587dbf3aecfb7f15bb90bc4006049ebb386f5f7deea8d02dd359b79bdea2e574",song));
 }
 [Fact] public void YourGirlMappingRejectsUnexpectedNoteCount() {
  var song=new MidiSong("bad",Array.Empty<MidiTrack>());
  Assert.Throws<InvalidDataException>(()=>PreparedSongMappings.Apply("71ed6ede2116d7cc3b0239ba517966799795e7ef6262c1519999ac550d2c309f",song));
 }
 [Theory]
 [InlineData("197e15f23786b435e150fd6e63d24e87b3ab11f7d7b1ccb88b6c3c90e7d590cc")]
 [InlineData("34bf7c563f499e4447364719d4b1ac8d97499d03ea14cd8b1be36d46fb7e97ca")]
 [InlineData("0aac6a5e499b1a5185c9438220a724b595ba312d8ff60e7fb60d4f7945ecdd77")]
 [InlineData("5eee88b0fba67613426174b4a145af119ec1a946d6f2063dc2982cc175ffcb16")]
 [InlineData("c692ce02f05bf8102b6439a4893bd203e3d51d5c7d6c46be0e1b64f495405998")]
 [InlineData("bf6dc22bb93b10939798a5b00ee4a705fa8e107b5783b642cbc595c1345b13a4")]
 public void NewLanaMappingsRejectUnexpectedNoteCounts(string hash) {
  var song=new MidiSong("bad",Array.Empty<MidiTrack>());
  Assert.Throws<InvalidDataException>(()=>PreparedSongMappings.Apply(hash,song));
 }
}
