/*
 * Asigna el rol Admin a un usuario por email (bootstrap del primer administrador).
 * Cambia @email por el tuyo. También sirve para Tecnico/Usuario cambiando @role.
 *
 * Uso:  sqlcmd -S "(localdb)\MSSQLLocalDB" -d ClashPlanner -b -i grant-admin.sql
 */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @email NVARCHAR(256) = N'CAMBIA-ESTO@ejemplo.com';
DECLARE @role  NVARCHAR(50)  = N'Admin';   -- Admin | Tecnico | Usuario

DECLARE @uid NVARCHAR(450) = (SELECT Id FROM dbo.Users WHERE Email = @email);
DECLARE @rid NVARCHAR(450) = (SELECT Id FROM dbo.Roles WHERE Name = @role);

IF @uid IS NULL
    PRINT 'No existe ningún usuario con email ' + @email + ' (regístralo primero en la app).';
ELSE IF @rid IS NULL
    PRINT 'No existe el rol ' + @role + '.';
ELSE IF EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @uid AND RoleId = @rid)
    PRINT @email + ' ya tiene el rol ' + @role + '.';
ELSE
BEGIN
    INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (@uid, @rid);
    PRINT 'OK: ' + @email + ' ahora es ' + @role + '. (Debe volver a iniciar sesión para que el token recoja el rol.)';
END
