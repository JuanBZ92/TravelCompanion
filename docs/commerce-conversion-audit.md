# Auditoría de cierre: compra, conversión, Assistant y rutas

Revisión del 20 de septiembre de 2026. Cada bloque separa tres estados: **implementado** significa que existe código y migración; **validado automáticamente** requiere una prueba o compilación ejecutada; **validado externamente** requiere tiendas, dispositivos o servicios reales.

| Punto del plan | Implementado | Validado automáticamente | Validado externamente | Observación |
|---|---:|---:|---:|---|
| Cuenta recuperable, OTP y prueba anónima | Sí | Parcial | No | La suite en memoria pasa. La prueba PostgreSQL de consumo único está creada y es obligatoria en CI, pero no pudo ejecutarse localmente por credenciales del PostgreSQL instalado. |
| Pase resuelto por viaje y permisos centralizados | Sí | Sí | No | Las ediciones, propuestas, rutas y compra consultan el viaje seleccionado; las pruebas de cuenta y comercio pasan. |
| Bloqueo, revisión y reintento de transacciones | Sí | Parcial | No | Las escrituras críticas usan bloqueo por viaje, revisión y la estrategia de ejecución de EF. El pipeline ejecutará concurrencia con PostgreSQL real. |
| StoreKit 2 | Sí | No | No | Existe un framework Swift StoreKit 2 enlazado al proyecto MAUI: precio localizado, compra con `appAccountToken`, JWS, restauración y `finish` posterior a la concesión. Falta compilarlo en macOS y validarlo en sandbox/TestFlight. |
| Google Play Billing | Sí | Compilación Android | No | Billing v8 consulta producto/precio, compra, pendiente, cancelación y restauración. La concesión se registra antes del consumo en backend. Falta pista interna y cuenta de licencia. |
| Verificación Apple/Google y concesión atómica | Sí | Parcial | No | Apple consulta App Store Server API tras verificar JWS; Google consulta Developer API; transacción, pase y eventos de activación se confirman juntos. Faltan pruebas reales de los proveedores. |
| Notificaciones, conciliación y revocación temprana | Sí | Compilación | No | Los payloads se autentican, cifran y persisten antes de procesarse; un worker reintenta; los marcadores de revocación impiden una activación posterior. |
| Recuperación tras suspensión y cambio de dispositivo | Sí | Compilación Android | No | Al activar la app se recupera primero el pase de la cuenta y luego la evidencia de la tienda; el pendiente permanece hasta un estado definitivo. |
| Vigencia anual, cambio de fechas y borradores | Sí | Sí | No | Se calcula en la zona del viaje, se rechazan fechas fuera de cobertura y la instantánea v2 conserva rutas y vínculos. |
| Consentimiento y analítica de producto | Sí | Sí | No | El consentimiento persistido también rige eventos de comportamiento del servidor; los hechos de compra son obligatorios y se deduplican. |
| Embudo y Admin | Sí | Parcial | No | Se corrigieron usuarios anónimos, orden de etapas, cohortes, mediana, sandbox y reembolsos. Admin incluye una cola de soporte para dobles compras. Google consulta `orders.get` para obtener total y moneda; si el permiso o dato no está disponible, conserva el estado “importe pendiente”. |
| Paywall común y acción bloqueada | Sí | Compilación Android | No | Requiere email verificado y precio localizado. Muestra el vencimiento previsto de la compra y el reinicio UTC de la cuota convertido a hora local. Map y Assistant conservan la recomendación, fecha y hora, y retoman el editor tras pagar. |
| Experimento estable de mensajes | Sí | Sí | No | La asignación se conserva al vincular la cuenta y la intención guarda entrada y variante; no se declara ganador automáticamente. |
| Flexibilidad y revisión del día | Sí | Sí | No | Se separan flexible, fijada y confirmada; se cubren intersecciones reales, medianoche e información incompleta. |
| Motor determinista y objetivos | Sí | Sí | No | Propuestas y rutas comparten intervalos fechados, reservas protegidas y objetivos distintos. Narrativas, explicaciones y advertencias persistidas se generan en español o inglés según la cultura de la solicitud. |
| Aplicar, sincronizar y deshacer | Sí | Sí | No | Aplicación conjunta, idempotencia, revisión, registro anterior, vínculos de rutas y trabajo durable de sincronización están implementados. |
| Cuota compartida del Assistant | Sí | Parcial | No | Reserva/confirmación es atómica e idempotente. La prueba concurrente PostgreSQL está creada y obligatoria en CI, pendiente de una ejecución con conexión válida. |
| Rutas personales y plantillas editoriales | Sí | Sí | No | Fecha, ventana, ritmo, acceso, procedencia, aplicaciones independientes y copia de plantilla están persistidos. |
| Borrar ruta y actividades | Sí | Sí | No | El usuario elige conservar actividades o quitar solo las flexibles; reservas confirmadas y horarios fijados permanecen. |
| Offline, accesibilidad y español/inglés | Sí | Compilación Android | No | Rutas y propuestas guardadas se leen offline, las vistas principales tienen semántica y el contenido persistido del planificador se localiza. Faltan pruebas reales con lector de pantalla y texto grande. |
| Migraciones y flags | Sí | Sí | No | EF no detecta cambios pendientes. Las migraciones son incrementales y desactivar compras nuevas no detiene conciliación. |
| CI PostgreSQL, Android e iOS | Sí | YAML validado | No | Azure Pipelines contiene PostgreSQL obligatorio y builds Android/iOS. El validador local pasa sintaxis y seguridad; el pipeline aún no se ha ejecutado. |
| Alertas operativas | Sí | Compilación | No | Un worker publica métricas de pagos sin pase, notificaciones agotadas, consumos pendientes, fallos de sincronización y dobles compras. Falta configurar umbrales y destinatarios en el entorno de observabilidad. |

