using System.Text.Encodings.Web;

namespace Nerdy.Api.Features.Auth;

/// <summary>The few HTML pages the API serves: they are opened from emails or by the sign-in browser flow.</summary>
internal static class AuthPages
{
    public static IResult Message(int statusCode, string title, string message) =>
        Page(statusCode, title, $"<h1>{Encode(title)}</h1><p>{Encode(message)}</p>");

    /// <summary>
    /// Password reset form. The email and token travel in the URL fragment (<c>#email=…&amp;token=…</c>) so they never
    /// reach server or proxy logs; the script posts them to <c>POST /api/auth/reset-password</c>.
    /// </summary>
    public static IResult ResetPassword() => Page(StatusCodes.Status200OK, "Choose a new password", """
        <h1>Choose a new password</h1>
        <form id="form">
          <label>New password<input id="password" type="password" autocomplete="new-password" minlength="8" required></label>
          <label>Repeat it<input id="repeat" type="password" autocomplete="new-password" minlength="8" required></label>
          <button type="submit">Save password</button>
          <p id="status" role="status"></p>
        </form>
        <script>
          const params = new URLSearchParams(location.hash.slice(1));
          const form = document.getElementById("form"), status = document.getElementById("status");
          if (!params.get("email") || !params.get("token")) {
            form.innerHTML = "<p>This link is incomplete. Request a new one from Nerdy.</p>";
          }
          form.addEventListener("submit", async (e) => {
            e.preventDefault();
            const password = document.getElementById("password").value;
            if (password !== document.getElementById("repeat").value) { status.textContent = "The passwords don't match."; return; }
            status.textContent = "Saving…";
            const response = await fetch("reset-password", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ email: params.get("email"), token: params.get("token"), newPassword: password })
            });
            if (response.ok) {
              history.replaceState(null, "", location.pathname);
              form.innerHTML = "<p>Your password has been changed. You can close this tab and sign in to Nerdy.</p>";
              return;
            }
            const problem = await response.json().catch(() => ({}));
            const errors = problem.errors ? Object.values(problem.errors).flat() : [];
            status.textContent = errors.length ? errors.join(" ") : (problem.title || "Something went wrong. Please try again.");
          });
        </script>
        """);

    private static IResult Page(int statusCode, string title, string body) => Results.Content($$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{Encode(title)}} · Nerdy</title>
          <style>
            body { font-family: "Segoe UI Variable", "Segoe UI", system-ui, sans-serif; background: #f3f3f8; color: #1b1b1f;
                   display: grid; place-items: center; min-height: 100vh; margin: 0; }
            main { background: #fff; border-radius: 12px; padding: 32px 40px; max-width: 420px; box-shadow: 0 8px 32px #0001; }
            h1 { font-size: 22px; margin: 0 0 12px; color: #5b5bd6; }
            p { line-height: 1.5; }
            label { display: block; margin: 12px 0; font-size: 14px; }
            input { display: block; width: 100%; box-sizing: border-box; margin-top: 4px; padding: 8px; border: 1px solid #c8c8d0; border-radius: 6px; font: inherit; }
            button { margin-top: 8px; padding: 8px 20px; border: 0; border-radius: 6px; background: #5b5bd6; color: #fff; font: inherit; cursor: pointer; }
          </style>
        </head>
        <body><main>{{body}}</main></body>
        </html>
        """, "text/html; charset=utf-8", statusCode: statusCode);

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
