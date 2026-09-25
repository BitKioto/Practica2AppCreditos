(function () {
    const statusElement = document.querySelector("[data-signalr-status]");
    const notificationsElement = document.querySelector("[data-signalr-notificaciones]");

    if (!statusElement || !notificationsElement) {
        return;
    }

    function setConnectionStatus(text, cssClass) {
        statusElement.textContent = text;
        statusElement.className = `badge ${cssClass}`;
    }

    function getEstadoClass(estado) {
        switch (estado) {
            case "Pendiente":
                return "bg-warning text-dark";
            case "Aprobado":
                return "bg-success";
            case "Rechazado":
                return "bg-danger";
            default:
                return "bg-secondary";
        }
    }

    function actualizarSolicitud(payload) {
        if (!payload || payload.solicitudId === undefined || payload.solicitudId === null) {
            return;
        }

        const solicitudId = String(payload.solicitudId);
        const estado = payload.estado || "Desconocido";

        document.querySelectorAll(`[data-solicitud-id="${solicitudId}"]`).forEach((contenedor) => {
            contenedor.querySelectorAll("[data-solicitud-estado]").forEach((badge) => {
                badge.textContent = estado;
                badge.className = `badge ${getEstadoClass(estado)}`;
            });

            const motivo = contenedor.querySelector("[data-solicitud-motivo]");
            if (motivo) {
                if (estado === "Rechazado") {
                    motivo.textContent = payload.motivoRechazo || "No indicado.";
                    motivo.classList.remove("d-none");
                } else {
                    motivo.textContent = "No aplica.";
                    if (contenedor.hasAttribute("data-solicitud-detalle")) {
                        motivo.classList.remove("d-none");
                    } else {
                        motivo.classList.add("d-none");
                    }
                }
            }
        });
    }

    function mostrarNotificacion(payload) {
        if (!payload || payload.solicitudId === undefined || payload.solicitudId === null) {
            return;
        }

        const solicitudId = String(payload.solicitudId);
        const estado = payload.estado || "actualizado";
        const mensaje = document.createElement("div");
        mensaje.className = "alert alert-info alert-dismissible fade show mb-2";
        mensaje.setAttribute("role", "alert");

        if (estado === "Rechazado" && payload.motivoRechazo) {
            mensaje.textContent = `Solicitud #${solicitudId} rechazada. Motivo: ${payload.motivoRechazo}`;
        } else {
            mensaje.textContent = `Solicitud #${solicitudId}: estado ${estado}.`;
        }

        const cerrar = document.createElement("button");
        cerrar.type = "button";
        cerrar.className = "btn-close";
        cerrar.setAttribute("data-bs-dismiss", "alert");
        cerrar.setAttribute("aria-label", "Cerrar");
        cerrar.addEventListener("click", () => mensaje.remove());

        mensaje.appendChild(cerrar);
        notificationsElement.appendChild(mensaje);

        window.setTimeout(() => {
            mensaje.classList.add("d-none");
            window.setTimeout(() => mensaje.remove(), 250);
        }, 7000);
    }

    if (!window.signalR) {
        setConnectionStatus("No disponible", "bg-danger");
        return;
    }

    const solicitudIds = Array.from(
        new Set(
            Array.from(document.querySelectorAll("[data-solicitud-id]"))
                .map((element) => element.getAttribute("data-solicitud-id"))
                .filter((id) => id !== null)
        )
    );

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/solicitudes", {
            transport: signalR.HttpTransportType.WebSockets
        })
        .withAutomaticReconnect()
        .build();

    async function recuperarEstados() {
        for (const solicitudId of solicitudIds) {
            try {
                const estado = await connection.invoke("ObtenerEstadoVigente", solicitudId);
                actualizarSolicitud(estado);
            } catch (error) {
                console.error(`No se pudo recuperar el estado de la solicitud ${solicitudId}.`, error);
            }
        }
    }

    connection.on("SolicitudEstadoActualizado", (payload) => {
        actualizarSolicitud(payload);
        mostrarNotificacion(payload);
    });

    connection.onreconnecting(() => {
        setConnectionStatus("Reconectando...", "bg-warning text-dark");
    });

    connection.onreconnected(async () => {
        setConnectionStatus("Conectado", "bg-success");
        await recuperarEstados();
    });

    connection.onclose(() => {
        setConnectionStatus("Desconectado", "bg-secondary");
    });

    connection.start()
        .then(async () => {
            setConnectionStatus("Conectado", "bg-success");
            await recuperarEstados();
        })
        .catch((error) => {
            setConnectionStatus("Desconectado", "bg-danger");
            console.error("No se pudo iniciar la conexión SignalR.", error);
        });
})();
