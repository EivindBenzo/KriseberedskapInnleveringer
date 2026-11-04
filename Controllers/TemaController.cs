using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeimevernetInnlevering1.Controllers;

/// <summary>
/// Bytter mellom lys og mørk modus. Valget lagres i informasjonskapselen (cookien) «tema»,
/// og _Layout.cshtml leser den og setter data-theme på &lt;html&gt;. CSS gjør resten.
/// </summary>
[AllowAnonymous]
public class TemaController : Controller
{
    public const string Cookie = "tema";
    public const string Mørk = "dark";
    public const string Lys = "light";

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Bytt(string? returnUrl)
    {
        var nytt = Request.Cookies[Cookie] == Mørk ? Lys : Mørk;
        Response.Cookies.Append(Cookie, nytt, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });
        // Tilbake til siden brukeren var på. Bare lokale adresser godtas, så lenken kan ikke sende brukeren til et annet nettsted.
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
    }

    /// <summary>Temaet for denne forespørselen. Lys er standard.</summary>
    public static string Gjeldende(HttpRequest request) => request.Cookies[Cookie] == Mørk ? Mørk : Lys;
}
