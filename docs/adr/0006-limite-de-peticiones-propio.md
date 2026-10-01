# 0006 · Límite de peticiones por IP propio, no el de ASP.NET Core

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

La API es pública y sin claves. Hay que limitar el uso abusivo y dar a los clientes legítimos la
información para autorregularse.

## Decisión

Un *middleware* propio con **ventana fija por IP** (120 peticiones por 60 s por defecto, configurable),
con `TimeProvider` inyectado para poder probarlo con un reloj falso.

- Responde en **todas** las respuestas con `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`
  y `RateLimit-Policy`, y con **429**, `Retry-After` y `problem+json` al pasarse.
- Se limita `/v1` y `/openapi`; no el explorador (ficheros estáticos) ni las sondas de salud.
- Cuentan también las respuestas servidas desde la caché y los 304.
- Las IPv6 se agrupan por `/64` (cambiar de dirección dentro de la misma red no sirve para saltarse el
  límite) y las IPv4 mapeadas cuentan como IPv4.
- **Detrás de un proxy** la IP real viene de `X-Forwarded-For` solo si se configura
  (`Proxy__ConfiarEnCabecerasReenviadas`). Sin configurar, la cabecera se ignora: hay un test de que
  no sirve para saltarse el límite.
- Los datos de IP viven en memoria y se purgan al caducar su ventana.

## Alternativas descartadas

- *`AddRateLimiter` de ASP.NET Core:* no permite leer cómodamente lo que queda de ventana para
  poner las cabeceras en las respuestas correctas, que es lo que más sirve al cliente.

## Consecuencias

- El límite es por instancia: con varias réplicas, cada una cuenta aparte. Para un servicio de una
  sola instancia (el caso previsto) es exacto.
