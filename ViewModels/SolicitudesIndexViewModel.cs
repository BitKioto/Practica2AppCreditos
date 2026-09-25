using Practica2AppCreditos.Models;

namespace Practica2AppCreditos.ViewModels;

public class SolicitudesIndexViewModel
{
    public EstadoSolicitud? Estado { get; set; }

    public decimal? MontoMin { get; set; }

    public decimal? MontoMax { get; set; }

    public DateTime? FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    public IReadOnlyList<SolicitudCredito> Solicitudes { get; set; }
        = Array.Empty<SolicitudCredito>();
}
