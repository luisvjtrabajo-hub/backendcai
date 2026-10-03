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
| `missions.list` | Activo/admin | `page?`, `pageSize?`; soldados ven publicadas y de rango permitido; `myAssignment` incluye su ID y estado, sin archivos |
| `missions.assign` | Activo/admin | `id` de misión; se asigna al usuario actual sin archivo; reintentar devuelve la misma asignación |
| `assignments.list` | Activo/admin | `missionId?`, `page?`, `pageSize?`; soldados ven sus asignaciones y estados, sin evidencias |
| `missions.create` | Admin o rango 5+ activo | `title`, `description`, `evidenceRequirement`, `missionType?`, `minimumRankCode?`, `badgeWeight?`, `fieldMission?`; crea borrador |
| `missions.update` | Admin o autor de rango 5+ | `id` y mismos campos de creación; solo borradores propios, sin cambiar catálogo oficial |
| `missions.publish` | Admin | `id`; `DRAFT` → `PUBLISHED` |
| `missions.archive` | Admin | `id`; `DRAFT`/`PUBLISHED` → `ARCHIVED` |
| `missions.delete` | Admin | `id`; devuelve `{ id, deleted: true }`. Retira cualquier misión del catálogo, incluso archivada; ID inexistente o ya eliminado: 404 |
| `submissions.create` | Activo/admin | `missionId`, `submissionNote`, `occurredAt` ISO con zona, `respectConfirmed=true`, `privacyConfirmed=true`; archivo opcional, requisitos adicionales según misión; ver abajo |
| `submissions.list` | Admin | `status?`, `missionId?`, `page?`, `pageSize?`; consulta de evidencias exclusiva para administradores |
| `submissions.review` | Admin con autoridad de validación | `id`, `status`, `requirementsVerified=true` al aprobar; nota obligatoria al rechazar; bonos/sanciones descritos abajo |
| `progress.get` | Activo/admin | `{}`; rango, escudo, lema, puntos, próximo rango/hito, Hospitalidad, ingreso, reserva y puntos por área |
| `history.get` | Activo/admin | `page?`, `pageSize?`; `{ completedMissionTotal, history: { items, total, page, pageSize } }` |
| `sectReports.create` | Activo/admin | `sectName`, `locationDescription`, `referenceNote` (doctrina/fuentes), `latitude?`, `longitude?`; coordenadas deben venir juntas |
| `sectReports.list` | Activo/admin | `status?` (por defecto `PENDING`), `page?`, `pageSize?`; solo propios para soldados |
| `sectReports.review` | Admin | `id`, `status`: `APPROVED`/`REJECTED`, `reviewNote?` |
| `sectRegistry.list` | Activo/admin | `page?`, `pageSize?`; reportes aprobados |
| `overview.get` | Admin | `{}`; métricas del dashboard |
| `files.get` | Admin para evidencias; propietario/admin para certificados | `id`; `{ id, name, contentType, base64 }`; el soldado no puede descargar evidencias ni siquiera propias |
| `ranks.get` | Activo/admin | `{}`; los diez rangos y diez hitos |
| `profile.update` | Propietario/admin | `birthDate`; admin puede indicar `userId`, `parentalConsentVerified`, `sponsorId?`, `reserve`, `reviewNote` obligatoria y `liftSanction?` solo superadmin |
| `milestones.validate` | Admin autorizado | `userId`, `code`, `requirementsVerified`, `reviewNote`, archivo PDF y declaraciones del hito; ver guía |
| `points.list` | Activo/admin | Historial privado propio, paginado, con bonos y sanciones |
| `evidence.upload` | Activo/admin | Archivo privado en multipart; devuelve `{ id }`; permite cargar previamente una invitación |
| `activity.list` | Admin | Alertas de 60/120 días, paginadas |
| `conduct.create` | Activo/admin | `targetId`, `note`; reporte privado al Capítulo |
| `conduct.list` | Admin | Reportes privados, paginados |
| `conduct.review` | Superadmin | `id`, `upheld`, `reviewNote`; confirmar humillación retira el rango |

Admin significa `SUPER_ADMIN` o `REGISTRADOR`. Un registrador no puede asignar roles. Ningún administrador puede modificar su propia cuenta mediante estas acciones ni desactivar a otro administrador. Las cuentas inactivas solo pueden consultar `auth.me` o cerrar sesión si conservan una sesión válida; la desactivación explícita las revoca.

`missionType`: `OPERACIONAL` (por defecto), `FORMATIVA`, `ESPIRITUAL`. Las propias operacionales son de campo. Rango mínimo de una misión propia: `CABALLERO_TEMPLE` por defecto; códigos válidos en `ranks.get`. `badgeWeight` representa puntos, 1–1000 en misiones propias; las oficiales conservan sus valores del libro. Género sigue `ALL`. Publicación y revisiones se asignan en el servidor. Reglas detalladas y decisiones de interpretación: [RANGOS_Y_MISIONES.md](RANGOS_Y_MISIONES.md).

