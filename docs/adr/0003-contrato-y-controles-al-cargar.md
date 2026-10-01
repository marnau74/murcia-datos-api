# 0003 · El contrato con el proyecto de datos y los controles al cargar

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

Dos proyectos distintos, en dos lenguajes, se pasan datos por un fichero. Si el productor cambia una
columna, el consumidor no debe arrancar con datos rotos ni dar respuestas silenciosamente erróneas.

## Decisión

Cada release trae `contrato.json` (versión del contrato, periodo, fuentes y, por tabla, filas y
columnas con su tipo) y `SHA256SUMS`. Antes de aceptar una versión, la API ejecuta estos controles, y
cualquier fallo la rechaza entera:

1. **Sumas SHA-256** del fichero de datos y de `contrato.json` frente a `SHA256SUMS`.
2. **Contrato compatible**: misma versión *mayor* (`1.x.y`), y presentes todas las tablas y columnas
   que la API necesita con el tipo esperado. Las tablas o columnas de más no rompen nada.
3. **Esquema real del fichero**: lo que dice `information_schema` coincide con lo esperado (no basta
   con fiarse del JSON del contrato).
4. **Filas**: cada tabla tiene las filas que dice el contrato.
5. **Claves únicas** en las tres tablas de hechos.
6. **Integridad referencial**: ningún hecho apunta a una fecha, territorio, tipo o residencia que no exista.

El resultado de los seis controles se publica en `/v1/metadatos` (`calidad`).

La descarga solo se hace del repositorio configurado: las direcciones se construyen
(`github.com/<repo>/releases/download/<etiqueta>/<fichero>`) con la etiqueta validada (`datos-AAAA-MM`)
y una lista cerrada de nombres de fichero, nunca con las direcciones que devuelva la API de GitHub.
Hay un tamaño máximo de descarga (256 MB por defecto).

## Consecuencias

- Un cambio incompatible en el proyecto de datos obliga a subir la versión mayor del contrato, y esta
  API se queda con la versión anterior hasta que se actualice.
- `SHA256SUMS` viaja en la misma release que los datos: protege contra descargas corruptas, no contra
  quien pueda modificar la release (para eso haría falta firmarla). Es una limitación conocida.
- La CI tiene un trabajo mensual (`datos-reales`) que arranca la imagen contra la última release
  publicada de verdad: si el productor cambia algo, falla aquí antes de que lo note nadie.
