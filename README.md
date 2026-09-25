# Práctica Calificada 2 - Sistema de Solicitudes de Crédito

## 🧪 Evidencias y Pruebas de Mensajería Asíncrona (Pregunta 7)

### Pasos para reproducir las pruebas:

1. **Desactivación del Consumidor:**
   - Ejecutar la aplicación especificando `RabbitMq__ConsumerEnabled=false`.
   - Registrar una nueva solicitud desde el panel de cliente.
   - *Resultado esperado:* El mensaje es publicado con confirmación de publisher y queda retenido en la cola durable `solicitudes.notificaciones` de CloudAMQP.


2. **Procesamiento de Cola y Consumidor:**
   - Reiniciar el servidor con `RabbitMq__ConsumerEnabled=true`.
   - *Resultado esperado:* El consumidor procesa el mensaje, guarda la entidad `Notificacion` en SQLite, realiza el ACK manual y vacía la cola.

3. **Verificación de Idempotencia (Mismo MessageId):**
   - Enviar nuevamente desde CloudAMQP Management un payload JSON reutilizando el mismo `MessageId`.
   - *Resultado esperado:* El consumidor detecta la clave existente en la BD, envía el ACK para descartar el mensaje repetido de la cola y evita la inserción de notificaciones duplicadas.
