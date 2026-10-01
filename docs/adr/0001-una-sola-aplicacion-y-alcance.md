# 0001 · Una sola aplicación (API + explorador), sin Aspire ni base de datos

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

Este proyecto es la capa de servicio de la plataforma de datos de turismo de Murcia
(`murcia-open-data`): ese proyecto produce, cada mes, una *release* con los datos ya modelados y este
sirve esos datos por HTTP y los enseña en un explorador web. Hay que decidir cuántas piezas lleva
detrás y qué se queda fuera.

## Decisión

- **Un solo servicio**: la API y el explorador se publican juntos en una imagen y un puerto. El
  explorador (Blazor WebAssembly) son ficheros estáticos que sirve la propia API, y llama a la API
  del mismo origen: sin CORS, sin segundo despliegue y sin que la página y la API se desincronicen.
  (La guía inicial recomendaba GitHub Pages aparte; se descarta porque obligaría a versionarlo por
  separado y a mantener dos URL.)
- **Sin base de datos de servidor.** Los datos son pequeños (unas 8.000 filas en las tres tablas de
  hechos) y se actualizan una vez al mes: DuckDB en proceso, en modo solo lectura, sobre el fichero de
  la release. No hay nada que migrar, respaldar ni proteger con credenciales.
- **Sin Aspire.** En los otros dos proyectos .NET Aspire ordena varios procesos y una base de datos.
  Aquí hay un solo proceso sin dependencias: el *AppHost* no aportaría nada. La telemetría
  (OpenTelemetry) se configura directamente en la API y solo se exporta si hay colector.
- **Sin cuentas ni claves de API.** Es una API pública de solo lectura y se protege con un límite de
  peticiones por IP ([0006](0006-limite-de-peticiones-propio.md)).
- **Solo `GET`**, prefijo `/v1`, errores en `application/problem+json`.

## Alternativas descartadas

- *Explorador en GitHub Pages:* ver arriba.
- *GraphQL u OData:* el consumidor típico (pandas, Excel, un `curl`) quiere URL y CSV; los filtros
  son pocos y cerrados.
- *Cargar los datos en memoria sin DuckDB:* funcionaría con este volumen, pero la gracia del
  proyecto es servir el mismo fichero que producen Python y dbt sin traducirlo, y DuckDB hace las
  agregaciones en SQL con el mismo motor con el que se calcularon.

## Consecuencias

- El despliegue es una imagen y un volumen (para guardar la última versión descargada).
- El servicio escala a cero sin problema: no tiene estado que perder.
- El explorador se prueba con `bUnit` y, además, la CI construye la imagen y comprueba que la página
  sale bien procesada de la publicación.
