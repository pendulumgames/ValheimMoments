using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ValheimMoments
{
    internal sealed class DiscordOptions
    {
        internal string WebhookUrl, Username;
        internal string Message = "Valheim moment";
        internal bool SaveLocalCopy;
        internal long MaxUploadBytes = 10L * 1024 * 1024;
    }

    internal sealed class UploadResult
    {
        internal bool Success;
        internal string Message;
        internal static UploadResult Fail(string message) { return new UploadResult { Message = message + " Local clip retained." }; }
    }

    internal static class DiscordWebhook
    {
        [DataContract] private sealed class Payload
        {
            [DataMember] public string username;
            [DataMember] public string content;
            [DataMember] public Mentions allowed_mentions = new Mentions();
        }
        [DataContract] private sealed class Mentions { [DataMember] public string[] parse = new string[0]; }
        [DataContract] private sealed class RateLimit { [DataMember(IsRequired = true)] public double retry_after { get; set; } }

        internal static bool TryEndpoint(string value, out Uri endpoint)
        {
            endpoint = null;
            Uri parsed;
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out parsed)) return false;
            if (parsed.Scheme != "https" || parsed.Port != 443 || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0) return false;
            if (parsed.Host != "discord.com" && parsed.Host != "discordapp.com" && parsed.Host != "canary.discord.com" && parsed.Host != "ptb.discord.com") return false;
            if (!Regex.IsMatch(parsed.AbsolutePath, @"^/api/(?:v[0-9]+/)?webhooks/[0-9]+/[A-Za-z0-9_-]+$")) return false;
            if (parsed.Query.Length != 0 && !Regex.IsMatch(parsed.Query, @"^\?thread_id=[0-9]+$")) return false;
            endpoint = new Uri(parsed.GetLeftPart(UriPartial.Path) + (parsed.Query.Length == 0 ? "?" : parsed.Query + "&") + "wait=true");
            return true;
        }

        internal static async Task<UploadResult> UploadAsync(string file, DiscordOptions options, CancellationToken stop,
            HttpMessageHandler testHandler = null)
        {
            // This boundary never returns exception text, response bodies or request URIs.
            try
            {
                Uri endpoint;
                if (!TryEndpoint(options.WebhookUrl, out endpoint)) return UploadResult.Fail("Missing or invalid Discord webhook configuration.");
                if (string.IsNullOrWhiteSpace(options.Username) || options.Username.Length > 80)
                    return UploadResult.Fail("Discord username must contain 1–80 characters.");
                long size = new FileInfo(file).Length;
                if (size == 0) return UploadResult.Fail("Clip is empty.");
                if (options.MaxUploadBytes < 1 || size > options.MaxUploadBytes)
                    return UploadResult.Fail("Clip exceeds the configured Discord upload limit (" + size + " bytes).");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                using (var client = new HttpClient(testHandler ?? new HttpClientHandler { AllowAutoRedirect = false }))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(75));
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("ValheimMoments/0.12.0");
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        using (var multipart = new MultipartFormDataContent())
                        using (var stream = File.OpenRead(file))
                        {
                            using (var json = new MemoryStream())
                            {
                                new DataContractJsonSerializer(typeof(Payload)).WriteObject(json, new Payload { username = options.Username, content = options.Message });
                                var data = new ByteArrayContent(json.ToArray());
                                data.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                                multipart.Add(data, "payload_json");
                            }
                            var attachment = new StreamContent(stream);
                            attachment.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
                            multipart.Add(attachment, "files[0]", "valheim-moment.webp");
                            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = multipart })
                            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            {
                                int status = (int)response.StatusCode;
                                if (status == 200)
                                {
                                    stream.Dispose(); // Release the Windows file handle before optional deletion.
                                    if (!options.SaveLocalCopy)
                                    {
                                        try { File.Delete(file); }
                                        catch { return new UploadResult { Success = true, Message = "Uploaded to Discord; local copy could not be removed." }; }
                                    }
                                    return new UploadResult { Success = true, Message = options.SaveLocalCopy ? "Uploaded to Discord; local copy retained." : "Uploaded to Discord; local copy removed." };
                                }
                                if (status == 429)
                                {
                                    double delay = await RetrySeconds(response, timeout.Token).ConfigureAwait(false);
                                    if (attempt == 2 || double.IsNaN(delay) || double.IsInfinity(delay) || delay < 0 || delay > 30)
                                        return UploadResult.Fail("Discord rate limited the upload; retry budget exhausted or wait exceeds 30 seconds.");
                                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.05, delay)), timeout.Token).ConfigureAwait(false);
                                    continue;
                                }
                                if (status == 413) return UploadResult.Fail("Discord rejected the attachment as too large.");
                                if (status == 401 || status == 403 || status == 404) return UploadResult.Fail("Discord webhook is invalid, deleted, or not permitted.");
                                return UploadResult.Fail("Discord rejected the upload (HTTP " + status + ").");
                            }
                        }
                    }
                }
                return UploadResult.Fail("Discord upload did not complete.");
            }
            catch (OperationCanceledException) { return UploadResult.Fail(stop.IsCancellationRequested ? "Discord upload cancelled." : "Discord upload timed out."); }
            catch { return UploadResult.Fail("Discord upload failed (network, file access, or service error)."); }
        }

        private static async Task<double> RetrySeconds(HttpResponseMessage response, CancellationToken token)
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue) return response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                if (response.Headers.RetryAfter.Date.HasValue) return Math.Max(0, (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
            }
            using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var json = new MemoryStream())
            {
                var bytes = new byte[1024];
                int n;
                while ((n = await source.ReadAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (json.Length + n > 8192) return double.NaN;
                    json.Write(bytes, 0, n);
                }
                json.Position = 0;
                var rate = (RateLimit)new DataContractJsonSerializer(typeof(RateLimit)).ReadObject(json);
                return rate.retry_after;
            }
        }
    }
}
