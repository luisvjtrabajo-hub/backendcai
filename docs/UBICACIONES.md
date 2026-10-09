# País, ciudad y mapa de apologetas

Aplicar `database/010_member_locations.sql` después de la 009, o desplegar con
`Database__AutoMigrate=true`. Es repetible y conserva perfiles, puntos y ubicaciones.
Las cuentas anteriores conservan su ciudad escrita; deben seleccionar la ciudad
desde su perfil para obtener coordenadas y aparecer en el mapa.

Registro: elegir país → escribir ciudad → Buscar ciudad → seleccionar resultado.
`auth.register` exige `locationId`; obtiene país, ciudad y coordenadas del catálogo
guardado por `locations.search`, sin confiar en coordenadas enviadas por el cliente.
El punto aparece cuando la cuenta pasa a SOLDADO_ACTIVE. Miembros de la misma ciudad
comparten un punto con el total de apologetas. Los filtros del directorio se aplican
en servidor; el dashboard carga todas las páginas y filtra sus ciudades.
El dashboard de los apologetas muestra su progreso personal. El mapa global y las
métricas generales se reservan a SUPER_ADMIN y REGISTRADOR; `members.map` y
`overview.get` exigen permisos administrativos en el backend.

La búsqueda usa [Open-Meteo Geocoding](https://open-meteo.com/en/docs/geocoding-api)
con datos de GeoNames. No requiere clave para uso no comercial. Si el uso es comercial,
configurar `Locations__BaseUrl=https://customer-geocoding-api.open-meteo.com/v1/`
y `Locations__ApiKey` de la suscripción correspondiente; también se puede configurar
una instancia propia compatible. Tiempo máximo de búsqueda: 10 segundos; límite
global: 60 búsquedas/minuto por instancia. Una caída de la búsqueda se muestra en
el formulario; las ciudades ya seleccionadas y el mapa siguen usando PostgreSQL.

Prueba local: `node scripts/locations-smoke.mjs`, con API de pruebas en localhost,
`Locations__BaseUrl=http://127.0.0.1:58081/`, `TEST_PSQL`, `TEST_ADMIN_EMAIL` y
`TEST_ADMIN_PASSWORD`. El script inicia un proveedor simulado para comprobar país,
ciudad, coordenadas, cambio de ubicación, filtros, permisos y fallos del proveedor.
Los otros smoke tests requieren una ciudad existente (`TEST_LOCATION_ID` en smoke.mjs).
