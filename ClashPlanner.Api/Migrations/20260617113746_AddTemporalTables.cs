using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClashPlanner.Api.Migrations
{
    /// <summary>
    /// Migración de RECONCILIACIÓN: el system-versioning (Tablas Temporales) de las 15
    /// tablas ya se aplicó en la BD fuera de banda (script SQL administrado). Esta
    /// migración existe solo para que el modelo/snapshot de EF refleje el versionado y
    /// las migraciones futuras lo respeten; por eso Up() y Down() son NO-OP: recrear o
    /// eliminar el versionado aquí chocaría con el esquema real. El estado «temporal»
    /// vive en el .Designer.cs y en AppDbContextModelSnapshot.cs, no en estos métodos.
    ///
    /// Si en el futuro EF debe poseer también la creación/borrado del versionado (p. ej.
    /// recrear la BD desde cero solo con migraciones), regenerar esta migración con
    /// `dotnet ef migrations remove` + `migrations add` sobre una BD sin temporales.
    /// </summary>
    public partial class AddTemporalTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NO-OP: el versionado ya existe en la BD (ver resumen de la clase).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // NO-OP: el versionado lo administra el script SQL, no esta migración.
        }
    }
}
