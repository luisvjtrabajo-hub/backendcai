# Backend CAI

API en **ASP.NET Core / .NET 10 LTS**, PostgreSQL 16 y Docker. Todo el backend está en `backendcai/`, en la carpeta `cai` exterior, junto a la carpeta `cai/` del frontend. El backend queda fuera del repositorio Git del frontend. Las pantallas de React ya llaman al nuevo contrato; debes volver a desplegarlas en Vercel.

```text
cai/                  # Carpeta exterior del proyecto
├── backendcai/        # Backend .NET, Docker, SQL y documentación
└── cai/               # Frontend y su repositorio Git
```

## Contrato e implementación

Solo hay dos rutas HTTP:

| Ruta | Uso |
| --- | --- |
| `POST /api` | Todas las operaciones mediante `{ "action": "...", "data": { ... } }` |
| `GET /healthz` | Comprueba API y conexión PostgreSQL; configurar como health check en Render |

Ejemplo de registro:

```json
{
  "action": "auth.register",
  "data": {
    "email": "persona@example.com",
    "password": "una-contraseña-larga",
    "fullName": "Nombre Apellido"
  }
}
```

El login y el registro devuelven `{ accessToken, expiresAt, user }`. Para las otras operaciones enviar `Authorization: Bearer <accessToken>`. Son **tokens opacos aleatorios**, no JWT; la base guarda solo su SHA-256. Cada solicitud comprueba el rol actual y el vencimiento. La sesión dura 24 horas; `Auth__SessionHours` permite 1–168 horas. El logout revoca todas las sesiones de la cuenta. Las contraseñas usan `PasswordHasher` de ASP.NET Core Identity (PBKDF2 con sal).

El formulario de registro envía el certificado junto a `auth.register` mediante `activationMode=NUMBER` o `REVIEW`. Registro y activación/revisión comparten transacción; un certificado inválido no deja una cuenta parcial que impida reintentar.

Arquitectura: `Api/` define contrato y dispatcher; `Modules/` contiene reglas por dominio; `Infrastructure/` encapsula conexiones, transacciones y arranque; `database/` contiene el esquema. `IActionHandler` y `IDatabase` mantienen una interfaz pequeña. El dispatcher usa una lista explícita de acciones; no ejecuta métodos, tablas ni SQL elegidos por el cliente. Cada solicitud usa una transacción y parámetros SQL. Las decisiones administrativas generan una auditoría.

Consulta [docs/API.md](docs/API.md) para las acciones y ejemplos de revisión.

## Desarrollo local con Docker

Desde `backendcai/`:

```powershell
Copy-Item .env.example .env
# Editar .env: contraseña PostgreSQL y credenciales del administrador.
docker compose up --build -d
docker compose logs -f api
```

La API escucha en `http://localhost:8080`. PostgreSQL queda en la red privada de Docker; sus datos usan el volumen `postgres-data`. El script SQL se ejecuta automáticamente al arrancar la API y puede repetirse. `docker compose down` detiene los contenedores y conserva el volumen.

En la raíz del frontend:

```powershell
Copy-Item .env.example .env
npm ci
npm run dev
```

Vite envía `/api` a `VITE_API_URL`, por defecto `http://localhost:8080`. No elimina `/api` al pasar por el proxy.

Sin Docker, usar un PostgreSQL propio y variables de entorno:

```powershell
$env:ConnectionStrings__Database = 'Host=localhost;Port=5432;Database=cai;Username=cai;Password=TU_PASSWORD'
$env:Cors__AllowedOrigins__0 = 'http://localhost:5173'
$env:Bootstrap__AdminEmail = 'admin@example.com'
$env:Bootstrap__AdminPassword = 'TU_PASSWORD_ADMIN_DE_AL_MENOS_12_CARACTERES'
$env:PORT = '8080'
dotnet run --project Cai.Api.csproj
```

## Desplegar en Render

### Opción A: Blueprint

