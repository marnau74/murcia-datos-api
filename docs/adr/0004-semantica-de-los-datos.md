# 0004 · Semántica de los datos: nulos, agregación y totales

- **Estado:** aceptada
- **Fecha:** 2026-10-01

## Contexto

Una API de estadísticas puede dar cifras bien formadas y equivocadas: un mes sin publicar convertido en
cero, un total al que le falta una parte, una media de porcentajes tratada como suma. Estas reglas son
de lo más importante del proyecto y están cubiertas con tests.

## Decisión

1. **Un dato que no existe es `null`, nunca 0** (igual que en el proyecto de datos). Un mes sin dato no
   cuenta como mes del periodo.
2. **Flujos se suman; existencias, tasas e índices se promedian.** Cada medida declara su agregado
   (`/v1/medidas`). Viajeros y pernoctaciones suman; plazas, establecimientos, ocupaciones e índice de
   precios hacen la media de los meses.
3. **Medidas solo mensuales.** La variación interanual no se promedia entre meses: pedirla con
   `agregacion` distinta de `mes` es un error 400 que lo explica.
4. **`meses` en cada periodo agregado**: cuántos meses con dato entran. Un año en curso, o uno con un
   hueco, no se hace pasar por completo. El explorador pinta esos periodos con el círculo vacío.
5. **`residencia=total` exige todas las residencias en cada mes.** Si a un mes le falta una, su total es
   `null` (no la suma parcial, que sería un número falso).
6. **No se mezclan fuentes**: cada fila lleva su `fuente` y los territorios del INE y de murciaturistica
   no se suman entre sí.
7. **Indicadores**:
   - *Estacionalidad*: solo años completos (12 meses con dato) y sin provisionales salvo petición; la
     cuota de cada mes es su parte del total anual, promediada entre años; la relación agosto/enero se
     calcula con las cuotas **sin redondear**.
   - *Variación*: del último mes con dato sin huecos desde enero y del acumulado hasta ese mes, frente
     al año anterior y a 2019. Si a la referencia le falta un mes, la variación es `null`: no se
     compara contra un periodo incompleto. Una referencia en cero no produce una división por cero.
8. **Provisionales** marcados (`provisional`, `meta.provisional_desde`).

## Consecuencias

- Hay respuestas con muchos `null` y `meses` por debajo de lo esperado: es información, no un fallo.
- Los consumidores que quieran un total anual deben mirar `meses` antes de compararlo.
