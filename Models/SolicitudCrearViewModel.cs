using System.ComponentModel.DataAnnotations;

namespace Practica2AppCreditos.Models;

public class SolicitudCrearViewModel
{
    [Display(Name = "Monto solicitado")]
    [Required(ErrorMessage = "El monto solicitado es obligatorio")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El monto solicitado debe ser mayor a 0")]
    public decimal MontoSolicitado { get; set; }
}