1. Publicar el contenido de `backendcai/` como raíz de un repositorio Git propio para el backend. El repositorio actual del frontend no incluye esta carpeta hermana.
2. En Render, crear un **Blueprint**, conectar el repositorio del backend y seleccionar `render.yaml` como Blueprint Path.
3. Completar `Cors__AllowedOrigins__0` con el origen real del frontend, por ejemplo `https://tu-front.vercel.app`, **sin barra final ni ruta**. Completar `Bootstrap__AdminEmail` y `Bootstrap__AdminPassword` con credenciales nuevas; el password admite 12–128 caracteres.
4. Revisar los planes antes de crear los recursos: el YAML propone web service `free` y PostgreSQL **`0.1c-256mb`, que es de pago**, con 1 GB de almacenamiento. La base restringe conexiones externas por defecto y comparte región con la API. Puedes cambiar planes según tu cuenta.
5. Render construye el Dockerfile, conecta `DATABASE_URL` desde la base y usa `/healthz`. La API escucha en `0.0.0.0` usando el `PORT` de Render.

### Opción B: recursos manuales

1. Crear **Render PostgreSQL** y copiar su **Internal Database URL**. Render crea la base; el SQL crea tablas dentro de ella.
2. Crear un **Web Service** desde el repositorio con lenguaje/runtime **Docker**.
3. Si el repositorio contiene directamente el contenido de `backendcai/`, dejar **Root Directory vacío**, usar **Dockerfile Path: `./Dockerfile`** y **Docker Build Context: `.`**. No hace falta Build Command ni Start Command: los define Docker.
4. Configurar las variables siguientes y el health check `/healthz`.

