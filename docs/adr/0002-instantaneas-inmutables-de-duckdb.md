# 0002 · Datos como instantáneas inmutables de DuckDB, con cambio atómico

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

Los datos cambian una vez al mes (y, a veces, se republica la release del mes con otro contenido). La
API tiene que actualizarse sola, sin cortes y sin servir nunca datos a medias ni rotos.

## Decisión

- Una **instantánea** es un fichero `.duckdb` abierto en modo solo lectura (`access_mode=READ_ONLY`),
  con su contrato y su catálogo (dimensiones y qué hay en cada tabla) cargados en memoria. No cambia
  nunca. La API no podría escribir en ella aunque quisiera.
- El **actualizador** consulta la última release `datos-AAAA-MM` al arrancar y cada 6 horas. Identifica
  una versión por etiqueta **y** por la huella (SHA-256) del fichero: el pipeline mensual repite la
  release del mes con `--clobber`, así que la misma etiqueta puede traer datos distintos.
- La versión nueva se descarga a una carpeta temporal, se verifica ([0003](0003-contrato-y-controles-al-cargar.md))
  **antes** de moverla a su sitio definitivo, y solo entonces se cambia la referencia activa de forma
  atómica (`Interlocked.Exchange`).
- Cada consulta **toma prestada** la instantánea (contador de préstamos) y la devuelve al terminar. La
  anterior se retira: no admite préstamos nuevos y se cierra cuando se devuelve el último. Así las
  consultas en curso terminan con la versión con la que empezaron, sin errores ni mezclas (hay un test
  con ocho lectores concurrentes y un cambio de versión en medio).
- Si algo falla (red, suma, contrato) no se toca lo que se sirve; el motivo queda en `/v1/metadatos` y
  la sonda `/health/ready` pasa a «degradada» (sigue en servicio).
- Se guardan en disco la versión nueva y la anterior. Al arrancar se carga la más reciente que supere
  de nuevo la comprobación de sumas, de modo que un reinicio con GitHub caído sirve datos igualmente.
- Una release **más antigua** que la activa se ignora (no se retrocede).

## Alternativas descartadas

- *Abrir siempre el fichero más reciente en cada petición:* coste y condiciones de carrera.
- *Reemplazar el fichero en sitio:* una consulta en curso leería un fichero a medio escribir.
- *Reiniciar el proceso para cambiar de datos:* corta el servicio.

## Consecuencias

- Hay dos ficheros abiertos durante unos instantes en cada cambio de versión.
- En Windows el borrado de la versión retirada puede aplazarse a la siguiente actualización (el
  fichero está en uso); en Linux no hay ese problema.
