# 0005 · Caché de salida, ETag y versión de los datos

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

Los datos cambian una vez al mes y las consultas se repiten mucho. Lo razonable es no tocar DuckDB si
la respuesta ya se calculó, y dejar que clientes y proxies reutilicen lo que ya tienen.

## Decisión

- **Caché de salida** de ASP.NET Core con una política propia: una hora, variando por la **consulta
  normalizada** (parámetros en minúsculas, en cualquier orden, listas de territorio/tipo/residencia
  ordenadas y sin repetidos, más el formato resuelto: JSON o CSV) y por la **versión de los datos**.
  Etiquetada, y vaciada de golpe cuando llega una versión nueva.
- **ETag débil** = hash de (versión de datos + consulta normalizada). Con `If-None-Match` igual, la
  respuesta es 304 sin ejecutar nada; si la caché ya tenía la respuesta, también sale de ella.
  `Cache-Control: public, max-age=3600`.
- **Solo se cachean y llevan cabeceras de caché las respuestas 200.** Un 400 o un 503 no llevan `ETag`
  ni `Cache-Control` público.
- Un ETag válido **no permite saltarse la validación**: primero se valida la consulta y después se
  compara el ETag (hay un test).
- Compresión Brotli/Gzip por delante de la caché.

## Lecciones (cada una cazada por un test)

- Las cabeceras de caché había que ponerlas justo antes de devolver la respuesta y no en un
  `OnStarting`: la caché de salida captura las cabeceras en su propio `OnStarting`, que se ejecuta
  antes que el de un *endpoint* registrado después, y guardaba la respuesta sin `ETag`.
- Al contrario, las cabeceras `RateLimit-*` **sí** van en un `OnStarting` registrado antes de la caché:
  si no, una respuesta servida desde la caché repetía el contador de la petición que la generó.
- El control de la versión en la clave hace que, aunque el vaciado por etiqueta fallase, una versión
  nueva nunca sirva respuestas de la vieja.

## Alternativas descartadas

- *Caché propia en un `Dictionary`:* hay que resolver expiración, tamaño y concurrencia.
- *Permitir saltarse la caché con `Cache-Control: no-cache` del cliente:* sería una puerta para
  hacer trabajar a DuckDB a voluntad.
