using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ClashPlanner.Api.Data;

/// <summary>
/// Fábrica de DISEÑO para las herramientas de EF Core (<c>dotnet ef migrations add</c>,
/// <c>database update</c>…). Sin ella, el tooling arrancaría todo el host de Program.cs,
/// que al iniciar siembra roles y configuración (tocando la BD) — innecesario y frágil
/// para generar migraciones. Aquí construimos el contexto con el proveedor Npgsql y la
/// cadena de conexión de appsettings.Development.json o de la variable de entorno
/// <c>ConnectionStrings__DefaultConnection</c>. Generar migraciones no abre conexión; solo
/// <c>database update</c> se conecta de verdad.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var conn = config.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=clashplanner;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AppDbContext(options);
    }
}
