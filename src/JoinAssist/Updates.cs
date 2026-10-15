using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace JoinAssist
{
    internal sealed class UpdateRelease
    {
        internal Version Version { get; private set; }
        internal string Tag { get; private set; }
        internal string Notes { get; private set; }
        internal string FileName { get; private set; }
        internal string Sha256 { get; private set; }
        internal Uri Page { get; private set; }
        internal Uri Download { get; private set; }
        internal long Size { get; private set; }

        internal static UpdateRelease Parse(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = UpdateClient.MaxMetadataBytes, RecursionLimit = 32 };
            var data = serializer.DeserializeObject(json) as IDictionary<string,object>;
            if (data == null) throw new InvalidOperationException("La risposta di GitHub non contiene una release valida.");
            object draft, preview;
            if (!data.TryGetValue("draft", out draft) || !(draft is bool) || (bool)draft ||
                !data.TryGetValue("prerelease", out preview) || !(preview is bool) || (bool)preview)
                throw new InvalidOperationException("La release non è una versione pubblica ordinaria.");
            string tag = Text(data, "tag_name");
            var version = ParseVersion(tag);
            string page = UpdateClient.Repository + "/releases/tag/" + tag;
            if (!string.Equals(Text(data, "html_url"), page, StringComparison.Ordinal))
                throw new InvalidOperationException("La release non appartiene al repository JoinAssist previsto.");
            string file = "JoinAssist-" + tag.TrimStart('v') + ".vsix";
            object assetsValue;
            if (!data.TryGetValue("assets", out assetsValue) || !(assetsValue is object[]))
                throw new InvalidOperationException("La release non contiene il pacchetto VSIX.");
            IDictionary<string,object> selected = null;
            foreach (object item in (object[])assetsValue)
            {
                var asset = item as IDictionary<string,object>;
                if (asset == null || Text(asset, "name") != file) continue;
                if (selected != null) throw new InvalidOperationException("La release contiene pacchetti duplicati.");
                selected = asset;
            }
            if (selected == null) throw new InvalidOperationException("Il pacchetto " + file + " non è presente nella release.");
            string url = UpdateClient.Repository + "/releases/download/" + tag + "/" + file;
            string digest = Text(selected, "digest");
            long size;
            object sizeValue;
            if (Text(selected, "browser_download_url") != url || Text(selected, "state") != "uploaded")
                throw new InvalidOperationException("Il pacchetto non è disponibile nel repository previsto.");
            if (digest == null || !Regex.IsMatch(digest, "\\Asha256:[a-fA-F0-9]{64}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("GitHub non fornisce un checksum SHA-256 valido per il pacchetto. Apri la pagina della release.");
            if (!selected.TryGetValue("size", out sizeValue) ||
                !long.TryParse(Convert.ToString(sizeValue, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out size) ||
                size <= 0 || size > UpdateClient.MaxPackageBytes)
                throw new InvalidOperationException("La dimensione del pacchetto non è valida o supera il limite di 25 MB.");
            return new UpdateRelease { Version = version, Tag = tag, Notes = Text(data, "body") ?? "Nessuna nota disponibile.",
                FileName = file, Sha256 = digest.Substring(7).ToLowerInvariant(), Size = size, Page = new Uri(page), Download = new Uri(url) };
        }

        static string Text(IDictionary<string,object> data, string name)
        { object value; return data.TryGetValue(name, out value) ? value as string : null; }

        internal static Version ParseVersion(string tag)
        {
            if (tag == null || !Regex.IsMatch(tag, "\\Av?[0-9]+\\.[0-9]+\\.[0-9]+(?:\\.[0-9]+)?\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("La release usa una versione non supportata.");
            Version value;
            if (!System.Version.TryParse(tag.TrimStart('v'), out value)) throw new InvalidOperationException("Versione non valida.");
            return new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
        }
    }

    internal sealed class UpdateClient : IDisposable
    {
        internal const string Repository = "https://github.com/DragonPalowski/JoinAssist";
        internal const string LatestApi = "https://api.github.com/repos/DragonPalowski/JoinAssist/releases/latest";
        internal const int MaxMetadataBytes = 2 * 1024 * 1024;
        internal const long MaxPackageBytes = 25 * 1024 * 1024;
        readonly HttpClient client;

        internal static Version InstalledVersion { get { return typeof(UpdateClient).Assembly.GetName().Version; } }
        internal static string DisplayVersion(Version version)
        { return version.Revision > 0 ? version.ToString(4) : version.ToString(3); }

        // Set TLS on this handler only; changing ServicePointManager would affect SSMS itself.
        internal UpdateClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false, SslProtocols = SslProtocols.Tls12 }) { }
        internal UpdateClient(HttpMessageHandler handler)
        {
            client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("JoinAssist/" + DisplayVersion(InstalledVersion));
        }

        internal async Task<UpdateRelease> CheckAsync(CancellationToken cancel)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using (var request = new HttpRequestMessage(HttpMethod.Get, LatestApi))
                {
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound) return null;
                        if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
                            throw new InvalidOperationException("GitHub ha limitato le richieste. Riprova più tardi o apri la pagina delle release.");
                        response.EnsureSuccessStatusCode();
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new MemoryStream())
                        {
                            await CopyAsync(input, output, MaxMetadataBytes, timeout.Token, null).ConfigureAwait(false);
                            return UpdateRelease.Parse(Encoding.UTF8.GetString(output.ToArray()));
                        }
                    }
                }
            }
        }

        internal async Task<string> DownloadAsync(UpdateRelease release, string root, CancellationToken cancel, IProgress<int> progress = null)
        {
            if (release == null) throw new ArgumentNullException("release");
            // A unique directory prevents collisions between SSMS query tabs and previous downloads.
            string folder = Path.Combine(root, release.Tag + "-" + Guid.NewGuid().ToString("N"));
            string partial = Path.Combine(folder, release.FileName + ".partial");
            string complete = Path.Combine(folder, release.FileName);
            Directory.CreateDirectory(folder);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));
                    using (var response = await OpenDownloadAsync(release.Download, timeout.Token).ConfigureAwait(false))
                    {
                        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != release.Size)
                            throw new InvalidOperationException("La dimensione ricevuta non corrisponde alla release. Nessun installer è stato aperto.");
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            long length = await CopyAsync(input, output, release.Size, timeout.Token, progress).ConfigureAwait(false);
                            if (length != release.Size) throw new InvalidOperationException("Download incompleto. Riprova.");
                        }
                    }
                    timeout.Token.ThrowIfCancellationRequested();
                    string digest;
                    using (var sha = SHA256.Create())
                    using (var file = File.OpenRead(partial)) digest = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(digest, release.Sha256, StringComparison.Ordinal))
                        throw new InvalidOperationException("Il checksum SHA-256 non corrisponde. Il download è stato scartato: nessun installer è stato aperto.");
                    timeout.Token.ThrowIfCancellationRequested();
                    File.Move(partial, complete);
                    return complete;
                }
            }
            finally
            {
                // Only our own incomplete file and empty per-download directory are removed.
                try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { if (!File.Exists(complete)) Directory.Delete(folder, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        async Task<HttpResponseMessage> OpenDownloadAsync(Uri uri, CancellationToken cancel)
        {
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                if (!AllowedDownload(uri)) throw new InvalidOperationException("GitHub ha indicato una destinazione di download non consentita.");
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                    int status = (int)response.StatusCode;
                    if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                    {
                        var next = response.Headers.Location;
                        response.Dispose();
                        if (next == null) throw new InvalidOperationException("Redirect di download privo di destinazione.");
                        uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                        continue;
                    }
                    try { response.EnsureSuccessStatusCode(); return response; }
                    catch { response.Dispose(); throw; }
                }
            }
            throw new InvalidOperationException("Troppi reindirizzamenti durante il download.");
        }

        internal static bool AllowedDownload(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
                (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                    uri.AbsolutePath.StartsWith("/DragonPalowski/JoinAssist/releases/download/", StringComparison.Ordinal) ||
                 uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
        }

        static async Task<long> CopyAsync(Stream input, Stream output, long limit, CancellationToken cancel, IProgress<int> progress)
        {
            var buffer = new byte[81920];
            long total = 0; int read, previous = -1;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > limit) throw new InvalidOperationException("La risposta supera la dimensione prevista. Download annullato.");
                await output.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                int percent = (int)(total * 100 / limit);
                if (progress != null && percent != previous) { progress.Report(percent); previous = percent; }
            }
            return total;
        }

        public void Dispose() { client.Dispose(); }
    }
}
