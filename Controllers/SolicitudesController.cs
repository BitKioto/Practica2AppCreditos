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

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var usuario = await _userManager.GetUserAsync(User);

        if (usuario is null)
        {
            return Challenge();
        }

        var cliente = await ObtenerClienteActualAsync(usuario.Id);

        if (cliente is null)
        {
            ModelState.AddModelError(
                string.Empty,
                "El cliente no está activo o no está registrado.");
            return View(new SolicitudCrearViewModel());
        }

        PrepararDatosCliente(cliente);

        if (!cliente.Activo)
        {
            ModelState.AddModelError(
                string.Empty,
                "El cliente no está activo o no está registrado.");
        }

        return View(new SolicitudCrearViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SolicitudCrearViewModel model)
    {
        var usuario = await _userManager.GetUserAsync(User);

        if (usuario is null)
        {
            return Challenge();
        }

        var cliente = await ObtenerClienteActualAsync(usuario.Id);

        if (cliente is not null)
        {
            PrepararDatosCliente(cliente);
        }

        if (cliente is null || !cliente.Activo)
        {
            ModelState.AddModelError(
                string.Empty,
                "El cliente no está activo para registrar solicitudes.");
        }
        else
        {
            var existeSolicitudPendiente = await _context.SolicitudesCredito
                .AsNoTracking()
                .AnyAsync(solicitud =>
                    solicitud.ClienteId == cliente.Id &&
                    solicitud.Estado == EstadoSolicitud.Pendiente);

            if (existeSolicitudPendiente)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Ya cuenta con una solicitud de crédito en estado Pendiente.");
            }

            var montoMaximo = cliente.IngresosMensuales * 10m;

            if (model.MontoSolicitado > montoMaximo)
            {
                ModelState.AddModelError(
                    string.Empty,
                    $"El monto solicitado no puede superar 10 veces sus ingresos mensuales (Máximo permitido: {montoMaximo:C}).");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (cliente is null)
        {
            return View(model);
        }

        _context.SolicitudesCredito.Add(new SolicitudCredito
        {
            ClienteId = cliente.Id,
            MontoSolicitado = model.MontoSolicitado,
            FechaSolicitud = DateTime.UtcNow,
            Estado = EstadoSolicitud.Pendiente
        });

        await _context.SaveChangesAsync();

        TempData["Exito"] = "Solicitud registrada con éxito.";
        return RedirectToAction(nameof(Index));
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

    private Task<Cliente?> ObtenerClienteActualAsync(string usuarioId)
    {
        return _context.Clientes
            .AsNoTracking()
            .SingleOrDefaultAsync(cliente => cliente.UsuarioId == usuarioId);
    }

    private void PrepararDatosCliente(Cliente cliente)
    {
        ViewBag.ClienteActivo = cliente.Activo;
        ViewBag.IngresosMensuales = cliente.IngresosMensuales;
        ViewBag.MontoMaximo = cliente.IngresosMensuales * 10m;
    }
}
