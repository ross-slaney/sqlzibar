using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SqlOS.OneCall.Api;

internal static class NotesBrowser
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, ["SqlOS"]));
        app.MapGet("/", async (HttpContext http, IAntiforgery antiforgery, IHttpClientFactory clients) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
            {
                return Page("<p>One notebook, available in your browser and through MCP.</p><p><a href='/login'>Sign in or create an account</a></p>");
            }

            using var request = await ApiRequest(http, HttpMethod.Get, "/api/notes");
            using var response = await clients.CreateClient("notes-api").SendAsync(request, http.RequestAborted);
            var tokens = antiforgery.GetAndStoreTokens(http);
            var field = $"<input type='hidden' name='{Encode(tokens.FormFieldName)}' value='{Encode(tokens.RequestToken!)}'>";
            var logout = $"<form action='/logout' method='post'>{field}<button>Sign out and revoke</button></form>";
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Page("<p>Your API session has expired. <a href='/login'>Sign in again</a>.</p>" + logout);
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return Page("<p>Your notebook access has been removed.</p>" + logout);
            }
            response.EnsureSuccessStatusCode();
            var notes = await response.Content.ReadFromJsonAsync<List<Note>>(http.RequestAborted) ?? [];
            var items = string.Join("", notes.Select(note => $"<li>{Encode(note.Text)}</li>"));
            return Page($"<p>Signed in as {Encode(http.User.Identity.Name ?? "you")}.</p><ul>{items}</ul>" +
                $"<form action='/notes' method='post'>{field}<label>New note <input name='text' required maxlength='2000'></label> <button>Add note</button></form>" +
                "<p>Connect an MCP client to <code>/mcp</code> with this account to read and add the same notes.</p>" + logout);
        });
        app.MapPost("/notes", async (HttpContext http, IAntiforgery antiforgery, IHttpClientFactory clients) =>
        {
            await antiforgery.ValidateRequestAsync(http);
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            using var request = await ApiRequest(http, HttpMethod.Post, "/api/notes");
            request.Content = JsonContent.Create(new NoteRequest(form["text"].ToString()));
            using var response = await clients.CreateClient("notes-api").SendAsync(request, http.RequestAborted);
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            return Results.Redirect("/");
        }).RequireAuthorization();
        app.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery, IHttpClientFactory clients) =>
        {
            await antiforgery.ValidateRequestAsync(http);
            var refreshToken = await http.GetTokenAsync("refresh_token");
            if (refreshToken != null)
            {
                using var response = await clients.CreateClient("notes-api").PostAsJsonAsync("/sqlos/auth/logout",
                    new { refreshToken, sessionId = http.User.FindFirst("sid")?.Value }, http.RequestAborted);
                response.EnsureSuccessStatusCode();
            }
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        }).RequireAuthorization();
    }

    private static async Task<HttpRequestMessage> ApiRequest(HttpContext http, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        var token = await http.GetTokenAsync("access_token");
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string Encode(string text) => HtmlEncoder.Default.Encode(text);

    private static IResult Page(string body) => Results.Content("<!doctype html><html lang='en'><meta charset='utf-8'>" +
        "<meta name='viewport' content='width=device-width,initial-scale=1'><title>Notes · SqlOS</title>" +
        "<body><main><h1>Notes</h1>" + body + "</main></body></html>", "text/html");
}
