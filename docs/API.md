# Contrato Single-Endpoint

Todas las acciones usan `POST /api`. Enviar JSON `{ "action": "nombre.acción", "data": { ... } }`. Todas salvo `auth.register` y `auth.login` requieren `Authorization: Bearer <accessToken>`.

No existe una ruta HTTP por acción. `GET /healthz` se reserva para la plataforma de despliegue.

Las respuestas mantienen los objetos usados por React: listas `{ items, total, page, pageSize }`, creación/actualización devuelve el registro. Página entre 1 y 100000; tamaño entre 1 y 100. IDs son UUID. Fechas ISO 8601 en UTC. Los campos opcionales pueden ser `null`.

| Acción | Permiso | `data` |
| --- | --- | --- |
| `auth.register` | Público | `email`, `password` (8–128 caracteres), `fullName`, `activationMode?` (`NONE`/`NUMBER`/`REVIEW`), `certificateNumber?`; multipart con archivo si `REVIEW` |
| `auth.login` | Público | `email`, `password` |
| `auth.me` | Autenticado | `{}`; devuelve usuario actual |
| `auth.logout` | Autenticado | `{}`; revoca todas sus sesiones |
| `members.list` | Activo/admin | `page?`, `pageSize?`; nombres y rangos de miembros activos, sin correo |
| `users.list` | Admin | `role?`, `page?`, `pageSize?` |
| `users.activate` | Admin | `id`, `reviewNote?`; aprueba cuenta y su revisión pendiente |
| `users.deactivate` | Admin | `id`, `reviewNote?`; desactiva y revoca sesiones |
| `users.setRole` | Superadmin | `id`, `role`: `REGISTRADOR` o `SOLDADO_ACTIVE` |
| `certificates.list` | Admin | `page?`, `pageSize?` |
| `certificates.create` | Admin | `certificateNumber`, `issuedToName?`, `issuedToEmail?` |
| `activation.claim` | Pendiente | `certificateNumber`; devuelve `{ message, user }` |
| `activation.submit` | Pendiente | Multipart: `certificateNumber` + archivo; devuelve `{ id, status, message }` |
| `activation.status` | Autenticado | `page?`, `pageSize?`; devuelve `{ user, reviews }` |
| `certificateReviews.list` | Admin | `status?` (por defecto `PENDING`), `page?`, `pageSize?` |
| `certificateReviews.review` | Admin | `id`, `status`: `APPROVED`/`REJECTED`, `reviewNote?` |
| `missions.list` | Activo/admin | `page?`, `pageSize?`; soldados ven publicadas y de rango permitido |
| `missions.create` | Admin | `title`, `description`, `missionType?`, `minimumRankCode?`, `genderEligibility?`, `badgeWeight?` |
| `missions.update` | Admin | `id` y mismos campos de creación; solo borradores |
| `missions.publish` | Admin | `id`; `DRAFT` → `PUBLISHED` |
| `missions.archive` | Admin | `id`; `DRAFT`/`PUBLISHED` → `ARCHIVED` |
| `submissions.create` | Activo/admin | Multipart: `missionId`, `submissionNote?` + archivo |
| `submissions.list` | Activo/admin | `status?`, `page?`, `pageSize?`; soldados ven sus propias evidencias |
| `submissions.review` | Admin | `id`, `status`: `APPROVED`/`REJECTED`, `reviewNote?` |
| `progress.get` | Activo/admin | `{}`; `{ rankCode, totalBadgeWeight, completedMissionTotal }` |
| `history.get` | Activo/admin | `page?`, `pageSize?`; `{ completedMissionTotal, history: { items, total, page, pageSize } }` |
| `sectReports.create` | Activo/admin | `sectName`, `locationDescription`, `referenceNote` |
| `sectReports.list` | Activo/admin | `status?` (por defecto `PENDING`), `page?`, `pageSize?`; solo propios para soldados |
| `sectReports.review` | Admin | `id`, `status`: `APPROVED`/`REJECTED`, `reviewNote?` |
| `sectRegistry.list` | Activo/admin | `page?`, `pageSize?`; reportes aprobados |
| `overview.get` | Admin | `{}`; métricas del dashboard |
| `files.get` | Propietario/admin | `id`; `{ id, name, contentType, base64 }` |

Admin significa `SUPER_ADMIN` o `REGISTRADOR`. Un registrador no puede asignar roles. Ningún administrador puede modificar su propia cuenta mediante estas acciones ni desactivar a otro administrador. Las cuentas inactivas solo pueden consultar `auth.me` o cerrar sesión si conservan una sesión válida; la desactivación explícita las revoca.

