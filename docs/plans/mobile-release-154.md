# Publicación Android 154

Petición: publicar los cambios locales en `main` y subir la APK a Google Drive.
Se conserva la indicación de no ejecutar pruebas. No incluye instalación en el
teléfono ni migraciones de base de datos.

## Contenido

- En Un plan para ahora, los intereses seleccionados se consideran alternativas;
  el primer interés no descarta los demás antes del ranking.
- La búsqueda cerca del próximo plan utiliza la ciudad del ancla verificada por
  el backend, conservando permisos, exclusiones, ventana y traslados.
- La pantalla explica la cercanía estimada y el tiempo efectivo. Conserva los
  mensajes de respuesta vacía del backend y permite elegir ampliar a la ciudad
  cuando no hay opciones cerca, sin ampliar automáticamente la búsqueda.
- Versión Android incrementada de 153 a 154; identidad habitual conservada.

La revisión y el diagnóstico están en `express-no-options-fix.md`. No se cambian
reservas existentes, zona horaria del editor, contratos o tablas.

## Criterios de entrega

| ID | Criterio | Evidencia requerida |
|---|---|---|
| release154-build | APK Release ARM64, versión 154, API habitual y diagnósticos desactivados; fuentes y artefacto identificados. | Compilación correcta, metadatos, firma compatible y hashes locales. |
| release154-main | Cambios revisados publicados en main, sin forzar historial. | Commit exacto y lectura posterior del remoto. |
| release154-drive | APK subida a la carpeta TravelCompanion existente. | Resultado completado y lectura posterior de nombre, tamaño y carpeta. |

El registro local de entrega es `artifacts/release154-20261008`. Su archivo
`delivery-status.json` registra el resultado real de publicación después del
commit y las comprobaciones posteriores; no se anticipa el éxito. La entrega sólo
se considera completa si los tres criterios están confirmados en ese registro.
La compilación y los metadatos del paquete no acreditan pruebas funcionales o
nativas, que no se ejecutan por indicación del usuario.

## Artefacto verificado

Android Release terminó correctamente. El paquete tiene versión 154, identidad
`com.yuku.travelcompanion.app`, sólo ARM64 y firma coincidente con la versión 153.
Los metadatos compilados apuntan a la API habitual y desactivan diagnósticos.
Las 366 fuentes/configuraciones de la instantánea conservaron sus hashes durante
la compilación. La fuente API modificada coincide con la compilación API correcta
registrada en `artifacts/express-empty-20261008`; no cambió desde esa compilación.

APK: `YUKU-Japan-154-plan-ahora.apk`, 25.197.501 bytes; SHA-256
`F16E4DD9F6075C7AA7D2D38F9DA746059E03186657F64DDE77E4021CD6832847`.
La prueba del paquete está en `artifacts/release154-20261008/apk-proof154.json`.

[APK en Google Drive](https://drive.google.com/file/d/14nLcWQ8fSGGOPBJ2kicnJkyY00jkTwLb/view?usp=drivesdk).
La subida finalizó y la lectura posterior confirmó nombre, tamaño y carpeta
TravelCompanion. El conector no devolvió hashes remotos; el hash anterior
identifica el archivo local enviado. Se conserva la organización y los permisos
existentes de Drive.
