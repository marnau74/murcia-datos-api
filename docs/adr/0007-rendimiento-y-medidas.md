# 0007 · Rendimiento: lo que se midió y lo que se cambió

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

El objetivo era p95 < 50 ms con la caché caliente. Antes de dar nada por bueno se midió (no se supuso):
con **BenchmarkDotNet** el coste de las consultas contra DuckDB y con **k6** la API entera.

Equipo de medida: Intel Core Ultra 5 125H (18 hilos lógicos), Windows 11, .NET 10, release real
`datos-2026-08` (5.598 filas de demanda, 1.492 de oferta, 277 de precios).

## Hallazgo: demasiados hilos para tan pocos datos

La primera medición dio **30 ms** para una serie mensual de un territorio (5.598 filas en la tabla): una
barbaridad. Probando el mismo SQL desde Python, **188 ms** con la configuración por defecto y **4,7 ms**
con `threads=1`. DuckDB reparte el trabajo entre todos los hilos de la máquina y, con tan pocos datos,
repartir cuesta mucho más que hacerlo.

**Decisión: limitar DuckDB a 2 hilos por conexión** (`threads=2` en la cadena de conexión). Medido
(BenchmarkDotNet, trabajo corto):

| Consulta (sin caché de la API) | Antes (todos los hilos) | Con `threads=2` |
|---|--:|--:|
| Serie mensual de un territorio | 30,7 ms | 6,3 ms |
| Todos los territorios por año | 21,8 ms | 5,3 ms |
| Demanda completa mensual (sin filtros) | 23,5 ms | 12,9 ms |
| Total de residencias por trimestre | 60,8 ms | 8,5 ms |
| Indicador de estacionalidad | 26,4 ms | 5,3 ms |

Se probó también un grupo de conexiones abiertas (abrir una conexión no era el coste: no cambió nada) y
se dejó porque evita abrir y cerrar en cada petición, que es gratis de mantener.

## Prueba de carga (k6, `perf/k6.js`)

Dos escenarios por separado, con la API en Release y k6 en Docker en la misma máquina (así que los
números incluyen la competencia por CPU y el paso por la red de Docker Desktop):

| Escenario | Carga | Media | p95 |
|---|---|--:|--:|
| Caché caliente (8 consultas repetidas) | 50 usuarios, 20 s | 5,4 ms | **9,1 ms** |
| Sin caché (≈ 10⁶ claves distintas: casi cada petición ejecuta DuckDB) | 10 usuarios, 20 s | 11,3 ms | **15,8 ms** |

En total, unas 3.900 peticiones por segundo, 0 % de errores. Los umbrales del script (p95 < 50 ms en
caliente, < 250 ms en frío, errores < 1 %) se cumplen con holgura. (Una primera versión del escenario
«frío» tenía solo ≈ 2.000 claves y en segundos acababa en la caché: media de 2 ms, una medida falsa. Se
corrigió ampliando el espacio de claves.)

## Consecuencias

- Estas cifras son de un portátil, no de un VPS pequeño: sirven para comparar versiones y para saber
  dónde está el coste, no como promesa. Los scripts están en el repositorio para repetirlas.
- La CPU, no la memoria, es el límite: la imagen necesita poco (unos 100 MB de aplicación más el
  entorno de ejecución).