`missionType`: `OPERACIONAL` (por defecto), `FORMATIVA`, `ESPIRITUAL`. Rango mínimo: `RECRUTA` (por defecto), `SOLDADO`, `CABO`, `SARGENTO`. Género: `ALL`. Peso de badge: 1–100, por defecto 1. Publicación y estado de revisión se asignan en el servidor.

Límites de texto: nombre de persona 160; correo 254; certificado 100; título/nombre de secta 180; descripción de misión/referencia de reporte 4000; ubicación 1000; notas de revisión/evidencia 2000. `password` conserva espacios literales; el resto de textos se recorta. Correo normalizado a minúsculas y números de certificado a mayúsculas.

## Subir evidencia o certificado

El formulario multipart tiene **solo tres campos**: `action`, `data` (texto JSON) y `file` (un archivo).

```javascript
const form = new FormData();
form.append('action', 'submissions.create');
form.append('data', JSON.stringify({
  missionId: 'UUID_DE_LA_MISION',
  submissionNote: 'Actividad realizada con el equipo'
}));
form.append('file', selectedFile);
const response = await fetch(`${API}/api`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${token}` },
  body: form
});
```

No definir `Content-Type` manualmente en multipart: el navegador añade el boundary. JPG/PNG/WebP/GIF/PDF, máximo 5 MB; la firma se comprueba y los bytes se guardan dentro de la misma transacción. Ante un fallo, no queda un archivo nuevo huérfano. Los archivos anteriores a un reenvío se conservan; no se eliminan automáticamente.

Para certificados usar `action='activation.submit'` y `data={ certificateNumber:'S/N' }`. `users.list` incluye `certificateFileId` si tiene una revisión pendiente; `certificateReviews.list` y `submissions.list` incluyen `fileId`. Descargar esos IDs con `files.get`; no existen URLs públicas de archivos.

El frontend registra y activa en una sola solicitud `auth.register`: `activationMode=NUMBER` para validar un número, o `REVIEW` para adjuntar un certificado. Ambos procesos comparten transacción; si el certificado falla, no queda una cuenta parcial y puede corregirse el formulario. Sin `activationMode`, el registro crea una cuenta pendiente y admite usar las acciones de activación posteriormente. El cliente React acepta `fetchApi(action, { data, file, signal })` y construye el JSON o multipart automáticamente.

## Revisar y aprobar

```javascript
// Estas acciones pueden usarse desde una futura pantalla de moderación.
const pending = await fetchApi('submissions.list', {
  data: { status: 'PENDING', page: 1, pageSize: 50 }
});
await fetchApi('submissions.review', {
  data: { id: pending.items[0].id, status: 'APPROVED', reviewNote: 'Evidencia verificada' }
});
// Aprobar hace que se sumen los puntos y se recalcule el rango una sola vez.
await fetchApi('sectReports.review', {
  data: { id: 'UUID_DEL_REPORTE', status: 'APPROVED' }
});
```

## Errores

```json
{
  "error": "VALIDATION_ERROR",
  "message": "title es obligatorio.",
  "traceId": "identificador-de-la-solicitud"
}
```

| HTTP | Significado |
| --- | --- |
| 400 | JSON/campos/acción inválidos o referencia inexistente |
| 401 | Credenciales, sesión inválida o vencida |
| 403 | Cuenta inactiva o permisos insuficientes |
| 404 | Registro inexistente o archivo ajeno |
| 409 | Duplicado, certificado no disponible, estado incompatible o revisión repetida |
| 413 | Solicitud demasiado grande |
| 415 | Usar JSON o multipart |
| 429 | 10 intentos de login/registro por correo cada 15 minutos, o límite de concurrencia |
| 500 | Fallo interno; detalles solo en logs del servidor |
| 503 | PostgreSQL no disponible |

El límite de intentos de autenticación vive en memoria de cada instancia y se reinicia con ella. La API limita a 32 solicitudes simultáneas por instancia. Las respuestas tienen `Cache-Control: no-store`. Usar HTTPS de Render para las credenciales y sesiones.

La guía anterior `endpoints.txt` describe otro backend REST/Express. Este contrato y los módulos C# son la referencia para esta implementación; no se incluyen las rutas antiguas, squads ni un sistema de amistades porque las pantallas actuales no permiten gestionar esos flujos.
