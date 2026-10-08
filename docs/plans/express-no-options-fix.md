# Express: intereses múltiples y resultados vacíos

Incidencia: Comida, Cultura y Naturaleza, cerca del próximo plan, 120 minutos,
produce un mensaje de ausencia de opciones. Base de revisión: `76deaa6`.
Se conserva la instrucción del usuario de no ejecutar pruebas.

## Criterios antes de modificar código

| ID | Criterio | Flujo y verificación prevista | Estado |
|---|---|---|---|
| express-empty-01 | En Express, cada interés seleccionado es una alternativa válida; el primero no elimina los demás. | Normalización de criterios con ventana y ranking; revisión de fuentes, compilación API. | Implementado; fuente revisada y API compilada |
| express-empty-02 | Buscar cerca del próximo plan utiliza su ciudad y coordenadas verificadas por el backend. | Reserva del viaje activo y catálogo; aislamiento, ancla y permisos conservados. | Implementado; fuente revisada y API compilada |
| express-empty-03 | Conservar ventana real, compromisos, traslados, cercanía y exclusiones; sin ampliar acceso ni inventar duración. | Revisión de móvil, Shared y backend; ningún cambio de API o tablas. | Conservado; fuente revisada |
| express-empty-04 | El usuario ve por qué se limita la búsqueda: tiempo efectivo con traslados y cercanía; un mensaje del backend no se sustituye siempre por el genérico. | Área Express, respuesta vacía y textos ES/EN; compilación Android. | Implementado; fuente revisada y Android compilado |
| express-empty-05 | Reintentos y cambios de cuenta/viaje conservan sus protecciones; sin alterar otros modos del asistente. | Revisión de fuentes de contextos, revisión independiente. | Conservado; revisión independiente realizada |
| express-empty-06 | Si no hay opciones cerca, ofrecer ampliar a la ciudad de forma explícita, conservando intereses, tiempo y exclusiones. | Reutilizar la acción existente de búsqueda por ciudad; no ampliar automáticamente ni presentar distancia garantizada. | Implementado; revisión independiente realizada y Android compilado |

## Hallazgos iniciales

- `NormalizeGuidedCriteria` elige la primera categoría salvo en la búsqueda de
  nueve categorías. El ranking aplica ese modo temático antes de la unión de
  categorías: comida puede descartar cultura y naturaleza prematuramente.
- La ciudad del catálogo se determina antes de resolver la reserva usada como
  ancla. El cliente actual envía su ciudad, pero el servidor debe usar el dato
  de la reserva verificada para evitar contextos incoherentes.
- Los 120 minutos son un máximo: se recortan al siguiente compromiso fijo y a
  medianoche. La visita más los traslados deben caber; cerca aplica 30 minutos
  a pie desde el ancla. Estas restricciones se conservan y se harán visibles.
- El móvil reemplaza mensajes de respuesta vacía por `ExpressNoOptions`, incluso
  cuando el backend explica que falta catálogo o contexto.

## Diagnóstico de la cuenta indicada

Después de la primera revisión, el usuario identificó su cuenta simulada, fecha
9 de octubre y próximo plan Rikugien Gardens. Se consultaron los datos mediante
una transacción PostgreSQL de solo lectura con límite de 15 segundos. No se
ejecutó la búsqueda, no se usaron cuotas del asistente y no se modificaron datos
o sesiones. Se registraron únicamente metadatos de horarios y catálogo, sin
contraseñas, tokens, notas personales o confirmaciones.

- El reloj de PostgreSQL en esa lectura era 8 de octubre, 17:45 UTC: 9 de
  octubre, 02:45 en Asia/Tokyo. La fecha distinta de Barcelona es correcta.
- Rikugien está guardado el 9 de octubre a las 14:00, zona Europe/Madrid;
  representa las 21:00 en Japón. El editor actual utiliza la zona del dispositivo
  para horarios exactos; no se cambió ese comportamiento ni se reinterpretaron
  reservas existentes en esta corrección.
- Con el catálogo actual, la estimación de cercanía utilizada por el ranking
  (distancia geográfica, 12 minutos/km, redondeada hacia arriba) sólo encuentra
  el propio Rikugien dentro de 30 minutos. Está ya en el itinerario y se excluye.
