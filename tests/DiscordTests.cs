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
    private const string Secret = "FAKE_TEST_TOKEN_NEVER_REAL";
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    private static Task<HttpResponseMessage> Reply(int status, string body)
    { return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) }); }
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
        foreach (string kind in new[] { "boss", "loot", "death", "manual" })
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
            return new HttpResponseMessage(HttpStatusCode.OK);
        }};
        var result = DiscordWebhook.UploadAsync(file, Options(), CancellationToken.None, handler).GetAwaiter().GetResult();
        Check(result.Success && File.Exists(file), "Upload retains local copy");
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
        options = Options(); options.SaveLocalCopy = false;
        result = DiscordWebhook.UploadAsync(file, options, CancellationToken.None, new FakeDiscord { Respond = (n, req) => Reply(200, "") }).GetAwaiter().GetResult();
        Check(result.Success && !File.Exists(file), "Delete only after confirmed upload");
        Console.WriteLine("PASS: " + checks + " Discord assertions, fake transport only; no network messages sent.");
    }
}