Límites de texto: nombre de persona 160; correo 254; certificado 100; título/nombre de secta 180; descripción de misión/referencia de reporte 4000; ubicación 1000; notas de revisión/evidencia 2000. `password` conserva espacios literales; el resto de textos se recorta. Correo normalizado a minúsculas y números de certificado a mayúsculas.

## Subir evidencia o certificado

La eliminación es lógica y auditada: bloquea nuevas asignaciones, reportes, ediciones y publicaciones. Conserva las asignaciones, los reportes, los archivos, los puntos y los hitos históricos; los administradores pueden terminar de revisar reportes pendientes mediante `submissions.list`/`submissions.review`. Las misiones eliminadas desaparecen de `missions.list` para todos los roles y no se recrean al importar el catálogo. No hay acción de restauración.

El soldado primero se asigna una misión con `fetchApi('missions.assign', { data: { id: missionId } })`, sin archivo. La asignación se conserva al cerrar sesión o recargar. Puede subir la evidencia posteriormente con `submissions.create`. Solo administradores pueden consultar evidencias. El historial personal y las asignaciones incluyen estados, sin archivos, URLs ni notas de evidencia.

El formulario multipart tiene **solo tres campos**: `action`, `data` (texto JSON) y `file` (un archivo).

```javascript
const form = new FormData();
form.append('action', 'submissions.create');
form.append('data', JSON.stringify({
  missionId: 'UUID_DE_LA_MISION',
  submissionNote: 'Bitácora detallada de la actividad realizada con el equipo',
  occurredAt: new Date().toISOString(),
  respectConfirmed: true,
  privacyConfirmed: true
}));
form.append('file', selectedFile);
const response = await fetch(`${API}/api`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${token}` },
  body: form
});
```

Se requiere fecha de nacimiento registrada. Sin archivo o enlace, las misiones que admiten bitácora necesitan al menos 30 caracteres. `honorReport` aplica únicamente en VIG-02/03/05 y nunca genera un hito verificado. `evidenceUrl` debe usar HTTPS. `recordingIncluded=true` exige `recordingConsent=true`.

Campo requiere `companionId` de otro miembro activo, `endedAt` con zona, `safeFieldConfirmed=true`, `noVulnerableTargets=true`. El compañero es opcional en otras actividades, para acreditar trabajo en equipo. FOR-04 requiere `moduleCode`: `CREDO`/`SACRAMENTOS`/`VIDA`/`ORACION`. VIG-05 requiere `linkedMissionId`. CAR-01 requiere `sectReportId` propio aprobado con coordenadas y fotografía. DEB-04 y EST-05 requieren `invitationFileId`: cargar antes PDF con `evidence.upload`. `mentionsMinors` marca revisión especial.

La revisión puede declarar `excellent`, `teamBonus` o `firstRegistryBonus` (este último con `sectReportId`). El tope incluye esos bonos. Rechazos usan `rejectionReason`: `OTHER`, `DISRESPECT`, `FALSE_EVIDENCE`. `foundingValidation=true` necesita nota y solo permite al superadministrador suplir un validador cuando no existe administrador con rango suficiente. Un actor no puede revisar sus propios reportes/hitos.

Hitos manuales: HIT-ING exige `interviewVerified`, `referenceMemberId`; HIT-CAB, `doctrinalExamVerified`, `ledFieldMissionVerified`; HIT-COM, `localCommandVerified`; HIT-MAR, `distinctLocationsVerified`; HIT-GM, `chapterElectionVerified` y rol superadmin distinto del candidato. Los demás requisitos se comprueban contra misiones validadas. HIT-DOM/PROX/FUN son automáticos y no admiten acta que sustituya sus misiones.

No definir `Content-Type` manualmente en multipart: el navegador añade el boundary. JPG/PNG/WebP/GIF/PDF, máximo 5 MB; la firma se comprueba y los bytes se guardan dentro de la misma transacción. Ante un fallo, no queda un archivo nuevo huérfano. Los archivos anteriores a un reenvío se conservan; no se eliminan automáticamente.

Para certificados usar `action='activation.submit'` y `data={ certificateNumber:'S/N' }`. `users.list` incluye `certificateFileId` si tiene una revisión pendiente; `certificateReviews.list` y `submissions.list` incluyen `fileId`. Descargar esos IDs con `files.get`; no existen URLs públicas de archivos.

El frontend registra y activa en una sola solicitud `auth.register`: `activationMode=NUMBER` para validar un número, o `REVIEW` para adjuntar un certificado. Ambos procesos comparten transacción; si el certificado falla, no queda una cuenta parcial y puede corregirse el formulario. Sin `activationMode`, el registro crea una cuenta pendiente y admite usar las acciones de activación posteriormente. El cliente React acepta `fetchApi(action, { data, file, signal })` y construye el JSON o multipart automáticamente.

## Revisar y aprobar

```javascript
// La pantalla Misiones ya contiene la moderación para administradores.
const pending = await fetchApi('submissions.list', {
  data: { status: 'PENDING', page: 1, pageSize: 50 }
});
await fetchApi('submissions.review', {
  data: { id: pending.items[0].id, status: 'APPROVED', requirementsVerified: true, reviewNote: 'Evidencia verificada' }
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
