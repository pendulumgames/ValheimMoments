using System;
using System.Collections.Generic;
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
    internal sealed class DiscordClip
    {
        internal readonly string File, Recorder;
        internal readonly bool Keep;
        internal DiscordClip(string file, string recorder, bool keep) { File = file; Recorder = recorder; Keep = keep; }
    }
    internal sealed class DiscordOptions
    {
        internal string WebhookUrl, Username;
        internal string[] MentionUsers = new string[0];
        internal string Message = "Valheim moment";
        internal bool SaveLocalCopy;
        internal long MaxUploadBytes = 10L * 1024 * 1024;
    }

    internal sealed class UploadResult
    {
        internal bool Success;
        internal string Message;
        internal string MessageId, ChannelId, GuildId;
        internal bool DeliveryUnknown;
        internal string MessageLink { get { return MessageId == null || GuildId == null ? null : "https://discord.com/channels/" + GuildId + "/" + ChannelId + "/" + MessageId; } }
        internal static UploadResult Fail(string message) { return new UploadResult { Message = message + " Local clip retained." }; }
        internal static UploadResult Unknown(string message)
        {
            var result = Fail(message + " Delivery is unknown; check Discord before retrying.");
            result.DeliveryUnknown = true;
            return result;
        }
    }

    internal static class DiscordWebhook
    {
        [DataContract] private sealed class Payload
        {
            [DataMember] public string username;
            [DataMember] public string content;
            [DataMember] public Mentions allowed_mentions = new Mentions();
            [DataMember] public AttachmentMetadata[] attachments;
        }
        [DataContract] private sealed class AttachmentMetadata
        {
            [DataMember] public int id;
            [DataMember] public string filename;
            [DataMember] public string description;
        }
        [DataContract] private sealed class Mentions { [DataMember] public string[] parse = new string[0]; [DataMember] public string[] users = new string[0]; }
        [DataContract] private sealed class RateLimit { [DataMember(IsRequired = true)] public double retry_after { get; set; } }
        [DataContract] private sealed class Receipt
        {
            [DataMember(IsRequired = true)] public string id { get; set; }
            [DataMember(IsRequired = true)] public string channel_id { get; set; }
            [DataMember] public string guild_id { get; set; }
            [DataMember(IsRequired = true)] public ReceiptAttachment[] attachments { get; set; }
        }
        [DataContract] private sealed class ReceiptAttachment
        {
            [DataMember(IsRequired = true)] public string filename { get; set; }
            [DataMember(IsRequired = true)] public long size { get; set; }
        }
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

        internal static Task<UploadResult> UploadAsync(string file, DiscordOptions options, CancellationToken stop,
            HttpMessageHandler testHandler = null)
        {
            if (options == null) return Task.FromResult(UploadResult.Fail("Missing Discord configuration."));
            return UploadManyAsync(new[] { new DiscordClip(file, null, options.SaveLocalCopy) }, options,
                options.MaxUploadBytes, stop, testHandler);
        }

        // Director supplies host-authenticated recorder labels and already-selected perspectives.
        // This transport never silently drops a clip to make a group fit.
        internal static async Task<UploadResult> UploadManyAsync(DiscordClip[] clips, DiscordOptions options, long maxPostBytes,
            CancellationToken stop, HttpMessageHandler testHandler = null)
        {
            // This boundary never returns exception text, response bodies or request URIs.
            bool submitted = false;
            string stage = "preparation";
            try
            {
                if (options == null) return UploadResult.Fail("Missing Discord configuration.");
                options = new DiscordOptions { WebhookUrl = options.WebhookUrl, Username = options.Username,
                    Message = options.Message, MaxUploadBytes = options.MaxUploadBytes, SaveLocalCopy = options.SaveLocalCopy,
                    MentionUsers = options.MentionUsers == null ? new string[0] : (string[])options.MentionUsers.Clone() };
                Uri endpoint;
                if (!TryEndpoint(options.WebhookUrl, out endpoint)) return UploadResult.Fail("Missing or invalid Discord webhook configuration.");
                if (string.IsNullOrWhiteSpace(options.Username) || options.Username.Length > 80)
                    return UploadResult.Fail("Discord username must contain 1–80 characters.");
                if (clips == null || clips.Length < 1 || clips.Length > 5 || maxPostBytes < 1 || options.MaxUploadBytes < 1)
                    return UploadResult.Fail("Invalid perspective count or upload budget.");
                clips = (DiscordClip[])clips.Clone();
                if (options.Message == null || options.Message.Length > 2000) return UploadResult.Fail("Discord message exceeds its text budget.");
                var sizes = new long[clips.Length];
                var metadata = new AttachmentMetadata[clips.Length];
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] == null || !paths.Add(Path.GetFullPath(clips[i].File))) return UploadResult.Fail("Duplicate or missing perspective.");
                    sizes[i] = new FileInfo(clips[i].File).Length;
                    if (sizes[i] < 1 || sizes[i] > options.MaxUploadBytes || sizes[i] > maxPostBytes - total)
                        return UploadResult.Fail("Clips exceed the per-file or combined Discord upload budget.");
                    total += sizes[i];
                    string recorder = clips[i].Recorder;
                    if (clips.Length > 1 && string.IsNullOrWhiteSpace(recorder)) return UploadResult.Fail("Missing perspective recorder.");
                    if (recorder != null && (recorder.Length > 128 || recorder.IndexOfAny(new[] { '\r', '\n' }) >= 0))
                        return UploadResult.Fail("Invalid perspective recorder.");
                    metadata[i] = new AttachmentMetadata { id = i,
                        filename = clips.Length == 1 ? "valheim-moment.webp" : "valheim-moment-" + (i + 1) + ".webp",
                        description = recorder == null ? "Valheim moment" : "Recorded by: " + recorder };
                }
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
                using (var client = new HttpClient(testHandler ?? new HttpClientHandler { AllowAutoRedirect = false }))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(75));
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("ValheimMoments/0.23.2");
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        using (var multipart = new MultipartFormDataContent())
                        {
                            using (var json = new MemoryStream())
                            {
                                new DataContractJsonSerializer(typeof(Payload)).WriteObject(json, new Payload { username = options.Username, content = options.Message, allowed_mentions = new Mentions { users = options.MentionUsers ?? new string[0] }, attachments = metadata });
                                var data = new ByteArrayContent(json.ToArray());
                                data.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                                multipart.Add(data, "payload_json");
                            }
                            for (int i = 0; i < clips.Length; i++)
                            {
                                var stream = File.OpenRead(clips[i].File);
                                var attachment = new StreamContent(stream);
                                attachment.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
                                multipart.Add(attachment, "files[" + i + "]", metadata[i].filename);
                                if (stream.Length != sizes[i]) return UploadResult.Fail("Clip changed before submission.");
                            }
                            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = multipart })
                            {
                              timeout.Token.ThrowIfCancellationRequested();
                              submitted = true;
                              stage = "request";
                              using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                              {
                                int status = (int)response.StatusCode;
                                if (status == 200)
                                {
                                    stage = "receipt reading";
                                    Receipt receipt;
                                    using (var json = await ReadBounded(response.Content, 65536, timeout.Token).ConfigureAwait(false))
                                    {
                                        stage = "receipt parsing";
                                        var fields = WebhookJson.Read(json);
                                        object attachments;
                                        if (!fields.TryGetValue("attachments", out attachments) || !(attachments is List<object>)) throw new InvalidDataException("Missing attachments");
                                        var parsed = new List<ReceiptAttachment>();
                                        foreach (var value in (List<object>)attachments)
                                        {
                                            var item = value as Dictionary<string, object>;
                                            if (item == null) throw new InvalidDataException("Invalid attachment");
                                            parsed.Add(new ReceiptAttachment { filename = WebhookJson.Text(item, "filename"), size = WebhookJson.Integer(item, "size") });
                                        }
                                        receipt = new Receipt { id = WebhookJson.Text(fields, "id"), channel_id = WebhookJson.Text(fields, "channel_id"), guild_id = WebhookJson.Text(fields, "guild_id"), attachments = parsed.ToArray() };
                                    }
                                    if (!ValidSnowflake(receipt.id) || !ValidSnowflake(receipt.channel_id) || !Matches(receipt.attachments, metadata, sizes))
                                        return UploadResult.Unknown("Discord returned an incomplete upload receipt.");
                                    var result = new UploadResult { Success = true, MessageId = receipt.id, ChannelId = receipt.channel_id,
                                        GuildId = ValidSnowflake(receipt.guild_id) ? receipt.guild_id : null,
                                        Message = "Uploaded to Discord; local retention settings applied." };
                                    multipart.Dispose(); // Release every Windows file handle before optional deletion.
                                    foreach (var clip in clips)
                                    {
                                        if (clip.Keep) continue;
                                        try { File.Delete(clip.File); }
                                        catch { result.Message = "Uploaded to Discord; local copy could not be removed."; }
                                    }
                                    return result;
                                }
                                if (status == 429)
                                {
                                    submitted = false;
                                    double delay = await RetrySeconds(response, timeout.Token).ConfigureAwait(false);
                                    if (attempt == 2 || double.IsNaN(delay) || double.IsInfinity(delay) || delay < 0 || delay > 30)
                                        return UploadResult.Fail("Discord rate limited the upload; retry budget exhausted or wait exceeds 30 seconds.");
                                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.05, delay)), timeout.Token).ConfigureAwait(false);
                                    continue;
                                }
                                if (status == 413) return UploadResult.Fail("Discord rejected the attachment as too large.");
                                if (status == 401 || status == 403 || status == 404) return UploadResult.Fail("Discord webhook is invalid, deleted, or not permitted.");
                                if (status >= 500 || status == 204) return UploadResult.Unknown("Discord did not confirm the uploaded attachment.");
                                return UploadResult.Fail("Discord rejected the upload (HTTP " + status + ").");
                              }
                            }
                        }
                    }
                }
                return UploadResult.Fail("Discord upload did not complete.");
            }
            catch (OperationCanceledException) { return submitted ? UploadResult.Unknown("Discord upload timed out or was cancelled after submission.") : UploadResult.Fail(stop.IsCancellationRequested ? "Discord upload cancelled." : "Discord upload timed out."); }
            catch (Exception error) { return submitted ? UploadResult.Unknown("Discord upload confirmation failed at " + stage + " (" + error.GetType().Name + ").") : UploadResult.Fail("Discord upload failed (" + error.GetType().Name + ")."); }
        }

        private static bool Matches(ReceiptAttachment[] actual, AttachmentMetadata[] expected, long[] sizes)
        {
            if (actual == null || actual.Length != expected.Length) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in actual)
            {
                if (item == null || !seen.Add(item.filename)) return false;
                int index = Array.FindIndex(expected, x => x.filename == item.filename);
                if (index < 0 || sizes[index] != item.size) return false;
            }
            return true;
        }

        private static bool ValidSnowflake(string value)
        {
            ulong id;
            return value != null && value.Length <= 20 && Regex.IsMatch(value, @"^[1-9][0-9]*$") && ulong.TryParse(value, out id);
        }

        private static async Task<MemoryStream> ReadBounded(HttpContent content, int maximum, CancellationToken token)
        {
            var result = new MemoryStream();
            try
            {
                using (var source = await content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var buffer = new byte[1024];
                    int count;
                    while ((count = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    {
                        if (result.Length + count > maximum) throw new InvalidDataException();
                        result.Write(buffer, 0, count);
                    }
                }
                result.Position = 0;
                return result;
            }
            catch { result.Dispose(); throw; }
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

    // Reflection-free response reader for Unity/Mono. Strict bounded JSON parsing;
    // unknown Discord fields remain forward-compatible without dynamic contracts.
    internal sealed class WebhookJson
    {
        private readonly string source;
        private int at, nodes;
        private sealed class Number { internal string Value; }
        private WebhookJson(string source) { this.source = source; }
        internal static Dictionary<string, object> Read(Stream stream)
        {
            string source;
            using (var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), false, 1024, true)) source = reader.ReadToEnd();
            if (source.Length > 65536) throw new InvalidDataException();
            var parser = new WebhookJson(source);
            var result = parser.Value(0) as Dictionary<string, object>; parser.Space();
            if (result == null || parser.at != source.Length) throw new InvalidDataException();
            return result;
        }
        internal static string Text(Dictionary<string, object> fields, string name)
        { object value; return fields.TryGetValue(name, out value) ? value as string : null; }
        internal static long Integer(Dictionary<string, object> fields, string name)
        {
            object value; long number;
            if (!fields.TryGetValue(name, out value) || !(value is Number) ||
                !long.TryParse(((Number)value).Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out number)) throw new InvalidDataException();
            return number;
        }
        private void Space() { while (at < source.Length && (source[at] == ' ' || source[at] == '\r' || source[at] == '\n' || source[at] == '\t')) at++; }
        private char Peek() { Space(); if (at == source.Length) throw new InvalidDataException(); return source[at]; }
        private void Eat(char expected) { if (Peek() != expected) throw new InvalidDataException(); at++; }
        private object Value(int depth)
        {
            if (++nodes > 4096 || depth > 24) throw new InvalidDataException();
            char c = Peek();
            if (c == '"') return String();
            if (c == '{')
            {
                at++; var result = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Peek() == '}') { at++; return result; }
                while (true) { string key = String(); Eat(':'); if (result.ContainsKey(key)) throw new InvalidDataException(); result.Add(key, Value(depth + 1)); c = Peek(); at++; if (c == '}') return result; if (c != ',') throw new InvalidDataException(); }
            }
            if (c == '[')
            {
                at++; var result = new List<object>(); if (Peek() == ']') { at++; return result; }
                while (true) { result.Add(Value(depth + 1)); c = Peek(); at++; if (c == ']') return result; if (c != ',') throw new InvalidDataException(); }
            }
            foreach (string literal in new[] { "true", "false", "null" })
                if (source.Length - at >= literal.Length && string.CompareOrdinal(source, at, literal, 0, literal.Length) == 0)
                { at += literal.Length; return literal == "null" ? null : (object)(literal == "true"); }
            int start = at;
            while (at < source.Length && "0123456789eE+-.".IndexOf(source[at]) >= 0) at++;
            string number = source.Substring(start, at - start);
            if (!Regex.IsMatch(number, @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$")) throw new InvalidDataException();
            return new Number { Value = number };
        }
        private string String()
        {
            Eat('"'); var result = new System.Text.StringBuilder();
            while (at < source.Length)
            {
                char c = source[at++]; if (c == '"') return result.ToString(); if (c < 32) throw new InvalidDataException();
                if (c == '\\')
                {
                    if (at == source.Length) throw new InvalidDataException(); c = source[at++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': break;
                        case 'b': c = '\b'; break; case 'f': c = '\f'; break; case 'n': c = '\n'; break; case 'r': c = '\r'; break; case 't': c = '\t'; break;
                        case 'u':
                            ushort code;
                            if (source.Length - at < 4 || !ushort.TryParse(source.Substring(at, 4), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out code)) throw new InvalidDataException();
                            at += 4; c = (char)code; break;
                        default: throw new InvalidDataException();
                    }
                }
                result.Append(c);
            }
            throw new InvalidDataException();
        }
    }
}
