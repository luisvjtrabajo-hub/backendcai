# Sistema del Excel integrado

Fuente: `../../CAI_Sistema_de_Rangos_y_Misiones.xlsx`. Se leyeron las nueve hojas, incluidas las notas de diseño y las fórmulas de la matriz. El catálogo reproducible está en `catalog/ranks-missions.json`; los escudos del frontend se extrajeron del propio libro. No se modificó el Excel.

## Rangos y puntos

| Nivel | Rango | Umbral | Bono |
| --- | --- | ---: | ---: |
| 1 | Postulante | 0 | 0 |
| 2 | Compañero de Armas | 1 | 10 |
| 3 | Escudero | 101 | 20 |
| 4 | Sargento de Armas | 301 | 35 |
| 5 | Caballero del Temple | 651 | 60 |
| 6 | Comendador | 1201 | 90 |
| 7 | Preceptor | 2001 | 130 |
| 8 | Mariscal | 3201 | 180 |
| 9 | Senescal | 5001 | 250 |
| 10 | Gran Maestre | 7501 | 350 |

Se necesita el umbral **y** el hito. El ascenso es secuencial, exige ingreso validado y desde el nivel 4 una HOS validada en los últimos 90 días. El bono de cada ascenso se acredita una sola vez. El rango 10 exige un acta de elección del Capítulo; el Voto de Fidelidad no tiene campos, misión, evidencia propia ni barra de progreso.

Por ejemplo, 48 puntos superan el umbral de Compañero de Armas (1), pero un
Postulante sigue necesitando HIT-DOM (PRX-01 y PRX-02 con evidencia aprobada) y
HIT-ING (acta administrativa de ingreso). Activar la cuenta o aprobar un certificado
no equivale a validar el ingreso. El panel de progreso enumera los requisitos
pendientes y recibe las actualizaciones de `progress.get` al volver a la ventana;
también muestra el bloqueo por sanción informado por el backend.

Las 53 misiones conservan código, título, categoría, área, puntos, evidencia, rango mínimo, repetición y tope del Excel. Las misiones de una sola realización no pueden repetirse; FOR-04 acepta exactamente cuatro módulos distintos; VIG-02 una vez al mes. Las repeticiones rechazadas se conservan y la corrección crea otro reporte. Solo puede haber uno pendiente por miembro/misión.

La asignación no exige archivo y se conserva entre sesiones. El reporte admite bitácora, archivo o enlace HTTPS, según lo que verifica el administrador para esa misión. Audio y vídeo se presentan mediante enlaces; los archivos admitidos siguen siendo imágenes/PDF de hasta 5 MB. Los soldados no pueden consultar evidencias, tampoco propias, incluso si tienen rango alto.

Los puntos se registran en `point_ledger`, con claves que impiden pagos repetidos y bloqueo transaccional por miembro. Los topes se calculan por mes de realización en **America/Lima**. Incluyen los puntos de misión, calidad, equipo y primicia; constancia, hitos y ascensos se registran separadamente. La calidad excelente agrega 20%; equipo agrega 15 cuando el compañero es un apadrinado de rango inferior; primicia agrega hasta 25 a una CAR-01 vinculada a una ficha nueva aprobada, sin premiar la misma ficha dos veces. El tope puede reducir un bono. Constancia agrega 25 tras cuatro semanas consecutivas validadas, como máximo una vez cada 28 días. No caducan puntos.

El catálogo admite honor con factor 0,5 y decimales, excepto FOR-03/FOR-04 y CAR-01, que requieren evidencia estructural en la plataforma. VIG-02, VIG-03 y VIG-05 lo aplican automáticamente sin archivo ni enlace. Las demás misiones aceptan bitácoras verificables o una declaración explícita de honor. Un reporte pendiente no da puntos: la aceptación bajo palabra de honor se realiza por revisión, siguiendo la definición precisa de «Economia de puntos», fila 30.

## Hitos

HIT-DOM se comprueba con PRX-01 y PRX-02; HIT-PROX con PRX-03 y CAR-01; HIT-FUN con FOR-03 y los cuatro módulos CREDO, SACRAMENTOS, VIDA y ORACION. Se generan al aprobar las misiones y otorgan sus puntos una sola vez.

Los otros hitos necesitan PDF firmado, nota y validación administrativa. Ingreso exige 30 días, entrevista y referencia de otro miembro activo. Armadura comprueba tres meses consecutivos y exige declarar examen y liderazgo verificados. Encomienda exige tres EST-02 y encomienda activa. Cátedra exige PRE-03 y PRE-01. Campaña exige tres DEB-04 y verificar lugares distintos. Gran Debate exige DEB-06 y acta del Capítulo, sin imponer EST-03 como misión adicional. Elección exige decisión del Capítulo y obra fundada y sostenida verificada en el acta, sin imponer EST-04 como única prueba.

**El libro no incluye preguntas, respuestas ni criterios de calificación de los exámenes.** El administrador configura preguntas y los miembros responden dentro de la aplicación; se conserva una copia del cuestionario y sus respuestas en el reporte, y la aprobación requiere revisión humana. Sin preguntas configuradas no se admite el examen. El usuario confirmó que todavía no tiene ese contenido: no se inventó un banco de preguntas ni un motor de evaluación automática. Del mismo modo, invitaciones, consentimiento, meses de catequesis, duración de servicio, encomiendas, métricas y actas requieren verificación humana. El validador debe revisar la evidencia concreta del catálogo antes de marcar los requisitos como cumplidos.

