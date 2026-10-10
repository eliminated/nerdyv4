using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Nerdy.App.Services.Auth;

/// <summary>
/// Native-app sign-in with Microsoft/Google (RFC 8252): opens the system browser at the API's external sign-in
/// endpoint and receives the one-time code on a short-lived loopback listener. The code is bound to a PKCE-style
/// verifier that never leaves this process, so an intercepted code is useless.
/// </summary>
public static class LoopbackSignIn
{
    public sealed record Result(string Code, string CodeVerifier, string RedirectUri);

    public static async Task<Result> RunAsync(Func<string, string, string, Uri> buildStartUri, CancellationToken ct)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://127.0.0.1:{port}/callback";
            await Windows.System.Launcher.LaunchUriAsync(buildStartUri(redirectUri, state, challenge));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                var target = await ReadRequestTargetAsync(stream, timeout.Token);
                if (target is null || !target.StartsWith("/callback", StringComparison.Ordinal))
                {
                    await RespondAsync(stream, 404, "Not found", "");
                    continue; // e.g. the browser asking for /favicon.ico
                }

                var query = HttpUtility.ParseQueryString(new Uri("http://127.0.0.1" + target).Query);
                if (query["state"] != state)
                {
                    await RespondAsync(stream, 400, "Sign-in failed", "This sign-in link doesn't match the one Nerdy started. Please try again from the app.");
                    throw new InvalidOperationException("The sign-in response didn't match this request.");
                }
                if (query["error"] is { } error)
                {
                    await RespondAsync(stream, 200, "Sign-in cancelled", "You can close this tab and return to Nerdy.");
                    throw new InvalidOperationException(error == "access_denied" ? "Sign-in was cancelled." : $"Sign-in failed ({error}).");
                }
                if (query["code"] is not { Length: > 0 } code)
                {
                    await RespondAsync(stream, 400, "Sign-in failed", "Nerdy didn't receive a sign-in code. Please try again.");
                    throw new InvalidOperationException("No sign-in code was returned.");
                }

                await RespondAsync(stream, 200, "You're signed in", "You can close this tab and return to Nerdy.");
                return new Result(code, verifier, redirectUri);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Sign-in timed out. Please try again.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) break;
            total += read;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
        }
        var firstLine = Encoding.ASCII.GetString(buffer, 0, total).Split("\r\n")[0].Split(' ');
        return firstLine.Length >= 2 && firstLine[0] == "GET" ? firstLine[1] : null;
    }

    private static async Task RespondAsync(NetworkStream stream, int status, string title, string message)
    {
        var html = $$$"""
            <!doctype html><html><head><meta charset="utf-8"><title>{{{title}}} · Nerdy</title>
            <style>body{font-family:'Segoe UI Variable','Segoe UI',sans-serif;background:#F3F3F7;color:#1B1B1F;display:grid;place-items:center;height:100vh;margin:0}
            .c{background:#fff;border-radius:14px;padding:32px 36px;box-shadow:0 16px 40px rgba(30,30,70,.12);max-width:380px;text-align:center}
            .b{width:44px;height:44px;border-radius:12px;background:#5B5BD6;margin:0 auto 14px}
            h1{font-size:20px;margin:0 0 6px}p{color:#5F5F6B;margin:0}
            @media (prefers-color-scheme:dark){body{background:#1C1C1F;color:#F2F2F5}.c{background:#2A2A2E}p{color:#A9A9B5}}</style></head>
            <body><div class="c"><div class="b"></div><h1>{{{WebUtility.HtmlEncode(title)}}}</h1><p>{{{WebUtility.HtmlEncode(message)}}}</p></div></body></html>
            """;
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
