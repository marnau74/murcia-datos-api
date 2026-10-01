# 0008 · Explorador en Blazor WebAssembly, gráficas SVG y política de contenido

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

Quien no programa también debería poder ver y descargar los datos. Y el explorador sirve de
documentación viva de la API: lo que ves es lo que pide.

## Decisión

- **Blazor WebAssembly** (C# de punta a punta, y lógica compartida en forma de clases simples que se
  prueban sin navegador). Llama a la API del mismo origen.
- **Gráficas de líneas en SVG puro**, hechas en Razor, sin librerías de JavaScript. La guía inicial
  proponía Observable Plot por *interop*; se descarta para no cargar con dos librerías (d3 y Plot) en
  una política de contenido estricta, y porque lo que hace falta es poco: ejes con números redondos,
  líneas con huecos y una leyenda. Accesibilidad: `role="img"` con título y descripción, y los mismos
  datos en la tabla de debajo; los valores salen al pasar el ratón (`<title>`).
- **Un mes sin dato corta la línea** (no se unen los puntos vecinos, que sugeriría un cero o un dato
  inventado), y los periodos agregados con menos meses de los que les tocan se pintan con el círculo
  vacío ([0004](0004-semantica-de-los-datos.md)).
- **La URL guarda el estado completo** (recurso, territorios, tipos, residencia, medida, agregación,
  periodo): compartir el enlace es compartir la consulta, y atrás/adelante funcionan. «Desde el primer
  mes» se guarda como `desde=` vacío: omitirlo haría que, al releer la URL, saliera el valor por
  defecto (fallo cazado por un test).
- **«La misma consulta, en la API»**: `curl`, `curl` a CSV y Python (pandas) listos para copiar, y
  botones de descarga (CSV y CSV para Excel).
- Diseño sobrio, mismo lenguaje que el informe del proyecto de datos: papel cálido, un acento azul,
  filetes; modo claro y oscuro con `prefers-color-scheme`; tipos del sistema (sin fuentes externas).

## Política de contenido (CSP) estricta

`default-src 'self'; script-src 'self' 'wasm-unsafe-eval' …; style-src 'self'` — **sin** `unsafe-inline`.

- No hay estilos en línea (las gráficas usan atributos y clases).
- Blazor escribe en la página un `importmap` en línea con los nombres con huella de sus ficheros, que
  cambia en cada compilación. La API **lee la propia página al arrancar, calcula el SHA-256 del
  importmap** (con los saltos de línea normalizados a LF, que es como lo calcula el navegador: con CRLF
  el hash no coincidía) y lo añade a `script-src`. Hay un test que lo comprueba y otro en la CI contra
  la imagen real.
- El proxy **no debe** poner su propia CSP: la sobrescribiría y el explorador no arrancaría (el
  `Caddyfile` incluido lo tiene anotado).
- La documentación de la API (Scalar) usa scripts y estilos en línea y queda fuera de esta política.

## Consecuencias

- La primera carga descarga el entorno de ejecución de WebAssembly (unos pocos MB, comprimidos con
  Brotli). Se publica sin AOT; instalando la carga de trabajo `wasm-tools` pesaría menos y arrancaría
  más rápido (no se ha hecho para que construir el proyecto no la exija).
- El explorador se publica por separado de la API (`dotnet publish` del explorador) y se copia a su
  `wwwroot`: publicando solo la API la página salía sin procesar, con los marcadores de huella sin
  sustituir. Lo hace el `Dockerfile` y lo vigila la CI.
