/*
 * RESET TOTAL de los datos de la base ClashPlanner.
 *
 * Vacía TODAS las tablas de usuarios y de sincronización (no filtra por nadie),
 * dejando la BD vacía pero funcional. SE CONSERVAN:
 *   - el esquema y el historial de migraciones (__EFMigrationsHistory),
 *   - DataProtectionKeys (claves para descifrar tokens),
 *   - Roles / RoleClaims (configuración, no datos de usuario).
 *
 * Todo en una transacción: si algo falla, no se borra nada.
 *
 * Uso:  sqlcmd -S "(localdb)\MSSQLLocalDB" -d ClashPlanner -b -i reset-all-data.sql
 */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    DELETE FROM dbo.Jobs;           PRINT 'Jobs:           ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Boosts;         PRINT 'Boosts:         ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.HelperStates;   PRINT 'HelperStates:   ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Plans;          PRINT 'Plans:          ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Overrides;      PRINT 'Overrides:      ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.UserSyncStates; PRINT 'UserSyncStates: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Deletions;      PRINT 'Deletions:      ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.CocTokens;      PRINT 'CocTokens:      ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Villages;       PRINT 'Villages:       ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.RefreshTokens;  PRINT 'RefreshTokens:  ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    -- Tablas de Identity ligadas al usuario (no tocamos Roles/RoleClaims).
    DELETE FROM dbo.UserTokens;     PRINT 'UserTokens:     ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.UserLogins;     PRINT 'UserLogins:     ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.UserClaims;     PRINT 'UserClaims:     ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.UserRoles;      PRINT 'UserRoles:      ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    DELETE FROM dbo.Users;          PRINT 'Users:          ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    COMMIT;
    PRINT 'OK: la base queda vacia (esquema, migraciones, DataProtectionKeys y Roles intactos).';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'ERROR: ' + ERROR_MESSAGE() + ' (no se ha borrado nada).';
    THROW;
END CATCH;
