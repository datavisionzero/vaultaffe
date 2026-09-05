using System.Net;
using System.Text;
using Vaultaffe.Application.Acts;
using Vaultaffe.Domain.Identities;
using Vaultaffe.Domain.Refusals;

namespace Vaultaffe.Api.Http;

/// <summary>
/// The one page this stage has: where a human confirms a device-code login
/// (Specification §6.6).
/// </summary>
/// <remarks>
/// It is rendered by the server, in one file, with no stylesheet and no script,
/// because the management application of the next stage is what a browser surface
/// worth building looks like — and a half-built one here would be thrown away
/// twice.
/// <para>
/// It asks for the email address and password rather than assuming a session,
/// which is what makes it safe to be the only page: there is no cookie to forge
/// and nothing a cross-site request could reuse, because every confirmation
/// carries the credentials with it. A stranger who guessed a user code still has
/// to know somebody's password, and what they would confirm is a login they do
/// not hold the device code for.
/// </para>
/// <para>
/// It is deliberately not in the OpenAPI document. That document is what both
/// clients are generated from (ADR 0006), and this is a page for a person.
/// </para>
/// </remarks>
public static class DevicePage
{
    /// <summary>Where the CLI sends a human.</summary>
    public const string Path = BeginDeviceLogin.VerificationPath;

    public static IEndpointRouteBuilder MapDevicePage(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, (string? code, HttpContext context) =>
                Html(context, Form(code, message: null)))
            .ExcludeFromDescription();

        endpoints.MapPost(Path, async (
                HttpContext context,
                ConfirmDeviceLogin confirm,
                CancellationToken cancellation) =>
            {
                var form = await context.Request.ReadFormAsync(cancellation);

                var code = form["code"].ToString();
                var email = form["email"].ToString();
                var password = form["password"].ToString();
                var denying = form.ContainsKey("deny");

                try
                {
                    var name = denying
                        ? await confirm.DenyAsync(code, email, password, cancellation)
                        : await confirm.ApproveAsync(code, email, password, cancellation);

                    return Html(context, Decided(name, denying));
                }
                catch (Refusal refusal)
                {
                    // A person gets the sentence, not a problem document: this is
                    // the one surface in this stage that a browser renders.
                    return Html(
                        context,
                        Form(code, refusal.Detail ?? "That did not work."),
                        StatusCodes.Status400BadRequest);
                }
            })
            .ExcludeFromDescription()
            .DisableAntiforgery();

        return endpoints;
    }

    private static IResult Html(HttpContext context, string body, int status = StatusCodes.Status200OK)
    {
        context.Response.StatusCode = status;

        return Results.Content(Page(body), "text/html; charset=utf-8");
    }

    private static string Form(string? code, string? message)
    {
        var typed = Escaped(UserCode.ForReading(UserCode.Normalize(code)));

        var problem = message is null
            ? string.Empty
            : $"<p class=\"problem\">{Escaped(message)}</p>";

        return $"""
            <h1>Confirm a sign-in</h1>
            <p>
              A <code>vaultaffe</code> somewhere is waiting for this code. Check that it is the
              one your terminal is showing before you confirm it.
            </p>
            {problem}
            <form method="post" autocomplete="off">
              <label for="code">Code from your terminal</label>
              <input id="code" name="code" value="{typed}" placeholder="XXXX-XXXX"
                     autocapitalize="characters" spellcheck="false" required>

              <label for="email">Your email address</label>
              <input id="email" name="email" type="email" autocomplete="username" required>

              <label for="password">Your password</label>
              <input id="password" name="password" type="password"
                     autocomplete="current-password" required>

              <div class="buttons">
                <button type="submit" name="approve" value="1">Confirm</button>
                <button type="submit" name="deny" value="1" class="secondary">I did not start this</button>
              </div>
            </form>
            """;
    }

    private static string Decided(string name, bool denied) =>
        denied
            ? """
              <h1>Refused</h1>
              <p>Nothing was signed in. Whoever started that login gets no token.</p>
              <p>
                If it was not you and you did not expect it, change your password and revoke
                any token you do not recognise.
              </p>
              """
            : $"""
              <h1>Confirmed</h1>
              <p>Signed in as {Escaped(name)}. You can close this page and go back to your terminal.</p>
              """;

    private static string Escaped(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    // One file, no stylesheet, no script, nothing loaded from anywhere: a page
    // that a browser with no network left still renders, on a machine that may
    // not be the one being signed in.
    private static string Page(string body) =>
        new StringBuilder()
            .Append("""
                <!doctype html>
                <html lang="en">
                <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <meta name="robots" content="noindex">
                <title>vaultaffe</title>
                <style>
                :root { color-scheme: light dark; }
                body {
                  margin: 0 auto; padding: 3rem 1.25rem; max-width: 26rem; line-height: 1.5;
                  font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
                }
                h1 { font-size: 1.4rem; margin: 0 0 0.75rem; }
                p { margin: 0 0 1rem; }
                label { display: block; margin: 1rem 0 0.25rem; font-size: 0.9rem; }
                input {
                  width: 100%; padding: 0.55rem 0.65rem; font: inherit; box-sizing: border-box;
                  border: 1px solid currentColor; border-radius: 0.4rem; background: transparent;
                  color: inherit;
                }
                #code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
                        letter-spacing: 0.15em; text-transform: uppercase; }
                .buttons { display: flex; gap: 0.5rem; margin-top: 1.5rem; flex-wrap: wrap; }
                button {
                  flex: 1 1 8rem; padding: 0.6rem 0.9rem; font: inherit; cursor: pointer;
                  border-radius: 0.4rem; border: 1px solid currentColor;
                }
                button.secondary { opacity: 0.7; }
                .problem { padding: 0.6rem 0.75rem; border: 1px solid currentColor;
                           border-radius: 0.4rem; }
                code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
                </style>
                </head>
                <body>
                """)
            .Append(body)
            .Append("""

                </body>
                </html>
                """)
            .ToString();
}
