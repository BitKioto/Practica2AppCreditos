using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Practica2AppCreditos.Data;

namespace Practica2AppCreditos.Hubs;

[Authorize]
public class SolicitudesHub : Hub
{
    private readonly ApplicationDbContext _context;

    public SolicitudesHub(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<object> ObtenerEstadoVigente(int solicitudId)
    {
        var usuarioId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(usuarioId))
        {
            usuarioId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        if (string.IsNullOrWhiteSpace(usuarioId))
        {
            throw new HubException("No se pudo identificar al usuario autenticado.");
        }

        var solicitud = await _context.SolicitudesCredito
            .AsNoTracking()
            .Where(s => s.Id == solicitudId && s.Cliente.UsuarioId == usuarioId)
            .Select(s => new
            {
                s.Id,
                s.Estado,
                s.MotivoRechazo
            })
            .SingleOrDefaultAsync();

        if (solicitud is null)
        {
            throw new HubException("La solicitud no fue encontrada.");
        }

        return new
        {
            solicitudId = solicitud.Id,
            estado = solicitud.Estado.ToString(),
            motivoRechazo = solicitud.MotivoRechazo
        };
    }
}
