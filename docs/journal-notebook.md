# Journal como cuaderno de viaje

## Funcionamiento

Journal permite escribir recuerdos libres con un viaje activo, sin actividades y sin pase. El botón principal abre el editor con la fecha de hoy; se pueden elegir fechas anteriores o posteriores al itinerario. Título y lugar son opcionales. El texto admite 2.000 caracteres y cada recuerdo hasta diez fotos. Una entrada necesita texto o al menos una foto para confirmarse.

La lista presenta recuerdos en tarjetas compactas y cronológicas. Cada tarjeta lleva fecha, título o lugar como encabezado, un extracto breve y una miniatura si hay portada. Al abrirla, el detalle da prioridad a la lectura y a la foto; «Editar recuerdo» queda visible y «Añadir fotos», actividad y eliminación se agrupan en opciones secundarias.

Desde el editor libre, «Elegir actividad del día» permite buscar por título o ciudad dentro de la fecha seleccionada. Usa el itinerario guardado en el dispositivo y funciona sin conexión. Elegir una actividad copia su nombre al campo Lugar y vuelve al mismo editor, sin modificar el texto ni las fotos. Se puede seguir escribiendo o cambiar el lugar a mano. Cerrar el buscador no cambia el recuerdo. Si no hay coincidencias, se puede continuar escribiendo libremente. El menú general del diario conserva por separado el flujo de recuerdos vinculados a una actividad.

El editor conserva un borrador cifrado después de 650 ms sin cambios y al cerrar. «Continuar borrador» recupera texto, fecha, lugar, fotos y portada. «Guardar recuerdo» confirma localmente y solicita sincronización. El menú del diario conserva «Elegir una actividad» y la creación del álbum PDF. Los borradores no forman parte del álbum.

La lista muestra primero el contenido local y luego carga las miniaturas y consulta la API en segundo plano, con un límite de 12 segundos para esa consulta. Guardar confirma el recuerdo localmente y muestra el estado pendiente sin esperar la red; la sincronización se reintenta al volver al diario. Los borradores sin cambios no vuelven a cifrarse y las lecturas locales no reescriben el índice si no cambió.

Las notas confirmadas se sincronizan; fotos y borradores permanecen únicamente en el dispositivo. El aviso se muestra en edición y lectura. No hay subida de fotos ni copia recuperable después de reinstalar. Cancelar el selector no añade fotos. La selección se procesa antes de modificar el borrador.

## Contratos y compatibilidad

Se mantienen los endpoints de Journal vinculado a actividades. Las entradas libres usan una identidad UUID distinta, generada por el móvil, sin inventar identificadores de actividades.

- `GET /api/mobile/trips/{tripId}/journal/free-entries`: entradas del propietario, incluidos los marcadores de eliminación.
- `PUT /api/mobile/trips/{tripId}/journal/free-entries/{id}`: `title`, `place`, `date`, `notes`, `expectedRevision`, `mutationId`.
- `DELETE /api/mobile/trips/{tripId}/journal/free-entries/{id}`: cuerpo con `expectedRevision` y `mutationId`.

PUT y DELETE devuelven `{ saved, entry }`: 200 si se acepta o se repite la última mutación; 409 con la versión actual si hay conflicto. Una identidad eliminada nunca puede recrearse, incluso con una revisión actualizada. Un DELETE anterior a la creación genera un marcador de eliminación. Autorización y bloqueo por viaje se verifican dentro de la operación. Los cambios libres no modifican reservas ni la revisión del itinerario.

El índice móvil existente se amplía con campos opcionales. Conserva notas antiguas, fotos, portadas, escrituras pendientes y conflictos. Los borradores se guardan por separado bajo el mismo prefijo cifrado de usuario/viaje. Eliminación de cuenta/viaje y vinculación anónima incluyen estos datos.

## Publicación posterior

La implementación inicial se validó sin modificar producción. La actualización Android 107 usa la API alojada; el push a main no despliega Render porque el despliegue automático está desactivado. La migración y la publicación de API siguen siendo pasos independientes. Hasta entonces, los recuerdos libres nuevos se conservan localmente, pendientes de sincronización. Orden recomendado:

