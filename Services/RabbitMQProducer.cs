using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Practica2AppCreditos.Messaging;
using Practica2AppCreditos.Models;
using RabbitMQ.Client;

namespace Practica2AppCreditos.Services;

public sealed class RabbitMQProducer
{
    public const string QueueName = "solicitudes.notificaciones";

    private static readonly TimeSpan PublisherConfirmationTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMQProducer> _logger;

    public RabbitMQProducer(
        IConfiguration configuration,
        ILogger<RabbitMQProducer> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> PublicarSolicitudRegistradaAsync(
        SolicitudCredito solicitud,
        Cliente cliente,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var evento = new SolicitudRegistrada
            {
                MessageId = Guid.NewGuid().ToString(),
                SolicitudId = solicitud.Id,
                UsuarioId = cliente.UsuarioId,
                FechaEventoUtc = DateTime.UtcNow
            };

            var connectionString = ObtenerConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogError(
                    "No se pudo publicar la notificación de la solicitud {SolicitudId}: no existe CLOUDAMQP_URL ni ConnectionStrings:RabbitMQ.",
                    solicitud.Id);
                return false;
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(connectionString, UriKind.Absolute),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                RequestedConnectionTimeout = PublisherConfirmationTimeout,
                ContinuationTimeout = PublisherConfirmationTimeout
            };

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(PublisherConfirmationTimeout);

            await using var connection = await factory.CreateConnectionAsync(timeoutCancellation.Token);

            // RabbitMQ.Client 7.x habilita el equivalente a channel.ConfirmSelect()
            // mediante estas opciones. Con el tracking activado, BasicPublishAsync
            // espera la confirmación del broker y lanza una excepción si recibe NACK
            // o una devolución de mensaje.
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);

            await using var channel = await connection.CreateChannelAsync(
                channelOptions,
                timeoutCancellation.Token);

            await channel.QueueDeclareAsync(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: timeoutCancellation.Token);

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = evento.MessageId,
                Type = nameof(SolicitudRegistrada)
            };

            var body = JsonSerializer.SerializeToUtf8Bytes(evento, JsonOptions);

            // Esta publicación es el equivalente de usar
            // channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5)): el timeout
            // y el tracking de confirmaciones garantizan la espera máxima.
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: QueueName,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: timeoutCancellation.Token);

            _logger.LogInformation(
                "Notificación de la solicitud {SolicitudId} publicada en RabbitMQ con MessageId {MessageId}.",
                solicitud.Id,
                evento.MessageId);

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "No se pudo encolar la notificación de la solicitud {SolicitudId}.",
                solicitud.Id);
            return false;
        }
    }

    private string ObtenerConnectionString()
    {
        var environmentConnectionString = Environment.GetEnvironmentVariable("CLOUDAMQP_URL");
        return !string.IsNullOrWhiteSpace(environmentConnectionString)
            ? environmentConnectionString
            : _configuration.GetConnectionString("RabbitMQ") ?? string.Empty;
    }
}
