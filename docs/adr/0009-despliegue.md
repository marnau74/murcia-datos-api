# 0009 · Despliegue

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

Es un servicio sin estado, de solo lectura y sin secretos. Tiene que poder correr gratis (demo) y en un
VPS normal, con la misma imagen.

## Decisión

- **Imagen de Docker** en dos fases (el SDK compila; llega a la final solo el entorno de ASP.NET Core),
  restauración de paquetes en una capa aparte para aprovechar la caché, usuario `app` (no administrador)
  y solo la librería nativa de DuckDB de la arquitectura de destino (la imagen baja de ~830 MB a ~490 MB).
  Se publica en **GHCR**, multiarquitectura (amd64 y arm64), con cada cambio en `main` y cada etiqueta `v*`.
- **Dos formas de desplegarla**, la misma imagen:
  - **Render** (plan gratuito, `render.yaml`): sin base de datos ni variables secretas. El servicio duerme
    tras un rato sin tráfico y, al despertar, carga la última versión (se guarda en disco, pero el disco
    del plan gratuito es efímero: tarda unos segundos en descargarla).
  - **VPS con Docker Compose + Caddy** (`deploy/`): HTTPS automático (Let's Encrypt); sin dominio propio
    sirve `<ip-con-guiones>.sslip.io`. Solo Caddy publica puertos. El contenedor de la API va con
    sistema de ficheros de solo lectura, sin capacidades de Linux, `no-new-privileges` y un volumen para
    los datos descargados.
- **Salud**: `/health/live` (el proceso responde) y `/health/ready` (hay datos cargados; «degradada» si
  la última actualización falló pero se sigue sirviendo la anterior: no se saca de rotación).
  Caddy las oculta hacia fuera.
- **Observabilidad**: OpenTelemetry (trazas y métricas de ASP.NET Core, HTTP y runtime, más métricas
  propias: consultas por recurso y resultado, y duración en DuckDB), exportadas solo si hay colector
  (`OTEL_EXPORTER_OTLP_ENDPOINT`).
- **CI**: formato, compilación con avisos como errores, tests por proyecto, vulnerabilidades en
  dependencias, CodeQL, y un trabajo que construye la imagen y la arranca. Un trabajo mensual
  (`datos-reales`) la prueba contra la última release publicada de verdad.

## Qué no está probado

- El despliegue en Render y en un servidor real con certificados, y que la imagen publicada en GHCR
  se pueda descargar (el trabajo `publicar` termina bien, pero nadie la ha consumido).

## Qué se ha probado de verdad

- El flujo de GitHub Actions (formato, tests, CodeQL, construcción y arranque de la imagen) se ejecuta
  en verde en cada cambio.
- La descarga desde GitHub real: el 1 de octubre de 2026 el trabajo `datos-reales` arrancó la imagen y
  descargó la release `datos-2026-09` de `murcia-open-data`, con sus sumas y su contrato, por red. Los
  controles de calidad pasaron, no hubo problema de actualización y cada recurso respondió una consulta.
  Antes solo estaba cubierta con un servidor HTTP falso (selección de la release, rechazo de direcciones
  ajenas, sumas, contrato) y con una carpeta local.