- SCAI The Bathhouse queda a unos 31 minutos y también está en el itinerario;
  Kayaba Coffee queda a 32 y su duración sugerida es 75 minutos. Ambos exceden
  el radio original. Aumentar de 60 a 120 minutos no modifica ese radio.
- Esta es una explicación suficiente del vacío con esos datos actuales,
  independientemente de la hora de inicio. El error de intereses múltiples es
  adicional; arreglarlo por sí solo no crea lugares dentro de ese radio.

Evidencia local en `artifacts/express-empty-20261008/account-context-readonly.json`
y `rikugien-catalog-distance-review.json`. Son lecturas y cálculos de diagnóstico,
no respuestas del asistente ni pruebas funcionales. El catálogo leído excluye
AdminOnly, pero no sustituye la comprobación de permisos del endpoint.

La referencia del usuario a una ventana 15:00–16:00 no se verificó en el móvil;
no se atribuye a un fallo del reloj sin esa evidencia. Los datos no demuestran
qué hora exacta tenía su petición anterior ni si el teléfono tenía caché antigua.

## Corrección y alcance

Se neutraliza el primer tema únicamente en Express con varios intereses. La
unión de categorías, permisos, tiempos, compromisos y exclusiones siguen en el
ranking. La ciudad del ancla validada sustituye una ciudad de petición incoherente.
El móvil muestra tiempo efectivo/cercanía y conserva mensajes vacíos del backend.

Si no hay resultados cerca, aparece la acción existente de búsqueda por ciudad.
El usuario debe elegirla y volver a buscar; no hay ampliación automática. La
ciudad procede del ancla cuando corresponde; se conservan intereses, duración e
historial de propuestas, y se limpian únicamente ubicación/ancla y petición de
reintento. En modo ciudad se mantiene la advertencia de traslados estimados;
no se promete que una visita esté cerca ni que encaje desde una ubicación desconocida.

No se ejecutó la app ni pruebas automatizadas, por indicación del usuario. La
compilación no acredita pruebas funcionales o nativas. Sin push, instalación o
despliegue en esta corrección hasta una petición de publicación.

## Cierre y evidencia de compilación

Los seis criterios se compararon con las rutas de producción modificadas. La
revisión independiente no encontró pendientes de implementación en este alcance.
La verificación funcional automatizada y nativa permanece sin ejecutar por la
instrucción del usuario; no se acredita con compilaciones o lecturas de datos.

- API: compilación correcta, 0 errores y 0 advertencias. Registro local:
  `artifacts/express-empty-20261008/api-build.log`.
- Android Release ARM64: compilación correcta, 0 errores y 207 advertencias.
  Registro final: `artifacts/express-empty-20261008/android-build.log`.
- Se verificaron sin cambios las ocho fuentes/configuraciones de la instantánea
  después de compilar; `git diff --check` terminó correctamente. La base es
  `76deaa6d0e380026535dceb11b32f90f7ca259dc`, con cambios locales sin publicar.
- Instantánea local: `artifacts/express-empty-20261008/source-snapshot.json`,
  SHA-256 `CBF549699AE1DE7A41B31467CB3AC133A03C4AA30160E20B1C4F9AE45E80C4D3`.
- APK local: `artifacts/express-empty-20261008/Yuku-local-express-v153.apk`,
  SHA-256 `5254B7B415B03DEF899703EFC4686D416F04D5DE09F8B0E42928911A65157DFD`.
  Es exclusivamente un artefacto de compilación; no se instaló ni se subió a Drive.
  Conserva versión 153; una publicación posterior deberá incrementar la versión.
- Evidencia de cierre: `artifacts/express-empty-20261008/closure-evidence.json`.

No se cambiaron permisos, reservas existentes, horarios del editor, contratos,
migraciones o producción. La evidencia no demuestra que la búsqueda histórica
del usuario produjera un resultado específico, ni garantiza alternativas cercanas
donde el catálogo actual no las contiene.

## Publicación posterior solicitada

El usuario autorizó posteriormente push a `main` y subida de APK a Drive. La
versión de entrega será 154; se conserva la compilación local 153 anterior como
evidencia histórica. Los criterios y el artefacto de publicación se registran
en `mobile-release-154.md` y `artifacts/release154-20261008`. No se ejecutan
pruebas ni se instalan aplicaciones en el teléfono durante esta entrega.
