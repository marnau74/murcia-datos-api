# Decisiones de arquitectura

Cada decisión relevante queda registrada en un ADR corto: contexto, decisión, alternativas
descartadas y consecuencias. Un ADR no se edita cuando cambia la decisión: se escribe uno
nuevo que lo sustituye.

| Nº | Decisión | Estado |
|---|---|---|
| [0001](0001-una-sola-aplicacion-y-alcance.md) | Una sola aplicación (API + explorador), sin Aspire ni base de datos | Aceptada |
| [0002](0002-instantaneas-inmutables-de-duckdb.md) | Datos como instantáneas inmutables de DuckDB, con cambio atómico | Aceptada |
| [0003](0003-contrato-y-controles-al-cargar.md) | El contrato con el proyecto de datos y los controles al cargar | Aceptada |
| [0004](0004-semantica-de-los-datos.md) | Semántica de los datos: nulos, agregación y totales | Aceptada |
| [0005](0005-cache-etag-y-versiones.md) | Caché de salida, ETag y versión de los datos | Aceptada |
| [0006](0006-limite-de-peticiones-propio.md) | Límite de peticiones por IP propio, no el de ASP.NET Core | Aceptada |
| [0007](0007-rendimiento-y-medidas.md) | Rendimiento: lo que se midió y lo que se cambió | Aceptada |
| [0008](0008-explorador-blazor-y-politica-de-contenido.md) | Explorador en Blazor WebAssembly, gráficas SVG y política de contenido | Aceptada |
| [0009](0009-despliegue.md) | Despliegue | Aceptada |
