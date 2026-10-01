// Prueba de carga de la API con k6 (https://k6.io).
//
//   k6 run -e BASE_URL=http://localhost:5280 perf/k6.js
//   docker run --rm -i -e BASE_URL=http://host.docker.internal:5280 grafana/k6 run - < perf/k6.js
//
// IMPORTANTE: la API limita las peticiones por IP (120 por minuto por defecto). Para medir el rendimiento y no el
// limitador, arráncala con  Limites__PeticionesPorVentana=1000000  (solo en la máquina de pruebas).
//
// Dos escenarios, por separado, porque miden cosas distintas:
//   · caliente: pocas consultas distintas repetidas: casi todo sale de la caché de salida (el caso normal de una API pública);
//   · frio: consultas distintas en cada petición, que obligan a ejecutar DuckDB (el peor caso, sin caché).
import http from "k6/http";
import { check } from "k6";

const base = __ENV.BASE_URL || "http://localhost:5280";

export const options = {
  scenarios: {
    caliente: {
      executor: "constant-vus",
      vus: 50,
      duration: "20s",
      exec: "caliente",
      startTime: "5s", // tras el calentamiento
    },
    frio: {
      executor: "constant-vus",
      vus: 10,
      duration: "20s",
      exec: "frio",
      startTime: "30s",
    },
  },
  thresholds: {
    "http_req_failed": ["rate<0.01"],
    "http_req_duration{scenario:caliente}": ["p(95)<50"],
    "http_req_duration{scenario:frio}": ["p(95)<250"],
  },
};

const consultasCalientes = [
  "/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=total&agregacion=anio&medidas=pernoctaciones",
  "/v1/demanda?territorio=region-murcia,costa-calida&tipo=hotel,apartamento&residencia=total&desde=2019-01&medidas=viajeros",
  "/v1/oferta?territorio=region-murcia&tipo=hotel&medidas=ocupacion_plazas&desde=2022-01",
  "/v1/precios?territorio=region-murcia&desde=2020-01",
  "/v1/indicadores/estacionalidad?territorio=region-murcia&tipo=hotel",
  "/v1/indicadores/variacion?territorio=region-murcia&tipo=hotel",
  "/v1/territorios",
  "/v1/metadatos",
];

const territorios = ["region-murcia", "costa-calida", "cartagena", "murcia-municipio"];
const tipos = ["hotel", "apartamento", "camping", "rural"];

export function setup() {
  // Calentamiento: la primera vez de cada consulta no sale de la caché.
  for (const ruta of consultasCalientes) {
    http.get(base + ruta);
  }
}

export function caliente() {
  const ruta = consultasCalientes[Math.floor(Math.random() * consultasCalientes.length)];
  const respuesta = http.get(base + ruta, { headers: { "Accept-Encoding": "br, gzip" } });
  check(respuesta, { "200": (r) => r.status === 200 });
}

function mes(indice) {
  // 0 = 2015-01 … 139 = 2026-08 (los meses que cubre la release real).
  return `${2015 + Math.floor(indice / 12)}-${String((indice % 12) + 1).padStart(2, "0")}`;
}

export function frio() {
  // El espacio de claves es enorme (territorio × combinación de tipos × desde × hasta ≈ 10⁶): casi cada petición es
  // distinta, no sale de la caché y obliga a ejecutar la consulta en DuckDB.
  const territorio = territorios[Math.floor(Math.random() * territorios.length)];
  const elegidos = tipos.filter(() => Math.random() < 0.5);
  const tipo = (elegidos.length > 0 ? elegidos : ["hotel"]).join(",");
  const desde = Math.floor(Math.random() * 130);
  const hasta = desde + Math.floor(Math.random() * (139 - desde));
  const respuesta = http.get(`${base}/v1/oferta?territorio=${territorio}&tipo=${tipo}&desde=${mes(desde)}&hasta=${mes(hasta)}&medidas=plazas`);
  check(respuesta, { "200": (r) => r.status === 200 });
}
