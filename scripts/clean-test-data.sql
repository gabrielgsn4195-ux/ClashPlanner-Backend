/*
 * Limpia los datos de prueba de la base de datos ClashPlanner.
 *
 * Borra los usuarios cuyo email coincide con @pattern y TODO lo que cuelga de
 * ellos. Las tablas de sincronización (Villages, Jobs, ...) y RefreshTokens se
 * relacionan por `UserId` SIN clave foránea a Users, así que hay que borrarlas
 * explícitamente (borrar el usuario no las arrastra). Las tablas de Identity
 * (UserRoles/UserClaims/UserLogins/UserTokens) sí tienen FK en cascada, pero se
 * borran a mano igualmente para no depender de ello y respetar el orden.
 *
 * Por defecto borra los usuarios de QA «qa+...@ejemplo.com». Para otro criterio,
 * cambia el valor de @pattern (p. ej. N'%' borraría TODOS los usuarios — úsalo
 * con cuidado). Todo va en una transacción: si algo falla, no se borra nada.
 *
 * Uso:  sqlcmd -S "(localdb)\MSSQLLocalDB" -d ClashPlanner -b -i clean-test-data.sql
 */
-- QUOTED_IDENTIFIER/ANSI_NULLS deben ir ON: las tablas de Identity tienen
-- índices filtrados (NormalizedEmail/UserName) que lo exigen al hacer DELETE.
-- sqlcmd los pone OFF por defecto, así que los forzamos aquí.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @pattern NVARCHAR(256) = N'qa+%@ejemplo.com';

IF OBJECT_ID('tempdb..#ids') IS NOT NULL DROP TABLE #ids;
SELECT Id INTO #ids FROM dbo.Users WHERE Email LIKE @pattern;

DECLARE @n INT = (SELECT COUNT(*) FROM #ids);
PRINT 'Usuarios que coinciden con ' + @pattern + ': ' + CAST(@n AS NVARCHAR(10));

IF @n = 0
BEGIN
    PRINT 'Nada que borrar.';
    RETURN;
END

PRINT 'Emails:';
SELECT '  - ' + Email FROM dbo.Users WHERE Id IN (SELECT Id FROM #ids);

BEGIN TRY
    BEGIN TRAN;

    DELETE FROM dbo.Jobs            WHERE UserId IN (SELECT Id FROM #ids); PRINT 'Jobs:           ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Boosts          WHERE UserId IN (SELECT Id FROM #ids); PRINT 'Boosts:         ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.HelperStates    WHERE UserId IN (SELECT Id FROM #ids); PRINT 'HelperStates:   ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Plans           WHERE UserId IN (SELECT Id FROM #ids); PRINT 'Plans:          ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Overrides       WHERE UserId IN (SELECT Id FROM #ids); PRINT 'Overrides:      ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.UserSyncStates  WHERE UserId IN (SELECT Id FROM #ids); PRINT 'UserSyncStates: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Deletions       WHERE UserId IN (SELECT Id FROM #ids); PRINT 'Deletions:      ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.CocTokens       WHERE UserId IN (SELECT Id FROM #ids); PRINT 'CocTokens:      ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.Villages        WHERE UserId IN (SELECT Id FROM #ids); PRINT 'Villages:       ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.RefreshTokens   WHERE UserId IN (SELECT Id FROM #ids); PRINT 'RefreshTokens:  ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    -- Tablas de Identity ligadas al usuario.
    DELETE FROM dbo.UserTokens      WHERE UserId IN (SELECT Id FROM #ids); PRINT 'UserTokens:     ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.UserLogins      WHERE UserId IN (SELECT Id FROM #ids); PRINT 'UserLogins:     ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.UserClaims      WHERE UserId IN (SELECT Id FROM #ids); PRINT 'UserClaims:     ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
    DELETE FROM dbo.UserRoles       WHERE UserId IN (SELECT Id FROM #ids); PRINT 'UserRoles:      ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    DELETE FROM dbo.Users           WHERE Id     IN (SELECT Id FROM #ids); PRINT 'Users:          ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

    COMMIT;
    PRINT 'OK: datos de prueba eliminados.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'ERROR: ' + ERROR_MESSAGE() + ' (no se ha borrado nada).';
    THROW;
END CATCH;

DROP TABLE #ids;
