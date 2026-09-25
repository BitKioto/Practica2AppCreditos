namespace Practica2AppCreditos.Messaging;

public sealed class SolicitudRegistrada
{
    public string MessageId { get; init; } = string.Empty;

    public int SolicitudId { get; init; }

    public string UsuarioId { get; init; } = string.Empty;

    public DateTime FechaEventoUtc { get; init; }
}
