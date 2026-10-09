# Revisión del XLSX contra la aplicación

Fuente: `../../CAI_Sistema_de_Rangos_y_Misiones.xlsx`, sus nueve hojas.
`python scripts/verify-workbook.py` compara 679 valores del libro con el catálogo
y comprueba los diez escudos. No modifica el XLSX ni el catálogo original.

| Requisito | Implementación y corrección |
| --- | --- |
| 10 rangos, 53 misiones, 10 hitos, 12 reglas | Catálogo contrastado con el libro; umbral y hito se exigen juntos y los bonos son únicos. |
| Exámenes FOR-03 y FOR-04 dentro de la app | Preguntas configurables por administrador, respuestas abiertas en Misiones y copia de preguntas/respuestas preservada en cada reporte. Su aprobación es humana y acredita la misión; no hay puntos por enviar respuestas. |
| VIG-01: siete días de oración | Registro diario privado; el servidor exige siete fechas consecutivas antes de reportar. No acredita la misma racha final dos veces. |
| VIG-06: rosario semanal | Registro privado y comprobación de la semana; no acredita la misma semana dos veces. |
| Factor de honor 0,5 | Disponible en misiones del catálogo salvo exámenes y CAR-01, que tienen evidencia estructural explícita. Invitaciones y reglas de campo siguen siendo obligatorias. No sustituye la evidencia de los hitos automáticos. |
| Constancia | Incluye todas las misiones aceptadas por un validador, también las aceptadas bajo honor; controla cuatro semanas consecutivas y pagos solapados. |
| Hospitalidad para ascender | Una HOS aceptada en los últimos 90 días permite cumplir R-10; el estado mostrado y la comprobación del ascenso usan el mismo criterio. |
| HIT-SEN | Exige DEB-06 y el acta; se eliminó la obligación adicional de completar EST-03, que el hito exacto del XLSX no exige. |
| HIT-GM | Elección y obra fundada/sostenida verificadas en el acta; no impone EST-04 como única prueba de la obra. No se solicita ni se registra el Voto. |
| Inactividad | Elimina alertas antiguas antes de pasar a reserva. Una edición de perfil no reinicia el plazo; reincorporar desde reserva sí lo reinicia. |
| Buscador de apologetas | Filtros en servidor por nombre, rango, área validada, ciudad y actividad; paginación real y perfil desplegable. Se retiraron especialidades ficticias. |
| Dashboard | Misiones en curso, misión mensual, enlaces configurables, padrino, historial, puntos por área y movimientos de bonos/sanciones. |
| Certificados | Tipos curso/hito/armadura y carga de PDF; solo los de curso habilitan activación. Registrar un certificado no valida automáticamente un hito. |
| Mapa y Sectas | Fichas aprobadas, doctrina y ubicación; orden por cercanía voluntario. Fichar exige rango 2 o superior según el mínimo de CAR-01, mayoría de edad y no estar en reserva. |
| CAR-01 | Una misma ficha no vuelve a acreditar puntos de registro. La primicia sigue siendo única y respeta el tope. |

## Contenido que todavía no existe

El usuario confirmó que no dispone del banco de preguntas ni de enlaces al curso
y grupo de espera. La configuración inicial queda vacía y la interfaz lo indica.
La API bloquea enviar un examen sin preguntas configuradas; no se inventaron
exámenes, respuestas correctas, aprobaciones ni enlaces. El administrador puede
cargar preguntas después; FOR-03 requiere al menos veinte. Las respuestas abiertas
se califican mediante revisión porque el libro no define puntuación de exámenes.

Las duraciones de servicio, métricas, invitaciones, liderazgo, encomiendas y actas
se comprueban con evidencia y confirmación del administrador, porque el libro no
contiene las fuentes externas que permitirían verificarlas automáticamente.

## Decisiones del libro y de la sesión

Se conservan las excepciones documentadas para PRX-02 y PRE-03, que evitan los
requisitos circulares del libro. La autoridad administrativa de revisión conserva
la regla previa de la sesión: los soldados no consultan evidencias. El mínimo de
CAR-01 del catálogo (nivel 2) prevalece sobre la descripción general del rango 3.
La unicidad territorial del Gran Maestre y las ramas paralelas siguen siendo
decisiones abiertas en la hoja «Cambios y notas»; no se impusieron reglas nuevas.

## Despliegue y verificación

Aplicar las migraciones nuevas 007, 008 y 009 en orden o desplegar con
`Database__AutoMigrate=true`. La 009 incorpora configuración de formación,
registros espirituales, ciudad y tipos/archivos de certificados.
Desplegar también el frontend actualizado. Las pruebas se realizan únicamente
en una base local aislada; no se ejecutan fixtures contra Render.

Validación realizada: backend .NET 10 y frontend Vite compilados, 679 comprobaciones
contra el XLSX, 454 comprobaciones de rangos y funciones nuevas, 179 de integración
general y seis del cargador del directorio. Las migraciones 001–009 se comprobaron
en PostgreSQL 16, incluidas las reparaciones de campos faltantes y su repetición.
