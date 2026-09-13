using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ValheimMoments;

internal sealed class FakeDiscord : HttpMessageHandler
{
    internal int Calls;
    internal Func<int, HttpRequestMessage, Task<HttpResponseMessage>> Respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Respond(++Calls, request);
    }
}
internal static class DiscordTests
{
    private static int checks;
    private const string Receipt = "{\"id\":\"12345\",\"channel_id\":\"67890\",\"guild_id\":\"98765\",\"attachments\":[{\"filename\":\"valheim-moment.webp\",\"size\":4}]}";
    private const string Secret = "FAKE_TEST_TOKEN_NEVER_REAL";
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    private static Task<HttpResponseMessage> Reply(int status, string body)
    { return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(status == 200 ? Receipt : body) }); }
    private static DiscordOptions Options() { return new DiscordOptions { WebhookUrl = "https://discord.com/api/webhooks/123/" + Secret, Username = "Test \"name\"", Message = "Ragnar died!", SaveLocalCopy = true }; }
    public static void Main(string[] args)
    {
        var session = new object();
        Check(DiscordRouting.CanSubmit(session, true, session, true), "Local host can submit own session clip");
        Check(!DiscordRouting.CanSubmit(session, false, session, false), "Remote client cannot use local Discord config");
        Check(!DiscordRouting.CanSubmit(session, false, session, true), "Client capture cannot gain host authority later");
        Check(!DiscordRouting.CanSubmit(session, true, new object(), true), "Host world switch rejects old clip");
        Check(!DiscordRouting.CanSubmit(session, true, null, false), "Disconnected session rejects upload");
        Check(!DiscordRouting.CanSubmit(null, true, null, true), "Menu captures have no submission authority");
        Check(!DiscordRouting.CanSubmit(session, true, session, false), "Loss of host role cancels authority");
        Check(DiscordRouting.Destination("discovery", "default", false, "", false, "", false, "", "discoveries") == "discoveries", "Discovery override");
        Check(DiscordRouting.Destination("discovery", "default", false, "", false, "", false, "", " ") == "default", "Blank discovery route falls back");
        Check(DiscordRouting.Destination("discovery", "default", false, "", false, "", false, "", "invalid") == "invalid", "Invalid nonblank discovery destination never falls back");
        Check(DiscordRouting.Destination("special", "default", true, "boss", true, "loot", true, "death", "discoveries") == "default", "Special enemies use default route");
        foreach (string kind in new[] { "boss", "loot", "death", "manual", "discovery", "special" })
            Check(DiscordRouting.Destination(kind, "default", false, "boss", false, "loot", false, "death") == "default", "Disabled overrides use default: " + kind);
        Check(DiscordRouting.Destination("boss", "default", true, "boss", true, "loot", true, "death") == "boss", "Boss routing");
        Check(DiscordRouting.Destination("loot", "default", true, "boss", true, "loot", true, "death") == "loot", "Good loot routing");
        Check(DiscordRouting.Destination("death", "default", true, "boss", true, "loot", true, "death") == "death", "Player death routing");
        Check(DiscordRouting.Destination("manual", "default", true, "boss", true, "loot", true, "death") == "default", "Manual always uses default");
        Check(DiscordRouting.Destination("boss", "default", true, "", false, "loot", false, "death") == "", "Enabled empty override never silently reroutes");
        string file = Path.Combine(args[0], "discord-test.webp");
        File.WriteAllBytes(file, new byte[] { 82, 73, 70, 70 });
        Uri endpoint;
        Check(!DiscordWebhook.TryEndpoint("http://discord.com/api/webhooks/123/token", out endpoint), "Reject HTTP");
        Check(!DiscordWebhook.TryEndpoint("https://discord.com.evil.invalid/api/webhooks/123/token", out endpoint), "Reject other hosts");
        Check(!DiscordWebhook.TryEndpoint("https://discord.com/api/webhooks/123/token?redirect=evil", out endpoint), "Reject query injection");
        Check(DiscordWebhook.TryEndpoint(Options().WebhookUrl + "?thread_id=42", out endpoint) && endpoint.Query.Contains("wait=true"), "Thread support and confirmation");
        var handler = new FakeDiscord { Respond = async (n, request) => {
            string body = await request.Content.ReadAsStringAsync();
            Check(body.Contains("files[0]") && body.Contains("image/webp"), "Attachment form");
            Check(body.Contains("payload_json") && body.Contains("allowed_mentions") && body.Contains("parse"), "JSON and mention suppression");
            Check(!body.Contains(Secret), "No token in payload");
            Check(body.Contains("Ragnar died!"), "Event message included");
            return await Reply(200, "");
        }};
        var result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(result.Success && File.Exists(file), "Upload retains local copy");
        Check(result.MessageId == "12345" && result.ChannelId == "67890" && result.GuildId == "98765", "Receipt IDs retained");
        var mentioning = Options(); mentioning.Message = "Recorded by: Mec (<@123456789012345678>)";
        mentioning.MentionUsers = new[] { "123456789012345678" };
        handler = new FakeDiscord { Respond = async (n, request) => {
            string body = await request.Content.ReadAsStringAsync();
            Check(body.Contains("\"parse\":[]") && body.Contains("\"users\":[\"123456789012345678\"]"), "Explicit user allowlist while role/everyone parsing stays off");
            return await Reply(200, "");
        }};
        Check(DiscordWebhook.UploadAsync(file, mentioning, CancellationToken.None, handler).GetAwaiter().GetResult().Success, "Mention payload uses normal confirmed upload path");
        foreach (string badReceipt in new[] { "", "{}", "not-json", Receipt.Replace("12345", "secret/path"), Receipt.Replace("size\":4", "size\":5"), Receipt.Replace("valheim-moment.webp", "other.webp"), new string('x', 65537) })
        {
            var malformed = badReceipt;
            handler = new FakeDiscord { Respond = (n, req) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(malformed) }) };
            var remove = Options(); remove.SaveLocalCopy = false;
            result = DiscordWebhook.UploadAsync(file, remove, CancellationToken.None, handler).GetAwaiter().GetResult();
            Check(!result.Success && result.DeliveryUnknown && File.Exists(file) && handler.Calls == 1, "Unconfirmed receipt retains clip without retry");
        }
        string second = Path.Combine(args[0], "discord-second.webp");
        File.WriteAllBytes(second, new byte[] { 1, 2, 3, 4 });
        var perspectives = new[] { new DiscordClip(file, "Alice", true), new DiscordClip(second, "Bjorn", false) };
        const string groupReceipt = "{\"id\":\"12345\",\"channel_id\":\"67890\",\"guild_id\":\"98765\",\"attachments\":[{\"filename\":\"valheim-moment-2.webp\",\"size\":4},{\"filename\":\"valheim-moment-1.webp\",\"size\":4}]}";
        handler = new FakeDiscord { Respond = async (n, req) => {
            string body = await req.Content.ReadAsStringAsync();
            Check(body.Contains("files[0]") && body.Contains("files[1]") && body.Contains("Recorded by: Alice") && body.Contains("Recorded by: Bjorn"), "Multiple labeled perspectives");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(groupReceipt) };
        }};
        result = DiscordWebhook.UploadManyAsync(perspectives, Options(), 7, CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(!result.Success && handler.Calls == 0 && File.Exists(second), "Aggregate budget rejects before network");
        var perFile = Options(); perFile.MaxUploadBytes = 3;
        result = DiscordWebhook.UploadManyAsync(perspectives, perFile, 8, CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(!result.Success && handler.Calls == 0, "Per-file budget independent of aggregate");
        result = DiscordWebhook.UploadManyAsync(new[] { perspectives[0], perspectives[0] }, Options(), 8, CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(!result.Success && handler.Calls == 0, "Duplicate file rejected");
        result = DiscordWebhook.UploadManyAsync(perspectives, Options(), 8, CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(result.Success && File.Exists(file) && !File.Exists(second) && handler.Calls == 1, "One grouped post with independent retention and unordered receipt");
        Check(result.MessageLink == "https://discord.com/channels/98765/67890/12345", "Stable message link without attachment URL or webhook token");
        File.WriteAllBytes(second, new byte[] { 1, 2, 3, 4 });
        foreach (string incomplete in new[] { groupReceipt.Replace("valheim-moment-2.webp", "valheim-moment-1.webp"), Receipt })
        {
            var body = incomplete;
            handler = new FakeDiscord { Respond = (n, req) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }) };
            result = DiscordWebhook.UploadManyAsync(perspectives, Options(), 8, CancellationToken.None, handler).GetAwaiter().GetResult();
            Check(!result.Success && result.DeliveryUnknown && File.Exists(second) && handler.Calls == 1, "Incomplete group never acknowledges or removes perspectives");
        }
        File.Delete(second);
        foreach (var invalid in new[] { new DiscordClip[0], new DiscordClip[] { null },
            new[] { perspectives[0], perspectives[0], perspectives[0], perspectives[0] },
            new[] { perspectives[0], new DiscordClip(file, "Alice\nforged", false) } })
        {
            handler = new FakeDiscord { Respond = (n, req) => Reply(200, "") };
            result = DiscordWebhook.UploadManyAsync(invalid, Options(), long.MaxValue, CancellationToken.None, handler).GetAwaiter().GetResult();
            Check(!result.Success && !result.DeliveryUnknown && handler.Calls == 0, "Invalid group rejected before submission");
        }
        foreach (int unknown in new[] { 204, 500, 502 })
        {
            handler = new FakeDiscord { Respond = (n, req) => Reply(unknown, "") };
            result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
            Check(result.DeliveryUnknown && !result.Success && handler.Calls == 1 && File.Exists(file), "Ambiguous response never blindly retries");
        }
        handler = new FakeDiscord { Respond = (n, req) => Reply(n == 1 ? 429 : 200, "{\"retry_after\":0.001}") };
        result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(result.Success && handler.Calls == 2, "429 retries");
        handler = new FakeDiscord { Respond = (n, req) => Reply(429, "{\"retry_after\":0.001}") };
        result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(!result.Success && handler.Calls == 3, "Bounded retries");
        foreach (string body in new[] { "{\"retry_after\":120}", "{}", "not-json" })
        {
            handler = new FakeDiscord { Respond = (n, req) => Reply(429, body) };
            result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
            Check(!result.Success && handler.Calls == 1, "Invalid/excessive rate wait fails safely");
        }
        handler = new FakeDiscord { Respond = (n, req) => { throw new TaskCanceledException(Secret); } };
        result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(!result.Success && result.Message.Contains("timed out") && !result.Message.Contains(Secret), "Timeout redacted");
        foreach (int status in new[] { 302, 400, 401, 403, 404, 413, 500 })
        {
            handler = new FakeDiscord { Respond = (n, req) => Reply(status, Secret) };
            result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
            Check(!result.Success && !result.Message.Contains(Secret) && File.Exists(file) && handler.Calls == 1, "Safe HTTP failure " + status);
        }
        handler = new FakeDiscord { Respond = (n, req) => { throw new HttpRequestException(Secret); } };
        result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(!result.Success && !result.Message.Contains(Secret), "Network exception redacted");
        var options = Options(); options.MaxUploadBytes = 1;
        handler = new FakeDiscord { Respond = (n, req) => Reply(200, "") };
        result = DiscordWebhook.UploadAsync(file, options, CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(!result.Success && handler.Calls == 0, "Size preflight");
        options = Options(); options.WebhookUrl = "";
        result = DiscordWebhook.UploadAsync(file, options, CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(!result.Success && handler.Calls == 0, "Missing webhook offline operation");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            result = DiscordWebhook.UploadAsync(file, Options(), cancel.Token, new FakeDiscord { Respond = (n, req) => Reply(200, "") }).GetAwaiter().GetResult();
            Check(!result.Success && File.Exists(file), "Cancellation retains clip");
        }
        string noGuild = Receipt.Replace(",\"guild_id\":\"98765\"", "");
        // Discord fields arrive in arbitrary order, including nested unknown objects,
        // escaped Unicode, nullable timestamps and non-integer unknown numbers.
        string rich = "{\"type\":0,\"content\":\"Ragn\\u00e1r \\\"hello\\\"\\n\",\"author\":{\"bot\":true,\"roles\":[],\"extra\":null},\"future\":-1.25e+3," + Receipt.Substring(1);
        handler = new FakeDiscord { Respond = (n, req) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(rich) }) };
        result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(result.Success && result.MessageLink != null, "Realistic extensible Discord message parses without reflection");
        foreach (string malformed in new[] { Receipt + "x", Receipt.Replace("\"size\":4", "\"size\":4.5"), Receipt.Replace("\"id\":\"12345\"", "\"id\":\"12345\",\"id\":\"999\""), Receipt.Replace("\"size\":4", "\"size\":9223372036854775808"), rich.Replace("true", "trueX") })
        {
            handler = new FakeDiscord { Respond = (n, req) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(malformed) }) };
            result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
            Check(!result.Success && result.DeliveryUnknown && handler.Calls == 1 && result.Message.Contains("receipt parsing"), "Malformed confirmation never retries POST and names safe stage");
        }
        handler = new FakeDiscord { Respond = (n, req) => {
            Check(n == 1 && req.Method == HttpMethod.Post, "Removed link feature performs no metadata GET");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(noGuild) });
        } };
        result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(result.Success && handler.Calls == 1, "Upload still confirms without a guild ID or link lookup");
        options = Options(); options.SaveLocalCopy = false;
        result = DiscordWebhook.UploadAsync(file, options, CancellationToken.None, new FakeDiscord { Respond = (n, req) => Reply(200, "") }).GetAwaiter().GetResult();
        Check(result.Success && !File.Exists(file), "Delete only after confirmed upload");
        Console.WriteLine("PASS: " + checks + " Discord assertions, fake transport only; no network messages sent.");
    }
}
