using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Practica2AppCreditos.Controllers;

[AllowAnonymous]
[Route("Account/AccessDenied")]
public class AccessDeniedController : Controller
{
    [HttpGet]
    public IActionResult Index(string? returnUrl = null)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }
}
