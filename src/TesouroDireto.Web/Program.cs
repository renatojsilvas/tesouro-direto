using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using TesouroDireto.Web.Components;
using TesouroDireto.Web.Extensions;
using TesouroDireto.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddTransient<CorrelationIdHandler>();

builder.Services.AddSingleton<IConditionalGetStore, BoundedConditionalGetStore>();

builder.Services.AddHttpClient<TesouroApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiSettings:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["ApiSettings:ApiKey"]);
}).AddHttpMessageHandler<CorrelationIdHandler>();

builder.Services.AddScoped<GoogleLoginService>();
builder.Services.AddScoped<IActingUserContext, ActingUserContext>();

var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
var googleConfigurado = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

builder.Services.AddSingleton(new GoogleAuthAvailability(googleConfigurado));

var authenticationBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/auth/login";
    });

if (googleConfigurado)
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.SaveTokens = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.Events.OnCreatingTicket = async context =>
        {
            var sub = context.Identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var email = context.Identity?.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
            var nome = context.Identity?.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
            var emailVerified = context.User.TryGetProperty("email_verified", out var emailVerifiedElement)
                && ReadBool(emailVerifiedElement);

            var loginService = context.HttpContext.RequestServices.GetRequiredService<GoogleLoginService>();
            var claims = new GoogleLoginClaims(sub, email, nome, emailVerified);
            var outcome = await loginService.ProcessLoginAsync(claims);

            if (!outcome.Ok)
            {
                context.Fail("Login Google recusado: e-mail não verificado ou falha ao sincronizar usuário.");
            }
            else if (!string.IsNullOrWhiteSpace(outcome.Papel))
            {
                context.Identity?.AddClaim(new Claim(ClaimTypes.Role, outcome.Papel));
            }
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.Response.Redirect("/");
            context.HandleResponse();

            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseSerilogDefaults();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/auth/login", async (string? returnUrl, IAuthenticationSchemeProvider schemeProvider) =>
{
    var googleScheme = await schemeProvider.GetSchemeAsync(GoogleDefaults.AuthenticationScheme);
    if (googleScheme is null)
    {
        return Results.Redirect("/");
    }

    var target = ReturnUrlSanitizer.Sanitize(returnUrl);
    var properties = new AuthenticationProperties { RedirectUri = target };

    return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
});

app.MapPost("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.LocalRedirect("/");
}).WithMetadata(new RequireAntiforgeryTokenAttribute());

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool ReadBool(JsonElement element) => element.ValueKind switch
{
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) && parsed,
    _ => false
};

internal partial class Program;
