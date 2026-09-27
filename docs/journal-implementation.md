# Journal: implementación y validación

## Entrega

- Notas independientes del itinerario con API autenticada, revisión optimista, reintentos idempotentes y migración aditiva `AddPersonalJournal`.
- Importación de notas antiguas y compatibilidad con el editor anterior. Los recuerdos de reservas protegidas no alteran las reservas. La eliminación lógica de cuenta borra sus notas; la vinculación de la cuenta anónima transfiere su propietario.
- Journal agrupado por fecha/ciudad, alta desde actividades consultables, edición con confirmación al descartar, galería, portada y recuperación de conflictos sin perder el borrador.
- Notas pendientes cifradas; reintento al reconectar o refrescar. Las fotos se guardan en almacenamiento privado cifrado, independiente del caché descartable, con máximo 10 por recuerdo. Copias normalizadas a 2048 px y miniaturas de 512 px en Android; el selector aplica orientación y retira metadatos. No cámara ni sincronización de fotos.
- Exportación Android nativa a PDF: días seleccionados, título, portada, notas completas y fotos; progreso/cancelación, vista previa mediante visor del sistema, guardar mediante selector de destino y compartir. Los códigos de confirmación y los documentos no forman parte del contrato de exportación.
- Navegación Shell con Viaje, Mapa, Asistente, Journal y Cuenta, sin overflow para los cinco destinos. Se mantienen las restricciones de Asistente. Documentos desde Viaje/Cuenta y Pase/Salir desde Cuenta.

## Validación automatizada

Pruebas de Journal cubren reservas protegidas, Free, aislamiento por viaje/cuenta, reintentos, conflicto, eliminación de actividad, compatibilidad del editor anterior, borrado de cuenta, notas offline, fotos locales, límite de fotos y vinculación de cuenta.

`PostgresJournalTests` verifica la migración con notas antiguas, reintentos simultáneos, dos ediciones en competencia y cascadas. Requiere `TRAVELCOMPANION_TEST_POSTGRES` apuntando a una base descartable; usa y elimina un esquema único. Ejecutada correctamente en PostgreSQL 17 temporal y aislado durante la revisión del 28/09/2026, junto con las otras cuatro pruebas de PostgreSQL.

## Orden de publicación

1. Ejecutar las pruebas PostgreSQL en staging y aplicar la migración aditiva con el procedimiento normal de despliegue. No se cambió el arranque para aplicar migraciones automáticamente.
2. Publicar la API compatible y comprobar GET/PUT de Journal, incluido conflicto 409 y una cuenta Free.
3. Distribuir la APK. Mientras la API anterior siga desplegada, las notas nuevas quedan pendientes en el dispositivo; no se debe considerar validada su sincronización.
4. Verificar Android Release en 1111, 3333, 3334 y Free: editar, agregar/quitar/portada de fotos, cancelar sin guardar, modo avión/reconexión, cambio de cuenta, vinculación, borrado de actividad, PDF largo y fotos faltantes, visor ausente, cancelar selector, compartir y guardar. Confirmar TalkBack, texto grande y Atrás desde cada pantalla.

La validación física sigue pendiente: `adb devices` no detecta teléfono. Compilar la APK no sustituye esta revisión. La migración `20260927212716_AddPersonalJournal` fue aplicada en Render el 28/09/2026, después de generar un respaldo completo. Importó 18 notas existentes. La publicación de API y APK se realiza después de esta verificación.

## Resultado de esta sesión

- API: 376 pruebas aprobadas, incluidas las cinco de PostgreSQL.
- Mobile: 170 pruebas de lógica aprobadas.
- Shared: 51 pruebas aprobadas.
- EF Core: el modelo coincide con la migración generada.
- PostgreSQL: migración, importación de notas, concurrencia, idempotencia y cascadas verificadas en una instancia temporal aislada.
- Android: APK Release versión 92 generada mediante `Publish-Mobile.ps1`; advertencias XamlC de bindings Source corresponden a la configuración intencional del proyecto. La validación en teléfono continúa pendiente.

## Ajustes de la revisión

- Documentos vuelve a mostrar navegación de regreso al abrirse desde Viaje o Cuenta.
- La exportación PDF informa un fallo si Android no puede crear una página, en lugar de continuar con una referencia nula.
- No se detectaron funciones pendientes de implementación del plan. La validación manual en dispositivo, accesibilidad y flujos del visor/selector Android sigue pendiente y no se considera cubierta por las pruebas automatizadas.

