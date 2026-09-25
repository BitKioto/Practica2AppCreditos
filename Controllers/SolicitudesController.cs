using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Practica2AppCreditos.Data;
using Practica2AppCreditos.Models;
using Practica2AppCreditos.ViewModels;

namespace Practica2AppCreditos.Controllers;

[Authorize]
public class SolicitudesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public SolicitudesController(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(
        EstadoSolicitud? estado,
        decimal? montoMin,
        decimal? montoMax,
        DateTime? fechaInicio,
        DateTime? fechaFin)
    {
        var viewModel = new SolicitudesIndexViewModel
        {
            Estado = estado,
            MontoMin = montoMin,
            MontoMax = montoMax,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin
        };

        if (montoMin < 0 || montoMax < 0)
        {
            ModelState.AddModelError(
                string.Empty,
                "Los montos de filtro no pueden ser negativos");
        }

        if (fechaInicio.HasValue &&
            fechaFin.HasValue &&
            fechaInicio.Value > fechaFin.Value)
        {
            ModelState.AddModelError(
                string.Empty,
                "La fecha inicio no puede ser mayor a la fecha fin");
        }

        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        var usuario = await _userManager.GetUserAsync(User);

        if (usuario is null)
        {
            return Challenge();
        }

        var consulta = _context.SolicitudesCredito
            .AsNoTracking()
            .Where(solicitud => solicitud.Cliente.UsuarioId == usuario.Id);

        if (estado.HasValue)
        {
            consulta = consulta.Where(solicitud => solicitud.Estado == estado.Value);
        }

        if (montoMin.HasValue)
        {
            consulta = consulta.Where(solicitud => solicitud.MontoSolicitado >= montoMin.Value);
        }

        if (montoMax.HasValue)
        {
            consulta = consulta.Where(solicitud => solicitud.MontoSolicitado <= montoMax.Value);
        }

        if (fechaInicio.HasValue)
        {
            var inicio = fechaInicio.Value.Date;
            consulta = consulta.Where(solicitud => solicitud.FechaSolicitud >= inicio);
        }

        if (fechaFin.HasValue)
        {
            var finExclusivo = fechaFin.Value.Date == DateTime.MaxValue.Date
                ? DateTime.MaxValue
                : fechaFin.Value.Date.AddDays(1);

            consulta = consulta.Where(solicitud => solicitud.FechaSolicitud < finExclusivo);
        }

        viewModel.Solicitudes = await consulta
            .OrderByDescending(solicitud => solicitud.FechaSolicitud)
            .ThenByDescending(solicitud => solicitud.Id)
            .ToListAsync();

        return View(viewModel);
    }

    public async Task<IActionResult> Detalle(int id)
    {
        var usuario = await _userManager.GetUserAsync(User);

        if (usuario is null)
        {
            return Challenge();
        }

        var solicitud = await _context.SolicitudesCredito
            .AsNoTracking()
            .Include(s => s.Cliente)
            .ThenInclude(c => c.Usuario)
            .SingleOrDefaultAsync(s => s.Id == id);

        if (solicitud is null)
        {
            return NotFound();
        }

        if (solicitud.Cliente.UsuarioId != usuario.Id)
        {
            return Forbid();
        }

        return View("Detalles", solicitud);
    }
}
