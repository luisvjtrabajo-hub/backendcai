# Verificación realizada

Fecha: 2 de octubre de 2026.

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
