# Pulido de Mañana a un vistazo — 8 de octubre de 2026

Referencia: captura 73804, sobre `main` en `fbe32a7`.
Alcance: presentación de esta pantalla, sin cambiar itinerario, permisos o contratos.

| ID | Criterio independiente | Verificación prevista | Estado |
|---|---|---|---|
| tomorrow-ui-01 | La actividad presenta hora, título y lugar útil sin duplicar el título; su acción de detalle resulta reconocible. | Revisión de composición, navegación existente y compilación Android. | Implementado; revisión de fuentes realizada. |
| tomorrow-ui-02 | El alojamiento se presenta como un elemento identificado, con nombre y acción clara para Maps. | Revisión de fuentes, datos ausentes, textos largos y acción existente. | Implementado; revisión de fuentes realizada. |
| tomorrow-ui-03 | Documentos relacionados se muestra únicamente si hay documentos accesibles; no aparece un título ni mensaje vacío. | Revisar carga inicial, respuesta vacía, errores, contenido local y permisos existentes. | Implementado; revisión de fuentes realizada. |
| tomorrow-ui-04 | Ver mañana usa una acción editorial clara, con icono y texto; conserva la selección del día y el regreso al itinerario. | Revisión ES/EN, controles de 48 dp y compilación Android. | Implementado; revisión de fuentes realizada. |

Conservar actualización parcial, errores con reintento, estado offline y aislamiento
de cuenta/viaje. Las comprobaciones de fuentes y compilación se distinguen de una
revisión nativa. Sin push ni instalación sobre la app habitual en esta tarea.

## Evidencia de esta revisión

Por indicación explícita del usuario («Mantener sin pruebas»), no se ejecutan
pruebas automatizadas ni revisión nativa. La compilación Android se registra
separadamente y no acredita funcionamiento ni aspecto en el teléfono.

La revisión de fuentes comprobó el uso de `EditorialUi`, márgenes de 24 dp,
acciones con tamaño mínimo de 48 dp y texto que puede ocupar varias líneas.
Se conservaron las acciones existentes de detalle, Google Maps y selección del
día. Los nuevos textos y descripciones están disponibles en español e inglés.

El contenedor de documentos nace oculto y sólo se muestra cuando hay elementos
accesibles. Se mantienen la comprobación de permisos al abrir, la cancelación y
las comprobaciones de cuenta/viaje. El error con reintento y el estado offline
permanecen separados del contenedor; ocultarlo no oculta esos estados.

Fuente congelada para compilar, SHA-256:

| Archivo | SHA-256 |
|---|---|
| `Pages/TomorrowOverviewPage.cs` | `DCB3F0A6CB1328373D075592425C834E8E1462B714BFBF429CDD92D251EB0B81` |
| `Localization/AppResources.resx` | `CAB9F6685E251B044F87BBAB6D5636F4EFB540BF7F834043AC578C09C893217C` |
| `Localization/AppResources.es.resx` | `A702C53F88CB8C9126AA00EF541EDF7560D8A1A9D76407E2B0FAA994749350B9` |

Los archivos de la tabla son relativos a `src/TravelCompanion.Mobile`.
La primera compilación quedó sin resultado confirmado por falta de respuesta
del entorno local. Los recursos de traducción se modificaron después para el
pulido de Sorpréndeme; sus hashes anteriores describen esa instantánea inicial,
no las fuentes actuales. La nueva compilación conjunta y sus hashes se registran
en `artifacts/express-surprise-20261008` y en
`docs/plans/express-surprise-polish.md`: compilación Android Release ARM64
completada con código de salida 0 y sin errores. La página mantuvo su hash
`DCB3F0A6CB1328373D075592425C834E8E1462B714BFBF429CDD92D251EB0B81`;
el manifiesto registra los hashes actuales de ambos recursos de traducción.
Ninguna revisión nativa ni prueba
automatizada está acreditada por esta documentación.

La posterior modificación de los textos de Journal se compiló junto con esta
página y Sorpréndeme. La evidencia de las fuentes actuales está en
`docs/plans/journal-sync-feedback.md` y `artifacts/journal-sync-20261008`;
la página de Mañana conserva su hash y la compilación Android terminó sin errores.

Después del sorteo de Sorpréndeme se recompiló el conjunto con los recursos
ES/EN actuales: Android Release ARM64, salida 0, cero errores. La instantánea y
APK correspondientes están en `artifacts/express-random-20261008`, documentadas
en `docs/plans/express-surprise-polish.md`. No añade cobertura funcional o nativa.
