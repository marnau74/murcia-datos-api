# Cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y versionado semántico.

## [1.0.0] — 2026-10-01

Primera versión completa.

### API
- Recursos de solo lectura bajo `/v1`: `metadatos`, `territorios`, `tipos-alojamiento`, `residencias`, `medidas`,
  `demanda`, `oferta`, `precios` e indicadores (`estacionalidad`, `variacion`).
- Filtros validados contra lo que hay en los datos (el error enumera los valores permitidos), agregación por
  mes, trimestre o año, JSON y CSV (con variante para Excel).
- Un dato que no existe es `null`; flujos se suman y existencias o tasas se promedian; `meses` por periodo;
  totales de residencia solo si están todas ([ADR 0004](docs/adr/0004-semantica-de-los-datos.md)).
- Errores `application/problem+json`, OpenAPI y documentación con Scalar; el contrato OpenAPI está guardado en el
  repositorio y un test falla si cambia sin querer.

### Datos
- Actualización automática desde las releases `datos-AAAA-MM` de `murcia-open-data`: sumas SHA-256, contrato,
  esquema, filas, claves e integridad antes de aceptar una versión, cambio atómico sin cortes y arranque desde
  disco si GitHub no responde.

### Rendimiento y seguridad
- Caché de salida con ETag/304, compresión, límite de peticiones por IP con cabeceras `RateLimit-*`, CORS de solo
  lectura, política de contenido estricta y tiempo y filas máximos por consulta.
- Medido con BenchmarkDotNet y k6; DuckDB limitado a 2 hilos (de 30 ms a 6 ms por consulta).

### Explorador
- Blazor WebAssembly servido por la propia API: gráfica SVG, tabla, URL compartible, llamada equivalente
  (`curl`, Python) y descarga en CSV.

### Despliegue
- Imagen de Docker multiarquitectura, `docker compose` con Caddy, plano de Render y CI (formato, tests,
  vulnerabilidades, CodeQL, prueba de la imagen y prueba mensual contra la release real).