## Validación ejecutada

- `TravelCompanion.Shared.Tests`: 18/18.
- `TravelCompanion.Api.Tests`: 256 aprobadas y 4 pruebas PostgreSQL omitidas sin `TRAVELCOMPANION_TEST_POSTGRES`; la suite específica de comercio queda en 13/13.
- `TravelCompanion.Mobile.Tests`: 22/22.
- API: compilación limpia, sin advertencias.
- Android: compilación `net10.0-android` limpia.
- EF Core: no hay cambios de modelo sin migración.
- Azure Pipelines: sintaxis y seguridad aprobadas; dos sugerencias informativas sobre paralelismo/plantillas.

## Puntos internos todavía pendientes

- Ejecutar y corregir, si aparece algún fallo, los cuatro escenarios relacionales del nuevo job PostgreSQL. El equipo local tiene PostgreSQL en el puerto 5433, pero no hay una conexión de prueba configurada y Docker Desktop no está iniciado.
- Compilar el bridge StoreKit con el job macOS. El código Swift y el proyecto Xcode están incluidos, pero Windows no puede validar el enlazado nativo.

## Checklist externo

### Apple

- [ ] Producto non-renewing, mercados, precio y textos configurados en App Store Connect.
- [ ] Issuer ID, Key ID, clave privada y certificados raíz montados como secretos.
- [ ] App Store Server Notifications V2 sandbox/producción apuntando al backend HTTPS.
- [ ] Compilación del bridge con Xcode y prueba en dispositivo/TestFlight.
- [ ] Compra, Ask to Buy pendiente, cierre de app, restauración, reembolso y revocación comprobados.

### Google

- [ ] Producto repetible, precio y mercados configurados en Play Console.
- [ ] Cuenta de servicio con consulta y consumo de compras y acceso a pedidos.
- [ ] Pub/Sub y RTDN con audiencia y cuenta emisora correctas.
- [ ] Firma, pista interna, cuenta de licencia y dispositivo físico.
- [ ] Compra, pendiente, cierre de app, restauración, consumo y void/refund comprobados.

### Backend y operación

- [ ] Staging HTTPS accesible, PostgreSQL con backup y Data Protection persistente.
- [ ] SMTP TLS y remitente verificado.
- [ ] Secretos de tiendas, IA y rutas cargados fuera del repositorio.
- [ ] Pipeline Azure ejecutado con PostgreSQL, Android y agente macOS.
- [ ] Umbrales, destino y responsables configurados para las métricas `commerce.payment_without_pass.current`, `commerce.store_notification_exhausted.current`, `commerce.store_finalization_pending.current`, `commerce.trip_sync_repeated_failure.current` y `commerce.duplicate_purchase_review.current`.
- [ ] Procedimiento de soporte para doble compra y reembolso.
- [ ] Catálogo/plantillas reales, privacidad y textos comerciales revisados.

El cierre comercial requiere evidencia sandbox de ambos proveedores. Compilar localmente o aprobar pruebas en memoria no sustituye esa aceptación.
