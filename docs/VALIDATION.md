# Verificación realizada

Eliminación de misiones, 3 de octubre de 2026:

- Frontend y backend compilan correctamente. `scripts/smoke.mjs`: **178 verificaciones** correctas con PostgreSQL 16.10 y API real; incluye eliminación por superadministrador y registrador, rechazo a soldados, borradores/publicadas/archivadas, ausencia en el catálogo y su total, bloqueo de nuevas asignaciones/reportes y de restauración por publicación/edición, conservación de puntos/historial/archivos y revisión de reportes pendientes.
- `scripts/migration-smoke.sql`: migración 005 repetible; volver a importar el catálogo no restaura una misión oficial eliminada, ni cambia las asignaciones o los puntos anteriores.
- La migración `005_mission_deletion.sql` se incluye en la publicación .NET y se aplica al arrancar con `Database__AutoMigrate=true`. Verificación local; sin despliegue a Render/Vercel ni revisión visual en navegador.

Fecha: 2 de octubre de 2026.

Sistema del workbook `CAI_Sistema_de_Rangos_y_Misiones.xlsx`:

- .NET compila sin advertencias ni errores; frontend compila con Vite correctamente.
- `scripts/smoke.mjs`: **142 verificaciones** de API correctas con PostgreSQL 16.10 real, incluyendo revisión concurrente única, asignación sin archivo, permisos y progreso sin ascensos por puntos solos.
- `scripts/ranks-smoke.mjs`: **406 verificaciones** correctas sobre el resultado publicado. Contrasta los diez rangos y las 53 misiones con el catálogo extraído; comprueba los nueve umbrales justo antes y al alcanzarlos, consultas simultáneas durante cada ascenso, hitos DOM/PROX/FUN, módulos distintos, Hospitalidad reciente, honor decimal, topes incluyendo calidad/primicia, bono de equipo en campo y formación, constancia, menores, fechas de nacimiento inmutables, invitación, horario/compañero/consentimiento, sanciones, reserva y validadores fundacionales/ordinarios. Los historiales complejos se preparan como fixtures solo en `cai_test` local; las decisiones se ejercitan por HTTP. Ejecutar esta batería de forma aislada, para que otra prueba no modifique los conteos durante la comprobación de migraciones.
- `scripts/migration-smoke.sql`: actualización desde datos del esquema anterior, rango antiguo archivado, puntos y asignaciones conservados, 53 misiones sin duplicados y reejecución de 003/004 que conserva rangos ya ganados.
- `scripts/activity-smoke.sql`: el servicio real, al reiniciar, creó alerta de 61 días y reserva de 121, conservando rangos y los 500 puntos del fixture.
- No se desplegó a Render/Vercel ni se pudo realizar una revisión visual con navegador conectado. El Docker daemon no está disponible; se verifica la publicación .NET y la inclusión de los cuatro scripts SQL.
- Los exámenes se acreditan mediante evidencia revisada: el workbook no contiene preguntas o soluciones para un motor de evaluación.

Las verificaciones previas se conservan debajo como historial; sus reglas antiguas de cuatro rangos fueron sustituidas por este sistema.

Actualización de asignaciones y permisos de evidencias:

- `scripts/smoke.mjs`: **141 verificaciones de integración correctas** con PostgreSQL real. Incluye asignación sin archivo, asignaciones simultáneas sin duplicados, conservación al iniciar otra sesión, comprobación de rango, subida posterior, estados de tarjeta y bloqueo de consulta/descarga de evidencias para soldados, incluso del archivo propio.
- `scripts/migration-smoke.sql`: actualización desde `001` correcta, con datos anteriores; `002` repetido sin duplicar asignaciones ni perder estados aprobados. Archivos actuales y anteriores de evidencias quedan protegidos; certificados conservan su clasificación.
- Frontend recompilado correctamente. La tarjeta utiliza acciones de ancho completo y texto ajustable; la subida se abre por separado. No hubo navegador conectado disponible para una revisión visual interactiva.
- Los scripts `001_schema.sql` y `002_mission_assignments.sql` se copian al resultado publicado y se ejecutan en orden al arrancar con `Database:AutoMigrate=true`.

Las verificaciones originales del primer backend se conservan debajo como referencia.

- .NET SDK 10.0.401 / runtime 10.0.12, Windows: compilación sin errores ni advertencias, restore con lockfile y publicación Release correctos.
- API ejecutada desde el resultado publicado; PostgreSQL 16.10 local, base aislada.
- `scripts/smoke.mjs`: **107 verificaciones correctas**. Incluye registro con certificado atómico, rollback y reintento; autorización; titularidad y uso único de certificados; revisión manual; archivos privados; misiones; reenvío tras rechazo; dos aprobaciones simultáneas con un solo éxito; progreso y rango; reportes; roles; revocación de sesiones; CORS y límite de intentos.
- El SQL se volvió a ejecutar en la misma base con `ON_ERROR_STOP=1`: correcto, conservando los registros.
- Frontend: `npm run build` correcto después de migrar las llamadas a `POST /api`.
- `docker compose --env-file .env.example config --quiet`: correcto.
- `render.yaml`: validación correcta con el JSON Schema oficial descargado de `https://render.com/schema/render.yaml.json`, usando un validador compatible con draft 2020-12.
- `git diff --check`: correcto.

La verificación local de .NET usó `NuGetAudit=false` porque la primera consulta a NuGet no estaba disponible en el sandbox. El Dockerfile conserva la restauración normal y la auditoría de NuGet.

No se construyó ni arrancó la imagen Docker: el motor Docker de este equipo no estaba activo. No se desplegaron recursos en Render ni se cambió el frontend ya publicado en Vercel. Las pruebas usaron únicamente recursos locales; los procesos de pruebas fueron detenidos y las descargas temporales retiradas.
