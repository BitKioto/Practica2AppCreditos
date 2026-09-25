using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Practica2AppCreditos.Models;

namespace Practica2AppCreditos.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var context = provider.GetRequiredService<ApplicationDbContext>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();

        await context.Database.MigrateAsync();

        await EnsureRoleAsync(roleManager, "Cliente");
        await EnsureRoleAsync(roleManager, "Analista");

        _ = await EnsureUserAsync(
            userManager,
            "analista1@banco.com",
            "Analista");

        var cliente1 = await EnsureUserAsync(
            userManager,
            "cliente1@banco.com",
            "Cliente");

        var cliente2 = await EnsureUserAsync(
            userManager,
            "cliente2@banco.com",
            "Cliente");

        var cliente1Entity = await EnsureClienteAsync(context, cliente1, 3000m);
        var cliente2Entity = await EnsureClienteAsync(context, cliente2, 5000m);

        await EnsureSolicitudAsync(
            context,
            cliente1Entity,
            5000m,
            EstadoSolicitud.Pendiente);

        await EnsureSolicitudAsync(
            context,
            cliente2Entity,
            10000m,
            EstadoSolicitud.Aprobado);

        await context.SaveChangesAsync();
    }

    private static async Task EnsureRoleAsync(
        RoleManager<IdentityRole> roleManager,
        string roleName)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(roleName));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"No se pudo crear el rol '{roleName}': {GetErrors(result)}");
            }
        }
    }

    private static async Task<IdentityUser> EnsureUserAsync(
        UserManager<IdentityUser> userManager,
        string email,
        string roleName)
    {
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user, "Password123!");

            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"No se pudo crear el usuario '{email}': {GetErrors(createResult)}");
            }
        }

        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            var addRoleResult = await userManager.AddToRoleAsync(user, roleName);

            if (!addRoleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"No se pudo asignar el rol '{roleName}' al usuario '{email}': {GetErrors(addRoleResult)}");
            }
        }

        return user;
    }

    private static async Task<Cliente> EnsureClienteAsync(
        ApplicationDbContext context,
        IdentityUser user,
        decimal ingresosMensuales)
    {
        var cliente = await context.Clientes
            .SingleOrDefaultAsync(c => c.UsuarioId == user.Id);

        if (cliente is not null)
        {
            return cliente;
        }

        cliente = new Cliente
        {
            UsuarioId = user.Id,
            Usuario = user,
            IngresosMensuales = ingresosMensuales,
            Activo = true
        };

        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        return cliente;
    }

    private static async Task EnsureSolicitudAsync(
        ApplicationDbContext context,
        Cliente cliente,
        decimal monto,
        EstadoSolicitud estado)
    {
        var existeSolicitud = await context.SolicitudesCredito
            .AnyAsync(s => s.ClienteId == cliente.Id);

        if (existeSolicitud)
        {
            return;
        }

        context.SolicitudesCredito.Add(new SolicitudCredito
        {
            ClienteId = cliente.Id,
            MontoSolicitado = monto,
            FechaSolicitud = DateTime.UtcNow,
            Estado = estado
        });
    }

    private static string GetErrors(IdentityResult result)
    {
        return string.Join("; ", result.Errors.Select(error => error.Description));
    }
}