## Salvaguardas

| Regla | Aplicación |
| --- | --- |
| R-01 | Declaración obligatoria de respeto; rechazo por desprecio resta 20 puntos. |
| R-02/R-07 | Campo requiere otro miembro activo, hora inicial y final entre 06:00 y 20:00, mismo día en Lima, entorno seguro y acceso autorizado. Rangos iniciales requieren acompañante de nivel 4+. |
| R-03 | Una grabación declarada exige consentimiento. Sin él se presenta bitácora. Los enlaces no se descargan ni inspeccionan automáticamente. |
| R-04 | Advertencia fija, declaración sobre personas vulnerables y marca de revisión especial si se mencionan menores. Todos los reportes pasan por administradores. |
| R-05 | Fecha de nacimiento obligatoria antes del reporte; el miembro no puede cambiarla luego. Menores necesitan consentimiento parental verificado por administrador y solo acceden a FOR/VIG/HOS. |
| R-06 | Evidencias y bitácoras privadas; declaración obligatoria de privacidad. Fichas públicas describen el grupo y su local público, sin campos para datos personales de interlocutores. Moderación revisa la anonimización. |
| R-08 | DEB-04 y EST-05 requieren una invitación propia cargada previamente en PDF. |
| R-09 | Evidencia falsa cancela el saldo neto de puntos del mes de realización; desde la segunda infracción baja un rango y bloquea recuperar automáticamente el rango sancionado. |
| R-10 | HOS verificada en los últimos 90 días para ascender al nivel 4 o superior. |
| R-11 | Revisión al iniciar y cada hora: alerta persistente a los 60 días y Reserva a los 120. Conserva rango/puntos; la reincorporación administrativa reinicia el plazo. Las alertas se muestran en el dashboard, sin enviar mensajes externos. |
| R-12 | Cualquier miembro activo puede reportar conducta de forma privada; el superadministrador registra la decisión del Capítulo. Confirmar humillación retira el rango y bloquea ascensos hasta levantar la sanción. |

## Decisiones para resolver ambigüedades

- PRX-02 exige nivel 2 pero desbloquea ese mismo nivel: se permite prepararla desde nivel 1. PRE-03 exige nivel 7 pero desbloquea el 7: se permite desde nivel 6. Los ascensos siguen exigiendo ambas llaves. El resto de la matriz conserva los mínimos del catálogo.
- Se adoptó la propuesta de las notas: administradores validan durante la etapa inicial, hasta existir tres Comendadores o superiores activos. Después necesitan rango superior; Sargento valida niveles 1–2 y Comendador hasta 4. Se conserva la petición previa del usuario: los soldados nunca adquieren acceso a evidencias.
- Para constituir por primera vez los niveles superiores, el superadministrador puede declarar una validación fundacional del Capítulo, con nota/acta. El servidor solo admite esta excepción si no existe ningún administrador con rango suficiente. Evita otro bloqueo circular al formar los primeros validadores. La elección de Gran Maestre es decisión del Capítulo certificada por un superadministrador distinto del candidato.
- No se supuso un cargo único mundial, ni ramas paralelas: el Excel deja esas decisiones abiertas. No hay caducidad de puntos. La misión destacada rota mensualmente entre las publicadas y elegibles; el bono se obtiene por constancia real.

## Despliegue y datos anteriores

Backend en la carpeta exterior `backendcai/`; frontend en la carpeta interior `cai/`. Se conserva **POST /api** y **GET /healthz**. No hacen falta nuevas variables de entorno.

Con `Database__AutoMigrate=true` se aplican los scripts nuevos al desplegar. El arranque guarda `schema_migrations` y serializa migraciones para evitar ejecuciones simultáneas. Si administras SQL manualmente, en una instalación ya existente aplica `003_rank_system.sql` y después `004_catalog.sql`; en una base nueva aplica 001, 002, 003 y 004, en ese orden.

**Los rangos antiguos no acreditaban los hitos nuevos.** Se conserva el rango anterior en `users.legacy_rank_code`, sus misiones, evidencias, asignaciones y puntos históricos. El nuevo camino empieza como Postulante y asciende al validar sus hitos. La migración no inventa exámenes ni actas a partir de pesos de badges. Repetir los scripts nuevos no reinicia rangos ya obtenidos ni duplica puntos.

Las misiones operacionales antiguas se marcan como de campo y requieren como mínimo Compañero de Armas; no pueden eludir acompañamiento/horario por haber sido creadas antes del Excel. Las propias de campo tampoco admiten Postulante como rango mínimo.

El panel de administración del dashboard permite gestionar nacimiento, consentimiento parental, padrino, reserva, actas y conducta. Misiones muestra puntos, requisitos, estados y revisión real; Sectas permite moderar fichas y agregar coordenadas. El mapa usa únicamente fichas aprobadas con coordenadas y teselas gratuitas de OpenStreetMap.

La revisión detallada y las funciones incorporadas están en [AUDITORIA_XLSX.md](AUDITORIA_XLSX.md). Para despliegues actuales aplicar también las migraciones 005 a 009, en orden.
