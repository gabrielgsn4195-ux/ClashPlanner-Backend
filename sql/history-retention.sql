-- Retención del historial de las tablas temporales (system-versioned) — auditoría F-013.
--
-- El versionado temporal se provisiona FUERA DE BANDA (igual que su activación inicial),
-- no por migraciones EF. Sin retención, las tablas `history.*` crecen sin techo: el
-- protocolo de /sync borra y reinserta el snapshot completo del usuario EN CADA push, así
-- que cada push genera una fila de historial por entidad afectada. En Azure SQL serverless
-- eso multiplica el almacenamiento (coste, backups, tiempos de despliegue) sin límite.
--
-- Este script fija una retención de 90 días en las 15 tablas versionadas. SQL Server purga
-- automáticamente el historial más antiguo. Ejecutar UNA vez contra la BD de producción
-- (Azure SQL) tras provisionar el versionado; volver a ejecutar es seguro (idempotente).
--
-- Requisitos: la tabla ya debe ser system-versioned (SYSTEM_VERSIONING = ON) y la BD debe
-- tener la limpieza automática habilitada (por defecto en Azure SQL). Ajustar 90 DAYS si
-- se necesita otra ventana.

SET XACT_ABORT ON;

DECLARE @tables TABLE (name SYSNAME);
INSERT INTO @tables (name) VALUES
    -- Sync / negocio (alta rotación: se reescriben en cada push)
    (N'Villages'), (N'Jobs'), (N'Boosts'), (N'HelperStates'), (N'Plans'),
    (N'Overrides'), (N'UserSyncStates'), (N'Deletions'), (N'RefreshTokens'),
    -- Identity (baja rotación)
    (N'Users'), (N'UserRoles'), (N'UserClaims'), (N'UserLogins'), (N'RoleClaims'), (N'UserTokens');

DECLARE @t SYSNAME, @sql NVARCHAR(MAX);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @tables;
OPEN c;
FETCH NEXT FROM c INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER TABLE [dbo].' + QUOTENAME(@t) +
               N' SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [history].' + QUOTENAME(@t) +
               N', HISTORY_RETENTION_PERIOD = 90 DAYS));';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM c INTO @t;
END
CLOSE c;
DEALLOCATE c;
