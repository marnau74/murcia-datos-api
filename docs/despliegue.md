# Desplegar la API

Es un servicio sin estado, de solo lectura y **sin secretos ni base de datos**. Hay dos caminos con la misma
imagen; las decisiones están en el [ADR 0009](adr/0009-despliegue.md).

## A. Render (gratis, para la demo)

1. En Render: **New → Blueprint** y elige el repositorio. Lee `render.yaml`; no pide nada.
2. Cuando termine, `https://<tu-servicio>.onrender.com/health/ready` debe responder `lista` y `/` abre el explorador.

Qué esperar del plan gratuito: el servicio **duerme** tras un rato sin tráfico (unos 15 minutos al escribir esto;
revisa las condiciones al desplegar) y la primera petición tarda en despertarlo. Al despertar descarga la última
versión de los datos desde GitHub (unos segundos): mientras tanto `/v1/*` responde 503 con `Retry-After`.

## B. VPS con Docker Compose y Caddy

1. Un servidor con Docker y los puertos 80 y 443 abiertos.
2. Copia la carpeta `deploy/` al servidor y prepara la configuración:

   ```bash
   cp .env.example .env
   ```

   Edita `.env`: `PROPIETARIO` (tu usuario de GitHub, de donde sale la imagen `ghcr.io/<propietario>/murcia-datos-api`)
   y `DOMINIO`. **Sin dominio propio**, usa la IP del servidor con guiones y `.sslip.io`: para `203.0.113.7`,
   `203-0-113-7.sslip.io` (apunta solo a esa IP y Caddy consigue el certificado de Let's Encrypt).
3. Arranca:

   ```bash
   docker compose pull && docker compose up -d
   docker compose ps          # api debe aparecer «healthy»
   ```

4. Comprueba: `https://<DOMINIO>/` (explorador), `https://<DOMINIO>/scalar` (documentación) y
   `curl https://<DOMINIO>/v1/metadatos`.

Si la imagen todavía no está publicada en GHCR, puedes construirla en el servidor desde el código:
`docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build`.

Actualizar: `docker compose pull && docker compose up -d`. Los datos se actualizan solos (cada 6 horas se
comprueba si hay una release nueva), sin tocar el servidor.

### Cosas que conviene saber

- **Detrás de Caddy hay que decirle a la API que confíe en `X-Forwarded-For`** (`Proxy__ConfiarEnCabecerasReenviadas`,
  ya puesto en el compose): si no, todas las peticiones parecerían de Caddy y compartirían un único límite.
- **Caddy no debe poner su propia `Content-Security-Policy`**: la API envía la suya con el hash del `importmap` de
  Blazor y, si Caddy la sustituyera, el explorador no arrancaría.
- Las sondas `/health/*` están ocultas hacia fuera; el contenedor las usa por dentro (la imagen no trae `curl`:
  el *healthcheck* hace la petición con `/dev/tcp` de bash).
- El volumen `datos` guarda la última versión descargada: tras un reinicio con GitHub caído, la API sirve datos igualmente.

## Variables de configuración

| Variable | Por defecto | Para qué |
|---|---|---|
| `Datos__Repositorio` | `marnau74/murcia-open-data` | De qué repositorio se descargan las releases `datos-AAAA-MM` |
| `Datos__IntervaloHoras` | `6` | Cada cuánto se comprueba si hay datos nuevos |
| `Datos__MaxFilas` | `20000` | Filas máximas por respuesta |
| `Datos__SegundosMaxConsulta` | `5` | Tiempo máximo de una consulta |
| `Datos__MaxDescargaMb` | `256` | Tamaño máximo de un fichero descargado |
| `Datos__Directorio` | `/datos` (imagen) | Dónde se guardan las versiones descargadas |
| `Datos__Origen` / `Datos__Ruta` | `GitHub` | `Directorio` + ruta: usar una carpeta local con una release (desarrollo, pruebas) |
| `Limites__PeticionesPorVentana` / `Limites__VentanaSegundos` | `120` / `60` | Límite de peticiones por IP |
| `Proxy__ConfiarEnCabecerasReenviadas` | `false` | `true` detrás de un proxy |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | — | Si se define, se exportan trazas y métricas |

## Desarrollo en local

Con el repositorio `murcia-open-data` al lado (carpeta `release/` generada por su pipeline):

```bash
dotnet run --project src/MurciaDatos.Api --launch-profile local     # http://localhost:5280
```

El perfil `github` descarga la release real en lugar de leer la carpeta.
