# ClashPlanner — Backend (API)

API de **ClashPlanner**: sincroniza los datos del planificador entre dispositivos y
**proxea la API oficial de Clash of Clans** con un único token de servidor (el
usuario final nunca lo ve). Construida con ASP.NET Core 10 + EF Core (PostgreSQL/Npgsql) +
Identity (email/contraseña) + JWT con refresh tokens.

- **Stack:** ASP.NET Core 10 (`net10.0`) · EF Core (PostgreSQL / Npgsql) · ASP.NET Identity · JWT + refresh tokens
- **Solución:** `ClashPlanner.slnx` · API en `ClashPlanner.Api` · tests en `ClashPlanner.Api.Tests` (xUnit)

## Desarrollo (local)

Requisitos: .NET 10 SDK y un PostgreSQL local (lo más fácil: `docker compose up -d db`,
que levanta uno en `localhost:5432`).

```bash
cd ClashPlanner.Api
dotnet ef database update          # crea el esquema en el Postgres local (primera vez)
dotnet run --no-launch-profile     # http://localhost:5117  ·  Swagger en /swagger
```

La configuración de desarrollo (cadena de conexión Postgres y clave JWT de dev) va
en `appsettings.Development.json`. Tests:

```bash
cd ClashPlanner.Api.Tests
dotnet test
```

> Los tests desactivan la migración automática (`Database:Migrate=false`) porque
> crean el esquema con otro proveedor.

## Estructura

```
ClashPlanner.Api/
├── Program.cs            Arranque: DI, Identity, JWT, CORS, rate limit, seeding
├── Endpoints/            Endpoints mínimos por área
│   ├── AuthEndpoints.cs  register · login · refresh · logout · me
│   ├── SyncEndpoints.cs  pull/push del planificador (LWW + baseRevision)
│   ├── CocEndpoints.cs   token de servidor + proxy de jugador
│   └── AdminEndpoints.cs gestión de ajustes (tabla Settings)
├── Services/
│   ├── TokenService.cs        Emisión/validación de JWT y refresh tokens
│   ├── SyncService.cs         Lógica de fusión y revisiones
│   ├── CocService.cs          Proxy a la API de CoC (token de servidor)
│   └── AppSettingsService.cs  Lee/escribe Settings; cifra los secretos
├── Models/              Entidades (usuario, refresh token, sync, settings, roles)
├── Dtos/                Contratos de auth y sync
├── Data/AppDbContext.cs
└── Migrations/
docker-compose.yml       API + PostgreSQL para desarrollo local
```

## Configuración general (tabla `Settings`)

Buena parte de la configuración operativa vive en la tabla `Settings` (no en
ficheros), editable en caliente desde los endpoints de Admin:

| Clave | Tipo | Descripción |
|---|---|---|
| `Coc:Token` | secreto | Token de la API oficial. **Cifrado con Data Protection** y enmascarado al leerlo. Fuente de verdad del proxy. |
| `Coc:UseProxy` | bool | `true` → llamadas vía proxy de RoyaleAPI (IP fija); `false` → directo a CoC. |
| `Coc:ProxyUrl` / `Coc:DirectUrl` | url | URLs base del proxy y directa. |
| `Coc:TimeoutSeconds` | int | Timeout del proxy. |
| `RateLimit:CocPerMinute` | int | Límite por IP del proxy. **Se aplica al reiniciar.** |
| `Cors:Origins` | csv | Orígenes permitidos por CORS. **Se aplica al reiniciar.** |

En la primera ejecución, `Program.cs` siembra estos ajustes con los valores de
config (incluido `Coc:Token` si está presente en el entorno). `CocService` lee el
token de la BD con *fallback* a `config["Coc:Token"]`.

> **Nunca pongas el token en un fichero del repo.** Para sembrarlo la primera vez,
> usa una variable de entorno o user-secrets (`Coc:Token`); luego gestiónalo desde
> Admin, que lo re-cifra.

## Configuración (variables de entorno)