| Variable | Valor |
| --- | --- |
| `DATABASE_URL` | Internal Database URL de PostgreSQL de Render, `postgresql://...` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Cors__AllowedOrigins__0` | Origen exacto de Vercel, sin barra final |
| `Bootstrap__AdminEmail` | Correo del primer administrador |
| `Bootstrap__AdminPassword` | Contraseña propia de 12–128 caracteres |
| `Bootstrap__AdminName` | Opcional; por defecto `Administrador CAI` |
| `Database__AutoMigrate` | Opcional; `true` por defecto |
| `Auth__SessionHours` | Opcional; `24` por defecto |

Para permitir otro origen, agregar `Cors__AllowedOrigins__1`, etc. No usar `*`. No configurar `PORT` en Render: la plataforma lo asigna. `DATABASE_URL` admite la URL PostgreSQL de Render; también se admite `ConnectionStrings__Database` en formato Npgsql. Las URLs usan SSL `Require` por defecto; para pruebas locales se puede usar `?sslmode=disable`.

El administrador se crea una sola vez. Reiniciar no cambia su password. Un correo que ya pertenece a un usuario sin rol `SUPER_ADMIN` provoca un error de arranque en vez de promoverlo automáticamente. Después del primer despliegue puedes quitar **ambas** variables de bootstrap. No existe un usuario/password universal ni se almacena el password en el SQL. Desde `users.setRole`, el superadministrador puede convertir otra cuenta en `REGISTRADOR`.

Si más adelante publicas la carpeta exterior completa como un solo repositorio, usar `backendcai/render.yaml` como Blueprint Path y cambiar las rutas del YAML a `dockerfilePath: ./backendcai/Dockerfile` y `dockerContext: ./backendcai`. Para un servicio manual en ese repositorio, usar Root Directory `backendcai`.

### Crear el esquema manualmente

Script: [database/001_schema.sql](database/001_schema.sql).

Puedes ejecutarlo en DBeaver, pgAdmin o `psql`, conectado a la base de Render. Para conectar desde tu equipo, habilita temporalmente tu IP en la configuración de acceso de PostgreSQL y usa la **External Database URL**:

```powershell
# DATABASE_URL debe contener la URL externa de tu base.
psql "$env:DATABASE_URL" -v ON_ERROR_STOP=1 -f database/001_schema.sql
```

El script es transaccional, idempotente y no borra registros. No lleva `CREATE DATABASE`: Render provisiona la base previamente. Si prefieres ejecutar SQL manualmente, establece `Database__AutoMigrate=false` después de aplicar el script. Futuras modificaciones de esquema deben añadirse como migraciones nuevas; este primer script no es un motor de migraciones versionadas.

## Conectar Vercel

En el proyecto del frontend, configurar:

```dotenv
VITE_API_URL=https://TU-BACKEND.onrender.com
```

Usar solo el origen, **sin `/api` ni barra final**. Volver a desplegar el frontend: Vite incorpora esa variable durante el build. Configurar ese mismo origen de Vercel en CORS del backend. Las sesiones del servidor anterior no son compatibles; cerrar sesión e iniciar nuevamente.

El cliente usa siempre `POST /api`; `fetchApi(action, { data, file, signal })` envía JSON o multipart. Se actualizaron las llamadas de las pantallas, el registro transaccional, el proxy de Vite, la consulta de aprobación y la descarga autenticada del certificado para que el administrador pueda revisarlo. Las imágenes de ejemplo, mapas y especialidades ficticias que ya tenía el frontend siguen siendo contenido de presentación; la API no las convierte en información real.

## Archivos y reglas

- Certificados y evidencias admiten JPG, PNG, WebP, GIF y PDF de hasta **5 MB**, comprobando la firma del formato. El máximo de la solicitud es 6 MB. No se admiten SVG, ejecutables ni video.
- Los archivos se guardan en PostgreSQL (`bytea`) y se entregan con `files.get`, únicamente al propietario o administradores. No hace falta un disco persistente en Render. El contenido ocupa espacio en la base; para gran volumen conviene sustituir este almacenamiento por un bucket privado.
- Un certificado solo puede consumirse una vez. La activación automática exige correo del titular coincidente; si no se registró correo, exige nombre coincidente. Los certificados sin titular requieren revisión manual.
- Las cuentas nuevas quedan pendientes; el servidor controla roles y evita elevar permisos desde el registro.
- Una evidencia por usuario/misión. Un rechazo permite reenviar; una aprobación no puede repetirse. Solo las aprobaciones suman peso y rango: RECRUTA 0–2, SOLDADO 3–9, CABO 10–19, SARGENTO 20+.
- Se respeta el rango mínimo de misiones. La versión actual usa `genderEligibility=ALL`, acorde al formulario actual, que no recoge género.
- Los reportes pendientes son visibles al autor y administradores; los aprobados aparecen en el registro de usuarios activos.
- Las revisiones de misiones y reportes están disponibles en la API documentada; el frontend actual no tiene todavía pantallas para esas dos revisiones.

## Verificaciones

```powershell
dotnet build Cai.Api.csproj -c Release
dotnet publish Cai.Api.csproj -c Release -o artifacts/publish
# Desde la raíz del frontend:
npm run build
```

Para integración, arrancar una API y PostgreSQL **de pruebas**, con CORS local. El script crea datos nuevos con un UUID y no borra registros:

```powershell
$env:TEST_API_URL = 'http://localhost:8080'
$env:TEST_ADMIN_EMAIL = 'EL_CORREO_ADMIN_DE_PRUEBAS'
$env:TEST_ADMIN_PASSWORD = 'EL_PASSWORD_ADMIN_DE_PRUEBAS'
node scripts/smoke.mjs
```

Cubre registro, login, CORS, autorización, errores, certificados, archivos, aprobación manual, borradores, publicaciones, reenvío de evidencias, revisión concurrente, rangos, privacidad de reportes, promoción a registrador, desactivación, logout y límite de intentos. Las pruebas persisten datos; usar una base aislada.

En una red sin acceso a NuGet, se puede compilar usando los paquetes ya disponibles y `-p:NuGetAudit=false --ignore-failed-sources`; eso **omite únicamente la consulta de vulnerabilidades durante esa verificación local**. El Dockerfile conserva la auditoría y el lockfile para el despliegue.

Referencias oficiales: [.NET y soporte LTS](https://dotnet.microsoft.com/en-us/platform/support/policy), [Docker en Render](https://render.com/docs/docker), [Blueprints](https://render.com/docs/blueprint-spec), [PostgreSQL en Render](https://render.com/docs/postgresql-creating-connecting).
