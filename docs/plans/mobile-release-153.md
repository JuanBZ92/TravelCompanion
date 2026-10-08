# Publicación Android 153

Petición: pushear los cambios locales a `main` y subir la APK a Google Drive.
Se conserva la indicación del usuario de no ejecutar pruebas. No incluye
instalación en el teléfono ni cambios de API o base de datos.

## Contenido

- Presentación de Mañana a un vistazo: actividad y hotel con acciones claras,
  sección de documentos oculta cuando no hay documentos accesibles.
- Sorpréndeme: presentación editorial y búsqueda personalizada; si no hay
  intereses guardados, sorteo temporal entre las nueve categorías.
- Journal: solicitud de sincronización al guardar, estados claros y reintento,
  sin retrasar la confirmación local ni sobrescribir ediciones nuevas.
- Versión Android incrementada de 152 a 153; identidad habitual conservada.

Los criterios y revisiones de fuentes están en `tomorrow-overview-polish.md`,
`express-surprise-polish.md` y `journal-sync-feedback.md`. La revisión de
publicación no encontró pendientes conocidos ni necesidad de migraciones.

## Entrega y evidencia

| ID | Criterio | Estado |
|---|---|---|
| release153-build | APK Release ARM64 con versión 153, API habitual y diagnósticos desactivados; fuentes y artefacto identificados. | Completado: salida 0, versión y firma comprobadas |
| release153-main | Cambios revisados publicados en main, sin forzar el historial. | Preparado; resultado de push y commit exacto en `delivery-status.json` |
| release153-drive | APK subida a la carpeta TravelCompanion existente; comprobar metadatos tras completar la subida. | Completado: nombre, tamaño y carpeta comprobados por lectura posterior |

Registro local de entrega: `artifacts/release153-20261008`. Incluye los hashes
de fuentes, compilación, APK y resultado de publicación. La compilación y
comprobación de metadatos del paquete no acreditan pruebas funcionales ni nativas.

APK: `YUKU-Japan-153-journal-sorpresa.apk`, 25.205.628 bytes; SHA-256
`C8BD1A1DDFD29DDB91F9476D466D6D53D8E8CF6ADBF5FB048779AA8065A666DB`.
Los 365 archivos de la instantánea conservaron sus hashes al terminar la
compilación. La firma coincide con la versión 152.

[APK en Google Drive](https://drive.google.com/file/d/1CYr2g8601mlWks_-dFtTicjUL1TZiEZ-/view?usp=drivesdk).
La subida finalizó correctamente y la lectura de metadatos confirmó versión en
el nombre, tamaño y carpeta. El conector no devolvió hashes remotos; el hash
anterior identifica el archivo local enviado.

El resultado de publicación en `main` se registra después del commit para poder
incluir su identificador real y la lectura del remoto, sin anticipar el éxito.