1. Respaldar la base de datos y verificar la restauración según el runbook existente.
2. Generar y revisar SQL de `20261002171350_AddJournalFreeEntries`, partiendo de `20261002142816_OptimizeQueryWorkloads` o del identificador real aplicado que muestre `dotnet ef migrations list`.
3. Aplicar primero la migración aditiva, con conexión administrativa y límites de espera de la sesión: `SET lock_timeout = '5s'; SET statement_timeout = '60s';`. No habilitar migraciones automáticas en producción. Ante bloqueo, cancelar y reprogramar.
4. Publicar API; verificar GET/PUT, reintento con el mismo mutationId, revisión obsoleta y DELETE con dos sesiones de prueba.
5. Publicar el móvil. Los clientes anteriores siguen usando los contratos originales. Si el nuevo móvil encuentra una API anterior o sin conexión, conserva localmente sus recuerdos y mutaciones.

Generación del script (sustituir BASE por el identificador aplicado, sin ejecutar contra producción):

```powershell
dotnet ef migrations script BASE 20261002171350_AddJournalFreeEntries --project src/TravelCompanion.Api --output artifacts/journal-migration.sql
```

La tabla nueva tiene clave UUID, claves foráneas con eliminación por cuenta/viaje, índice de viaje e índice compuesto propietario/viaje/fecha/UUID.

## Reversión sin pérdida de contenido

Revertir los binarios de API y móvil conservando la tabla y la fila de historial de migraciones. No ejecutar Down si se han creado recuerdos o marcadores de eliminación. Down incluye una comprobación que rechaza expresamente borrar una tabla con contenido. Solo una base de prueba vacía permite revertir físicamente la migración. Los marcadores deben conservarse para impedir que un dispositivo antiguo restaure entradas borradas.

## Validación reproducible

Las pruebas PostgreSQL usan `TRAVELCOMPANION_TEST_POSTGRES` apuntando exclusivamente a una base local de pruebas. Crean esquemas únicos y los eliminan al terminar. Incluyen migración, índices, acceso gratuito, aislamiento entre cuentas, reintentos, escritura concurrente desde dos contextos, conflictos, eliminación irreversible, protección de Down y conservación del recuerdo tras borrar la actividad.

```powershell
dotnet test tests/TravelCompanion.Mobile.Tests/TravelCompanion.Mobile.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Api.Tests/TravelCompanion.Api.Tests.csproj --verbosity minimal
dotnet test tests/TravelCompanion.Shared.Tests/TravelCompanion.Shared.Tests.csproj --verbosity minimal
dotnet build src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-android --verbosity minimal
```

La APK de revisión usa `com.yuku.travelcompanion.journalreview`, título «Yuku Journal», versión 107 y backend local `http://127.0.0.1:5088` mediante `adb reverse tcp:5088 tcp:5088`. Es una instalación distinta de la app habitual y no se distribuye como versión de producción.
El archivo entregado queda en `artifacts/mobile/Yuku-Journal-review-v107.apk`. La conexión al backend de pruebas requiere que este PC siga ejecutándolo y que se mantenga el enlace USB; el contenido local del diario sigue disponible sin conexión.

Comando de empaquetado de revisión (ARM64 del dispositivo conectado, ensamblados embebidos):

```powershell
dotnet build src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-android --verbosity minimal -p:RuntimeIdentifier=android-arm64 -p:AndroidUseAssemblyStore=true -p:AndroidIncludeDebugSymbols=false -p:AndroidEnableMarshalMethods=false -p:ApplicationId=com.yuku.travelcompanion.journalreview "-p:ApplicationTitle=Yuku Journal" -p:ApplicationVersion=107 -p:TravelCompanionApiBaseUrl=http://127.0.0.1:5088
```

