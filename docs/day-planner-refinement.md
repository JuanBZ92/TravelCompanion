# Ajustes de planificación y selector del viaje

Revisión del 5 de octubre de 2026. La implementación y las pruebas se prepararon localmente y conservan los cambios de la auditoría anterior. No requiere una migración nueva. El usuario solicitó después publicar el código en `main` y continuar la revisión visual con el Android conectado; el estado de esa revisión se registra por separado de las pruebas automatizadas.

## Cambios

- Duración: una fila de cuatro opciones de 1/3/5/7 días, selección visible y una indicación del pase. Se conservan las fechas válidas y los permisos gratuitos.
- «Tus próximos días»: las flechas existentes permiten cambiar una sola idea en su tarjeta. El resto del resultado y la selección permanecen. Las alternativas excluyen las propuestas visibles y anteriores, además de los lugares ya guardados. No se consume otra generación. Si el catálogo no tiene otra idea compatible, se conserva la actual.
- Viaje: fecha y hotel integrados en la tarjeta central; se elimina la tarjeta separada de alojamiento. El hotel usa la acción existente de Maps cuando está disponible. Sin hotel se muestra la ciudad. Los botones adyacentes y los deslizamientos horizontales de la franja cambian el día; no hay navegación circular fuera del viaje.
- Robustez: UUID por reemplazo, revisión optimista de propuesta separada de la revisión del itinerario, recibos de reintento y mutaciones pendientes cifradas. Un resultado incierto impide guardar una selección desactualizada. No se cambia una reserva al reemplazar una idea.

La [guía de publicación](day-planner-rollout.md) documenta el endpoint nuevo, la compatibilidad y el orden API antes de APK. Los metadatos de reemplazo se almacenan en el JSON de la propuesta existente; no se añade una tabla.

## Validación local

- Lógica móvil: 350 pruebas aprobadas, cero fallos y cero omitidas. Cubre selección, reintentos con la misma mutación, recuperación de borradores, fallo de guardado, cambio de contexto, agotamiento y conflictos entre dispositivos.
- Shared: 69 pruebas aprobadas, cero fallos y cero omitidas. Incluye compatibilidad de respuestas antiguas y serialización de las revisiones y solicitud original del reemplazo. Una aserción inicial comparaba referencias de colecciones tras deserializar; se corrigió para comprobar campos y contenido.
- API: 506 pruebas aprobadas, cero fallos y cero omitidas. Incluye 33 pruebas identificadas como PostgreSQL, de las cuales veinte verifican el planificador y siete el reemplazo. Las siete nuevas cubren reintentos/concurrencia, revisiones distintas, carrera entre reemplazar y guardar, rollback, revocación, cancelación y eliminación. Se ejecutaron contra PostgreSQL real local, con esquemas aislados. Las comparaciones iniciales de JSON se ajustaron a su representación normalizada por `jsonb`; no se sustituyó PostgreSQL por un proveedor en memoria.
- Builds API y worker: cero advertencias y cero errores.
- Android: compilación final con cero advertencias y cero errores, en 1:25,05. Incluye botones con ajuste de texto para fechas bloqueadas y tamaños de fuente ampliados.
- Recursos y estructura: once pares nuevos ES/EN, sin claves duplicadas; XML de las tres pantallas válido y `git diff --check` limpio.

Logs y resultados TRX en `artifacts/day-planner-refinement/`. La APK `artifacts/mobile/DayPlannerReview-v117.apk` tiene paquete `com.yuku.travelcompanion.plannerreview`, nombre «Yuku Planner», versión 117 y arquitecturas ARM64/x86_64. Firma v1/v2/v3 verificada. Tamaño: 93.525.280 bytes; SHA-256: `6335C76BC52ABEBCC9FB8B0D3B040B0E683F437B7B4DBD12CE5E4596C1213477`. Usa exclusivamente `http://127.0.0.1:5188` y **no se instaló**. Se conserva la identidad y los datos de la app habitual.

La prueba HTTP `tools/DayPlanReview/Smoke.ps1` contra el backend sintético de `127.0.0.1:5188`, con PostgreSQL `127.0.0.1:55439`, generó 4/12/20/28 ideas distintas para 1/3/5/7 días. Reemplazó cinco veces la misma tarjeta de la propuesta de siete días, sin repetir IDs anteriores ni modificar las demás. Cada mutación devolvió el mismo resultado al reintentar y la recuperación de la generación devolvió la propuesta canónica con revisión cinco. Guardó dos ideas, incluida la reemplazada, y comprobó el mismo recibo al repetir el guardado. Consumió cuatro generaciones con pase y añadió dos planes exclusivamente sintéticos; no alteró la cuota gratuita. Resultado en `local-http-smoke.log`. La ausencia de consumo adicional por reemplazo también está comprobada por las pruebas PostgreSQL.

El backend local se detuvo al cerrar las pruebas. La revisión del código y los tests de navegación mantienen los límites del viaje y la identidad de los días; las guardas de cargas antiguas de Viaje se revisaron estáticamente y compilaron. No se afirma una prueba de carrera de red de `ScheduleViewModel` ni funcionamiento nativo de gestos a partir de pruebas de helpers. La [auditoría móvil](mobile-ux-audit-2026-10.md) conserva también los recorridos pendientes de la fase anterior.

## Comprobación manual pendiente

El usuario pidió continuar sin el teléfono y registrar las pruebas visuales pendientes. La compilación y las pruebas de lógica no sustituyen esta revisión nativa:

1. En «Planifica tus días», comprobar las cuatro duraciones en español/inglés, fuente al 150 % y viaje corto. Verificar selección, fechas y aviso del pase.
2. En «Tus próximos días», cambiar repetidamente una tarjeta de cualquier día. Comprobar que cambia solo esa tarjeta, conserva su selección y nunca vuelve a una alternativa anterior ni duplica otro día, incluso si la tarjeta estaba desmarcada.
3. Desconectar la red durante el reemplazo, cerrar/reabrir y reintentar con las mismas flechas. Comprobar estado pendiente y que guardar está bloqueado hasta resolverlo. Verificar el aviso junto a la tarjeta y su anuncio con TalkBack.
4. Agotar un catálogo sintético pequeño. Comprobar que la tarjeta actual se conserva y aparece el aviso de falta de alternativas. Probar cambios concurrentes de la propuesta desde dos dispositivos y cambio de cuenta/viaje.
5. En Viaje, tocar días adyacentes y deslizar a izquierda/derecha desde las tres tarjetas. Verificar extremos, días bloqueados, cambios rápidos, scroll vertical y que se muestra el hotel correcto durante una estancia de varias noches.
6. Tocar el hotel y confirmar Maps con la misma ubicación anterior. Revisar título largo, ausencia de alojamiento, datos offline y regreso a Viaje. El nombre completo debe quedar disponible para TalkBack aunque se trunque visualmente a dos líneas.

La APK de revisión usa una identidad separada y backend local. Para la revisión posterior se necesita iniciar ese backend y preparar la conexión del dispositivo; no es una APK de producción.
