using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Keynote.Online;

namespace Keynote.Tests;
public class UpdatePackageServiceTests {
 sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>respond(request,token); }
 static byte[] Package(string name="Keynote.App.exe") {using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)){var entry=zip.CreateEntry(name);using var output=entry.Open();output.Write(new byte[]{77,90,1,2});}return stream.ToArray();}
 static string Metadata(byte[] package,string tag="v1.2.3",string? digest=null,string? url=null) => System.Text.Json.JsonSerializer.Serialize(new {tag_name=tag,assets=new[]{new {name="larpiano-windows-x64.zip",digest=digest ?? "sha256:"+Convert.ToHexStringLower(SHA256.HashData(package)),browser_download_url=url ?? "https://github.com/xhakkurw233/larpiano/releases/download/v1.2.3/larpiano-windows-x64.zip"}}});
 static string PortwaysMetadata(byte[] package, string tag="v1.2.3", string? sha=null, string? url=null) => System.Text.Json.JsonSerializer.Serialize(new {tag_name=tag,version=tag.TrimStart('v'),sha256=sha ?? Convert.ToHexStringLower(SHA256.HashData(package)),download_url=url ?? "/api/releases/latest/download"});
 static string Stage()=>Path.Combine(Path.GetTempPath(),"larpiano-test-"+Guid.NewGuid().ToString("N"));

 [Fact] public async Task DownloadsVerifiesAndExtractsPortwaysRelease() {
  var bytes=Package();
  using var http=new HttpClient(new Handler((r,t)=>{
   if(r.RequestUri!.PathAndQuery.Contains("/api/releases/latest/download"))
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(PortwaysMetadata(bytes))});
  }));
  var dir=Stage();
  try{
   var path=await new UpdatePackageService(http, "https://larpiano.portways.app").DownloadAsync("1.2.3",dir,null,CancellationToken.None);
   Assert.Equal(new byte[]{77,90,1,2},await File.ReadAllBytesAsync(path));
   Assert.False(File.Exists(Path.Combine(dir,"update.zip")));
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }

 [Fact] public async Task DownloadsVerifiesAndExtractsOnlyExecutable() {
  var bytes=Package();
  using var http=new HttpClient(new Handler((r,t)=>Task.FromResult(
   (r.RequestUri!.Host=="api.github.com" || r.RequestUri!.AbsolutePath.EndsWith("/api/releases/latest"))
    ? new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Metadata(bytes))}
    : new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})));
  var dir=Stage();
  try{
   var path=await new UpdatePackageService(http).DownloadAsync("1.2.3",dir,null,CancellationToken.None);
   Assert.Equal(new byte[]{77,90,1,2},await File.ReadAllBytesAsync(path));
   Assert.False(File.Exists(Path.Combine(dir,"update.zip")));
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }

 [Fact] public async Task RejectsBadChecksumAndCleansUp() {
  var bytes=Package();
  using var http=new HttpClient(new Handler((r,t)=>Task.FromResult(
   (r.RequestUri!.Host=="api.github.com" || r.RequestUri!.AbsolutePath.EndsWith("/api/releases/latest"))
    ? new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Metadata(bytes,digest:"sha256:"+new string('0',64)))}
    : new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})));
  var dir=Stage();
  try{
   await Assert.ThrowsAsync<InvalidDataException>(()=>new UpdatePackageService(http).DownloadAsync("1.2.3",dir,null,CancellationToken.None));
   Assert.True(!Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0);
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }

 [Fact] public async Task RejectsUntrustedRedirect() {
  var bytes=Package();
  using var http=new HttpClient(new Handler((r,t)=>{
   if(r.RequestUri!.Host=="api.github.com" || r.RequestUri!.AbsolutePath.EndsWith("/api/releases/latest"))
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Metadata(bytes))});
   var response=new HttpResponseMessage(HttpStatusCode.Redirect);
   response.Headers.Location=new Uri("https://example.com/file");
   return Task.FromResult(response);
  }));
  var dir=Stage();
  try{
   await Assert.ThrowsAsync<InvalidDataException>(()=>new UpdatePackageService(http).DownloadAsync("1.2.3",dir,null,CancellationToken.None));
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }

 [Fact] public async Task RejectsAnotherPortwaysSiteAsPackageSource() {
  var bytes=Package();
  using var http=new HttpClient(new Handler((r,t)=>Task.FromResult(
   new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(PortwaysMetadata(bytes,url:"https://other-project.portways.app/update.zip"))})));
  var dir=Stage();
  try {
   await Assert.ThrowsAsync<InvalidDataException>(()=>new UpdatePackageService(http).DownloadAsync("1.2.3",dir,null,CancellationToken.None));
  } finally {if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }

 [Fact] public async Task RejectsArchiveWithoutExecutable() {
  var bytes=Package("../Keynote.App.exe");
  using var http=new HttpClient(new Handler((r,t)=>Task.FromResult(
   (r.RequestUri!.Host=="api.github.com" || r.RequestUri!.AbsolutePath.EndsWith("/api/releases/latest"))
    ? new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Metadata(bytes))}
    : new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})));
  var dir=Stage();
  try{
   await Assert.ThrowsAsync<InvalidDataException>(()=>new UpdatePackageService(http).DownloadAsync("1.2.3",dir,null,CancellationToken.None));
   Assert.True(!Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0);
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }

 [Fact] public async Task RejectsChangedRelease() {
  var bytes=Package();
  using var http=new HttpClient(new Handler((r,t)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Metadata(bytes,tag:"v1.2.4"))})));
  var dir=Stage();
  try{
   await Assert.ThrowsAsync<InvalidDataException>(()=>new UpdatePackageService(http).DownloadAsync("1.2.3",dir,null,CancellationToken.None));
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }

 [Fact] public async Task CancelsDownload() {
  var bytes=Package();
  using var http=new HttpClient(new Handler(async(r,t)=>{
   if(r.RequestUri!.Host=="api.github.com" || r.RequestUri!.AbsolutePath.EndsWith("/api/releases/latest"))
    return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Metadata(bytes))};
   await Task.Delay(Timeout.Infinite,t);
   return new HttpResponseMessage(HttpStatusCode.OK);
  }));
  var dir=Stage();
  using var cancel=new CancellationTokenSource();
  try{
   var task=new UpdatePackageService(http).DownloadAsync("1.2.3",dir,null,cancel.Token);
   cancel.Cancel();
   await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
}