Si se cambian opciones de empaquetado entre compilaciones, limpiar primero el objetivo Android. Durante esta revisión un paquete incremental produjo un error nativo de registro de MAUI al arrancar; se resolvió con compilación limpia, los parámetros anteriores e instalación `adb install --no-incremental -r`. Se verificó después el arranque y el inicio de sesión. No se borraron los datos de la app.

## Evidencia de revisión — 2 de octubre de 2026

Revisión del plan:

| Área | Entrega |
|---|---|
| Lectura | Cabecera compacta, acción de escritura, fechas cronológicas, ciudades, lista virtualizada, portada y dos miniaturas, lectura completa y acciones secundarias. |
| Escritura | Entradas sin actividad, fecha fuera del itinerario, título/lugar opcionales, límites visibles, recuerdos solo con fotos y rechazo de entradas vacías. |
| Borradores | Almacenamiento cifrado separado, autoguardado y guardado al salir o detenerse la ventana, recuperación, descarte confirmado y exclusión del PDF. |
| Compatibilidad | Notas vinculadas intactas, UUID libres diferenciados, fotos/portadas locales, conflictos conservados, limpieza por cuenta/viaje y transferencia verificada. |
| Backend | Tabla e índices aditivos, endpoints propios, autorización gratuita, revisión optimista, mutación idempotente y marcadores de eliminación. |
| Calidad | ES/EN, botones de 48 dp, texto ampliado, miniaturas en lista y originales al abrir, archivos ausentes tolerados, pruebas automatizadas y Android físico. |
| Entrega | Migración y SQL preparados, instrucciones de publicación/reversión y APK separada. Sin push ni publicación en producción. |

- API: suite completa de 405 pruebas pasada con PostgreSQL 17 local y cero omitidas; una prueba adicional de vinculación anónima pasada después, 406 casos verificados en total.
- Móvil: 235 pruebas pasadas. Incluyen borradores tras recrear el store, aislamiento por cuenta/viaje, selección cancelada, fallo de lectura y cambio de cuenta durante una selección, notas antiguas serializadas, portadas, límites, entradas solo con fotos, conflictos de edición y eliminación, reintentos y marcadores de eliminación.
- Shared: 60 pruebas pasadas.
- EF: no hay diferencias pendientes entre el modelo y la migración. SQL generado en `artifacts/journal-migration.sql`.
- Android: compilación final correcta, sin advertencias ni errores; APK de revisión con identificador separado.
- Instalación comprobada: app habitual en versión 106 y APK de revisión en versión 107, coexistiendo sin sustituir la instalación original.
- Android físico: diario vacío sin actividades; escritura sin título; borrador recuperado después de cierre forzado y reapertura; confirmación y comprobación de la fila en PostgreSQL; recuerdo solo con tres imágenes sintéticas; cambio de portada; galería completa; lectura y extracto de 2.000 caracteres; mezcla de fechas anteriores al viaje y recuerdos antiguos.
- Exportación Android sin Wi-Fi ni datos: PDF de cuatro páginas, día elegido incluido y día desmarcado excluido. Álbum mixto de siete páginas comprobado por extracción de texto: nota antigua, texto libre y recuerdo largo presentes.
- Foto ausente: se movió temporalmente un archivo sintético dentro de la APK de revisión; apareció el aviso y se pudo exportar sin esa foto. El archivo se restauró después. Wi-Fi y datos también quedaron restablecidos.
- APK final: probado «Pendiente de sincronizar» con el servidor apagado; al simular una escritura desde otra sesión apareció «Conflicto», se compararon versiones y se pudo conservar y sincronizar la versión local. El PDF final se guardó mediante el selector nativo y respetó la segunda foto elegida como portada.
- Textos españoles e ingleses: claves verificadas sin ausencias ni duplicados. La revisión visual principal se hizo en español con el tamaño de texto ampliado del dispositivo.

Las capturas, PDFs y logs locales están en `artifacts/journal-*`. La carga y los recuerdos de prueba son sintéticos; la base `tc_journal_review` se creó específicamente para esta revisión y no se consultó producción. La concurrencia se verificó con dos contextos PostgreSQL; no se utilizó un segundo teléfono físico.
