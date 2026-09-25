using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Practica2AppCreditos.Data;
using Practica2AppCreditos.Messaging;
using Practica2AppCreditos.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Practica2AppCreditos.Services;

public sealed class SolicitudConsumerService : BackgroundService
{
    public const string QueueName = "solicitudes.notificaciones";

    private static readonly TimeSpan ReconexionDelay = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SolicitudConsumerService> _logger;

    public SolicitudConsumerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SolicitudConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = ObtenerConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning(
                "RabbitMQ no está configurado. El consumidor de notificaciones queda desactivado hasta que se configure CLOUDAMQP_URL o ConnectionStrings:RabbitMQ.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EscucharAsync(connectionString, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Error inesperado en el consumidor de notificaciones. Seintentará reconectar.");
                await EsperarReconexionAsync(stoppingToken);
            }
        }
    }

    private async Task EscucharAsync(
        string connectionString,
        CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString, UriKind.Absolute),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false),
            stoppingToken);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, eventArgs) =>
            ProcesarMensajeAsync(channel, eventArgs, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumerTag: "solicitudes-notificaciones-consumer",
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Consumidor de notificaciones escuchando en {Queue}.", QueueName);

        // La recuperación automática de RabbitMQ mantiene el consumidor tras
        // reconexiones del broker. La cancelación cierra el canal y el servicio.
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task ProcesarMensajeAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken)
    {
        SolicitudRegistrada? evento;

        try
        {
            evento = JsonSerializer.Deserialize<SolicitudRegistrada>(
                eventArgs.Body.Span,
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            _logger.LogError(
                exception,
                "Mensaje corrupto recibido desde RabbitMQ. DeliveryTag={DeliveryTag}.",
                eventArgs.DeliveryTag);
            await RechazarAsync(channel, eventArgs.DeliveryTag);
            return;
        }

        if (!EsEventoValido(evento))
        {
            _logger.LogError(
                "Mensaje inválido recibido desde RabbitMQ. DeliveryTag={DeliveryTag}.",
                eventArgs.DeliveryTag);
            await RechazarAsync(channel, eventArgs.DeliveryTag);
            return;
        }

        var eventoValido = evento!;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

            var yaExiste = await dbContext.Notificaciones.AnyAsync(
                notificacion => notificacion.MessageId == eventoValido.MessageId,
                stoppingToken);

            if (yaExiste)
            {
                // La idempotencia evita duplicados si RabbitMQ redelivera el mensaje.
                await channel.BasicAckAsync(
                    eventArgs.DeliveryTag,
                    multiple: false,
                    stoppingToken);
                return;
            }

            dbContext.Notificaciones.Add(new Notificacion
            {
                MessageId = eventoValido.MessageId,
                SolicitudId = eventoValido.SolicitudId,
                UsuarioId = eventoValido.UsuarioId,
                Texto = "Recibimos tu solicitud de crédito y está pendiente de evaluación",
                FechaProcesamientoUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync(stoppingToken);

            // Solo se confirma el mensaje después de persistir la notificación.
            await channel.BasicAckAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Al cerrar el canal durante el apagado, RabbitMQ reencolará el mensaje.
            _logger.LogWarning(
                "Procesamiento de notificación interrumpido durante el apagado. MessageId={MessageId}.",
                eventoValido.MessageId);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "No se pudo procesar la notificación MessageId={MessageId}. Se hará NACK sin reencolar.",
                eventoValido.MessageId);

            await channel.BasicNackAsync(
                eventArgs.DeliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken: CancellationToken.None);
        }
    }

    private static bool EsEventoValido(SolicitudRegistrada? evento)
    {
        return evento is not null &&
               !string.IsNullOrWhiteSpace(evento.MessageId) &&
               evento.SolicitudId > 0 &&
               !string.IsNullOrWhiteSpace(evento.UsuarioId) &&
               evento.FechaEventoUtc != default;
    }

    private static Task RechazarAsync(IChannel channel, ulong deliveryTag)
    {
        return channel.BasicRejectAsync(
            deliveryTag,
            requeue: false,
            cancellationToken: CancellationToken.None).AsTask();
    }

    private async Task EsperarReconexionAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(ReconexionDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La cancelación es la forma normal de finalizar el hosted service.
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
