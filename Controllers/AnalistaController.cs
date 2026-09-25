using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Practica2AppCreditos.Data;
using Practica2AppCreditos.Hubs;
using Practica2AppCreditos.Models;

namespace Practica2AppCreditos.Controllers;

[Authorize(Roles = "Analista")]
public class AnalistaController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IDistributedCache _cache;
    private readonly IHubContext<SolicitudesHub> _hubContext;

    public AnalistaController(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        IDistributedCache cache,
        IHubContext<SolicitudesHub> hubContext)
    {
        _context = context;
        _userManager = userManager;
        _cache = cache;
        _hubContext = hubContext;
    }

    public async Task<IActionResult> Index()
    {
        var solicitudes = await _context.SolicitudesCredito
            .AsNoTracking()
            .Include(s => s.Cliente)
            .ThenInclude(cliente => cliente.Usuario)
            .Where(solicitud => solicitud.Estado == EstadoSolicitud.Pendiente)
            .OrderBy(solicitud => solicitud.FechaSolicitud)
            .ThenBy(solicitud => solicitud.Id)
            .ToListAsync();

        return View(solicitudes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aprobar(int id)
    {
        var solicitud = await _context.SolicitudesCredito
            .Include(s => s.Cliente)
            .SingleOrDefaultAsync(s => s.Id == id);

        if (solicitud is null || solicitud.Estado != EstadoSolicitud.Pendiente)
        {
            TempData["Error"] =
                "No se pueden procesar solicitudes que ya fueron aprobadas o rechazadas";
            return RedirectToAction(nameof(Index));
        }

        if (solicitud.Cliente is null)
        {
            TempData["Error"] = "No se encontró la información del cliente de la solicitud.";
            return RedirectToAction(nameof(Index));
        }

        if (solicitud.MontoSolicitado > (solicitud.Cliente.IngresosMensuales * 5))
        {
            TempData["Error"] =
                $"No se puede aprobar la solicitud porque el monto ({solicitud.MontoSolicitado:C}) supera el límite de 5 veces los ingresos mensuales del cliente ({solicitud.Cliente.IngresosMensuales * 5:C}).";
            return RedirectToAction(nameof(Index));
        }

        solicitud.Estado = EstadoSolicitud.Aprobado;
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync($"solicitudes_{solicitud.Cliente.UsuarioId}");

        string propietarioUserId = solicitud.Cliente.UsuarioId;
        await _hubContext.Clients.User(propietarioUserId)
            .SendAsync("SolicitudEstadoActualizado", new
            {
                solicitudId = solicitud.Id,
                estado = solicitud.Estado.ToString(),
                motivoRechazo = solicitud.MotivoRechazo
            });

        TempData["Exito"] = "Solicitud aprobada correctamente.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rechazar(int id, string? motivoRechazo)
    {
        var solicitud = await _context.SolicitudesCredito
            .Include(s => s.Cliente)
            .SingleOrDefaultAsync(s => s.Id == id);

        if (solicitud is null || solicitud.Estado != EstadoSolicitud.Pendiente)
        {
            TempData["Error"] =
                "No se pueden procesar solicitudes que ya fueron aprobadas o rechazadas";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(motivoRechazo))
        {
            TempData["Error"] = "El motivo de rechazo es obligatorio.";
            return RedirectToAction(nameof(Index));
        }

        if (solicitud.Cliente is null)
        {
            TempData["Error"] = "No se encontró la información del cliente de la solicitud.";
            return RedirectToAction(nameof(Index));
        }

        solicitud.Estado = EstadoSolicitud.Rechazado;
        solicitud.MotivoRechazo = motivoRechazo;
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync($"solicitudes_{solicitud.Cliente.UsuarioId}");

        string propietarioUserId = solicitud.Cliente.UsuarioId;
        await _hubContext.Clients.User(propietarioUserId)
            .SendAsync("SolicitudEstadoActualizado", new
            {
                solicitudId = solicitud.Id,
                estado = solicitud.Estado.ToString(),
                motivoRechazo = solicitud.MotivoRechazo
            });

        TempData["Exito"] = "Solicitud rechazada correctamente.";
        return RedirectToAction(nameof(Index));
    }
}
