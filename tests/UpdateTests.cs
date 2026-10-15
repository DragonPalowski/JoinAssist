using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using JoinAssist;

internal static class UpdateTests
{
    static int count;
    static readonly byte[] payload = Encoding.UTF8.GetBytes("Synthetic VSIX payload, not an installer");
    static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static void Check(bool condition, string label) { count++; if (!condition) throw new Exception("Updater: " + label); }
    static void Reject(Action action, string label)
    { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, label); }
    static async Task RejectAsync(Func<Task> action, string label)
    { bool rejected = false; try { await action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, label); }

    static Dictionary<string,object> Data()
    {
        return new Dictionary<string,object> {
            { "tag_name", "v0.5.0" }, { "draft", false }, { "prerelease", false },
            { "html_url", UpdateClient.Repository + "/releases/tag/v0.5.0" },
            { "body", "Synthetic release notes" },
            { "assets", new object[] { new Dictionary<string,object> {
                { "name", "JoinAssist-0.5.0.vsix" }, { "state", "uploaded" }, { "size", payload.Length },
                { "digest", "sha256:" + Hash(payload) },
                { "browser_download_url", UpdateClient.Repository + "/releases/download/v0.5.0/JoinAssist-0.5.0.vsix" }
            } } }
        };
    }
    static Dictionary<string,object> Asset(Dictionary<string,object> data) { return (Dictionary<string,object>)((object[])data["assets"])[0]; }
    static string Json(Dictionary<string,object> data) { return new JavaScriptSerializer().Serialize(data); }
    static UpdateRelease Release() { return UpdateRelease.Parse(Json(Data())); }
    static void Bad(Action<Dictionary<string,object>> change, string label)
    { var data = Data(); change(data); Reject(() => UpdateRelease.Parse(Json(data)), label); }
    static HttpResponseMessage Bytes(byte[] bytes) { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }
    static HttpResponseMessage Redirect(string url)
    { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(url, UriKind.RelativeOrAbsolute); return response; }

    internal static async Task Run()
    {
        Check(UpdateRelease.ParseVersion("v0.5.0") == new Version(0,5,0,0), "three-part versions normalize revision");
        Check(UpdateRelease.ParseVersion("0.5.0.1") > UpdateRelease.ParseVersion("v0.5.0"), "four-part version compares numerically");
        Check(UpdateRelease.ParseVersion("v0.10.0") > UpdateRelease.ParseVersion("v0.9.9"), "numeric version comparison");
        foreach (string tag in new[] { null, "v0.5", "v0.5.0-beta", "v0.5.0/../bad", "v999999999999.0.0", "v0.5.0\n" })
            Reject(() => UpdateRelease.ParseVersion(tag), "reject malformed tag");
        var release = Release();
        Check(release.FileName == "JoinAssist-0.5.0.vsix" && release.Notes == "Synthetic release notes", "parse release metadata");
        Check(release.Size == payload.Length && release.Sha256 == Hash(payload), "parse exact digest and size");
        Check(UpdateClient.DisplayVersion(new Version(0,5,0,0)) == "0.5.0", "friendly version");
        Bad(d => d["draft"] = true, "draft rejected");
        Bad(d => d["prerelease"] = true, "preview rejected");
        Bad(d => d.Remove("draft"), "missing visibility rejected");
        Bad(d => d["html_url"] = "https://github.com/other/repo/releases/tag/v0.5.0", "foreign repository rejected");
        Bad(d => Asset(d)["browser_download_url"] = "https://example.invalid/fake.vsix", "foreign download rejected");
        Bad(d => Asset(d)["name"] = "../JoinAssist-0.5.0.vsix", "unexpected filename rejected");
        Bad(d => Asset(d)["state"] = "new", "unready asset rejected");
        Bad(d => Asset(d).Remove("digest"), "missing digest rejected");
        Bad(d => Asset(d)["digest"] = "sha256:bad", "invalid digest rejected");
        Bad(d => Asset(d)["size"] = 0, "zero size rejected");
        Bad(d => Asset(d)["size"] = UpdateClient.MaxPackageBytes + 1, "oversized package rejected");
        Bad(d => d["assets"] = new object[0], "missing package rejected");
        Bad(d => d["assets"] = new object[] { Asset(d), Asset(d) }, "duplicate packages rejected");
        foreach (string url in new[] { "http://github.com/DragonPalowski/JoinAssist/releases/download/v0.5.0/a.vsix", "https://github.com/other/repo/a.vsix",
            "https://release-assets.githubusercontent.com.evil.invalid/a", "https://user@release-assets.githubusercontent.com/a", "https://release-assets.githubusercontent.com:444/a", "https://example.invalid/a" })
            Check(!UpdateClient.AllowedDownload(new Uri(url)), "reject untrusted redirect");
        Check(UpdateClient.AllowedDownload(new Uri("https://release-assets.githubusercontent.com/asset?id=test")), "allow GitHub asset host");
        Check(UpdateClient.AllowedDownload(new Uri("https://objects.githubusercontent.com/asset")), "allow GitHub objects host");

        using (var handler = new FakeHandler((request, token) => Bytes(Encoding.UTF8.GetBytes(Json(Data())))))
        using (var client = new UpdateClient(handler))
        {
            Check(handler.Requests == 0, "no startup network traffic");
            var result = await client.CheckAsync(CancellationToken.None);
            Check(result.Version == new Version(0,5,0,0) && handler.Requests == 1, "latest release fetched on request");
            Check(handler.LastUri.AbsoluteUri == UpdateClient.LatestApi && !handler.SentAuthorization, "fixed public API, no credentials");
        }
        using (var client = new UpdateClient(new FakeHandler((r,t) => new HttpResponseMessage(HttpStatusCode.NotFound))))
            Check(await client.CheckAsync(CancellationToken.None) == null, "empty repository handled");
        using (var client = new UpdateClient(new FakeHandler((r,t) => new HttpResponseMessage((HttpStatusCode)429))))
            await RejectAsync(async () => { await client.CheckAsync(CancellationToken.None); }, "rate limit handled");
        using (var client = new UpdateClient(new FakeHandler((r,t) => Bytes(new byte[UpdateClient.MaxMetadataBytes + 1]))))
            await RejectAsync(async () => { await client.CheckAsync(CancellationToken.None); }, "metadata size bounded");

        string root = Path.Combine(Path.GetTempPath(), "JoinAssist-UpdateTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var progress = new InlineProgress();
            using (var handler = new FakeHandler((r,t) => r.RequestUri.Host == "github.com" ? Redirect("https://release-assets.githubusercontent.com/test") : Bytes(payload)))
            using (var client = new UpdateClient(handler))
            {
                string file = await client.DownloadAsync(release, root, CancellationToken.None, progress);
                Check(File.Exists(file) && File.ReadAllBytes(file).SequenceEqual(payload), "download promoted only after hash verification");
                Check(handler.Requests == 2 && handler.LastUri.Host == "release-assets.githubusercontent.com", "HTTPS redirect followed");
                Check(progress.Values.Last() == 100 && Directory.GetFiles(root,"*.partial",SearchOption.AllDirectories).Length == 0, "progress and no partial leftovers");
                string second = await client.DownloadAsync(release, root, CancellationToken.None);
                Check(file != second && File.Exists(file), "separate downloads do not overwrite each other");
            }
            int before = Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length;
            var corrupt = payload.ToArray(); corrupt[0] ^= 1;
            using (var client = new UpdateClient(new FakeHandler((r,t) => Bytes(corrupt))))
                await RejectAsync(async () => { await client.DownloadAsync(release,root,CancellationToken.None); }, "bad digest rejected");
            using (var client = new UpdateClient(new FakeHandler((r,t) => Bytes(new byte[1]))))
                await RejectAsync(async () => { await client.DownloadAsync(release,root,CancellationToken.None); }, "wrong declared size rejected");
            using (var client = new UpdateClient(new FakeHandler((r,t) => Redirect("http://example.invalid/fake.vsix"))))
                await RejectAsync(async () => { await client.DownloadAsync(release,root,CancellationToken.None); }, "unsafe redirect blocked");
            using (var handler = new FakeHandler((r,t) => Redirect("https://release-assets.githubusercontent.com/loop")))
            using (var client = new UpdateClient(handler))
            {
                await RejectAsync(async () => { await client.DownloadAsync(release,root,CancellationToken.None); }, "redirect loops bounded");
                Check(handler.Requests == 6, "redirect limit enforced");
            }
            using (var client = new UpdateClient(new FakeHandler((r,t) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChunkStream(new byte[payload.Length + 1], false)) })))
                await RejectAsync(async () => { await client.DownloadAsync(release,root,CancellationToken.None); }, "stream exceeding size rejected");
            using (var client = new UpdateClient(new FakeHandler((r,t) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChunkStream(new byte[1], false)) })))
                await RejectAsync(async () => { await client.DownloadAsync(release,root,CancellationToken.None); }, "truncated chunked stream rejected");
            using (var client = new UpdateClient(new FakeHandler((r,t) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChunkStream(payload, true)) })))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(30);
                bool canceled = false;
                try { await client.DownloadAsync(release,root,cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "closing/canceling interrupts an active download");
            }
            Check(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length == before, "failed and canceled downloads leave no files");
            Check(Directory.GetDirectories(root).Length == 2, "failed per-download directories removed");
        }
        finally { Directory.Delete(root,true); }
        Console.WriteLine("PASS: " + count + " updater checks (synthetic HTTP, no real network or installer).");
    }

    internal static async Task Live()
    {
        string root = Path.Combine(Path.GetTempPath(), "JoinAssist-LiveUpdateTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var client = new UpdateClient())
            using (var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                var release = await client.CheckAsync(cancel.Token);
                if (release == null) throw new Exception("No public release found.");
                string file = await client.DownloadAsync(release,root,cancel.Token);
                if (!File.Exists(file) || new FileInfo(file).Length != release.Size) throw new Exception("Incomplete live download.");
                Console.WriteLine("PASS: live GitHub API + HTTPS redirect + download + SHA-256: " + release.Tag + ". No installer launched.");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }

    sealed class InlineProgress : IProgress<int>
    { internal readonly List<int> Values = new List<int>(); public void Report(int value) { Values.Add(value); } }
    sealed class FakeHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> reply;
        internal int Requests; internal Uri LastUri; internal bool SentAuthorization;
        internal FakeHandler(Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> response) { reply = response; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Requests++; LastUri = request.RequestUri; SentAuthorization |= request.Headers.Authorization != null; return Task.FromResult(reply(request,token)); }
    }
    sealed class ChunkStream : Stream
    {
        readonly byte[] bytes; readonly bool slow; int offset;
        internal ChunkStream(byte[] data, bool wait) { bytes = data; slow = wait; }
        public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { return offset; } set { throw new NotSupportedException(); } }
        public override int Read(byte[] buffer,int start,int length) { int n = Math.Min(length,bytes.Length-offset); Array.Copy(bytes,offset,buffer,start,n); offset+=n; return n; }
        public override async Task<int> ReadAsync(byte[] buffer,int start,int length,CancellationToken token)
        { if(slow) await Task.Delay(Timeout.Infinite,token); token.ThrowIfCancellationRequested(); return Read(buffer,start,length); }
        public override void Flush() { } public override long Seek(long n,SeekOrigin o) { throw new NotSupportedException(); }
        public override void SetLength(long n) { throw new NotSupportedException(); } public override void Write(byte[] b,int o,int n) { throw new NotSupportedException(); }
    }
}
