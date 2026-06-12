using ClashPlanner.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Fábrica de la API para los tests de integración. Sustituye SQL Server por
/// SQLite en memoria (con una conexión abierta durante toda la vida de la
/// fábrica para que la base persista entre peticiones) y desactiva la
/// auto-migración, creando el esquema con <c>EnsureCreated</c>.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        // Entorno «Testing»: Program carga appsettings.Testing.json (Jwt,
        // Database:Migrate=false) en vez de appsettings.Development.json.
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Quita el DbContext y TODOS los servicios internos del proveedor SQL
            // Server que registró Program; si no, EF ve dos proveedores y falla.
            var efDescriptors = services
                .Where(d => d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore.") == true
                            || d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                            || d.ServiceType == typeof(AppDbContext))
                .ToList();
            foreach (var d in efDescriptors) services.Remove(d);

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));

            // Crea el esquema en la base SQLite en memoria.
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