| Variable | Descripción |
|---|---|
| `ConnectionStrings__DefaultConnection` | Cadena de conexión a PostgreSQL (formato Npgsql; en Neon incluye `SSL Mode=Require`) |
| `Jwt__SigningKey` | Clave de firma JWT (**≥32 caracteres**; el arranque falla si no) |
| `Jwt__Issuer` / `Jwt__Audience` | Emisor/audiencia del JWT |
| `Cors__Origins__0` | Origen permitido por CORS (web) |
| `DataProtection__KeysPath` | Carpeta donde persisten las claves (contenedor: `/keys`) — para una sola instancia |
| `DataProtection__Store` | `Database` para compartir las claves entre **varias instancias** vía la BD (tiene prioridad sobre `KeysPath`) |
| `Database__Migrate` | Aplicar migraciones al arrancar (`true` por defecto) |

## Desarrollo local con Docker Compose

Levanta la API + PostgreSQL con datos y claves persistentes:

```bash
cp .env.example .env          # rellena POSTGRES_PASSWORD y JWT_SIGNING_KEY
docker compose up -d --build
```

- La API escucha en `http://localhost:5117` (puerto 8080 del contenedor).
- Las migraciones se aplican solas al arrancar (`Database:Migrate=true`).
- Las claves de Data Protection (cifran el token de CoC) persisten en el volumen
  `dp-keys`; los datos de Postgres en `pgdata`.

> **Producción:** la API se despliega en Render (ver `render.yaml`) y la BD es
> **PostgreSQL en Neon** (serverless, plan free). La cadena de conexión de Neon se
> pone como secreto `ConnectionStrings__DefaultConnection` en el panel de Render
> (formato Npgsql, con `SSL Mode=Require`). Este Compose es solo para local.

### Puesta en producción real
- **HTTPS:** Render termina TLS por ti; el contenedor habla HTTP en el 8080.
- **Secretos:** `JWT_SIGNING_KEY` y la cadena de conexión por variables de entorno /
  gestor de secretos; nunca en el repositorio (`.env` está en `.gitignore`).
- **CORS:** pon en `Cors:Origins` el dominio de la app web.
- **Escalado horizontal:** con varias réplicas tras un balanceador, usa
  `DataProtection__Store=Database` para que todas compartan las claves (si no, cada
  instancia tendría las suyas y no podría descifrar los tokens de las demás).
- **Clientes:** en el escritorio, fija `VITE_SYNC_URL` al dominio del API y añade
  ese dominio al `connect-src` de la CSP del cliente.

> **Nota histórica:** hasta jun-2026 la BD era Azure SQL con tablas temporales
> (system-versioning) y un `sql/history-retention.sql` para acotar el historial. Al
> migrar a PostgreSQL (Neon) se eliminó el historial automático (Postgres no tiene
> system-versioning nativo). Si se quisiera reintroducir auditoría, sería con tablas
> `history` + triggers o la extensión `temporal_tables`.

## Endpoints

- **Auth:** `POST /auth/register` · `POST /auth/login` · `POST /auth/refresh` · `POST /auth/logout` · `POST /auth/logout-all` (cierra todas las sesiones; requiere sesión) · `GET /auth/me`. Refresh tokens rotatorios por familia: caducidad por inactividad + deadline absoluto; el reuso de un token rotado revoca toda la familia.
- **Sync:** `GET /sync` (pull) · `POST /sync` (push con `baseRevision`; `409` en conflicto)
- **CoC (proxy público, con rate limit por IP):** `GET /coc/player?tag=…` · `GET /coc/clan?tag=…` · `GET /coc/clan/currentwar?tag=…` · `GET /coc/clan/warlog?tag=…` · `GET /coc/clan/capitalraids?tag=…` · `GET /coc/clan/leaguegroup?tag=…` · `GET /coc/clanwar?warTag=…`. El token de servidor se gestiona en `/admin/settings` (no hay endpoints `/coc/token`).
- **Admin:** gestión de la tabla `Settings` (leer: `Admin`/`Técnico`; editar: solo `Admin`) y de usuarios/roles (solo `Admin`)
- **Salud:** `GET /health`

## Roles

Se siembran al arrancar: `Admin`, `Técnico`, `Usuario`.

## Licencia

Ver [LICENSE](LICENSE).
