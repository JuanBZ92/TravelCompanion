# Compra, conversión y planificación avanzada

> Actualización septiembre de 2026: consultar el [plan de valor y conversión](launch-value-cycle.md). Las propuestas y rutas temáticas descritas más abajo son históricas: sus endpoints retirados conservan respuesta 410 y esta implementación no los reactiva. La nueva revisión de viaje usa el editor y la mejora de día existentes. El Free persistente tiene cuotas independientes para chat y mejoras de día.

## Configuración

Las migraciones `AddCommerceConversionProposalsAndRoutes`, `AddPurchaseReconciliationAndRouteLinks` y `AddProductExperimentAssignments` añaden identidad verificada, intenciones y transacciones de tienda, notificaciones, analítica, cuotas, propuestas, operaciones de deshacer, rutas, evidencia cifrada para conciliación y asignaciones estables de experimentos.

Configurar secretos fuera de `appsettings.json`:

- `EmailVerification__HashSecret`
- `Smtp__Enabled`, `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password` y `Smtp__FromAddress`
- `StorePurchases__GoogleServiceAccountJson`
- `StorePurchases__GoogleRtdnAudience` y `StorePurchases__GoogleRtdnServiceAccountEmail`
- `StorePurchases__AppleRootCertificatesPath`, directorio con las autoridades raíz de Apple obtenidas de Apple PKI
- `DataProtection__KeysPath`, apuntando a almacenamiento persistente compartido por todas las instancias de la API

Los identificadores de producto viven en `StorePurchases__AppleProductId` y `StorePurchases__GoogleProductId`. `StorePurchases__NewPurchasesEnabled` detiene checkouts nuevos sin detener restauraciones ni notificaciones. `ProductFeatures` permite desactivar analítica, paywall, propuestas y rutas por separado.

## Contratos HTTP

- Cuenta: `POST /api/mobile/account/email/code`, `POST /api/mobile/account/email/verify`, `GET /api/mobile/account`, `POST /api/mobile/account/select-trip`, `POST /api/mobile/account/trips/{tripId}/archive`, `DELETE /api/mobile/account`.
- Compra: `POST /api/mobile/purchases/intents`, `GET /api/mobile/purchases/intents/{id}`, `POST /api/mobile/purchases/intents/{id}/cancel`, `POST /api/mobile/purchases/verify` y `POST /api/mobile/purchases/restore`.
- Proveedores: `POST /api/providers/store-notifications/apple` y `POST /api/providers/store-notifications/google`.
- Conversión: `GET /api/mobile/conversion/paywall/{tripId}` y `POST /api/mobile/conversion/events`.
- Propuestas: `POST /api/mobile/proposals`, `GET/PATCH /api/mobile/proposals/{id}`, `POST /api/mobile/proposals/{id}/apply` y `POST /api/mobile/proposals/operations/{id}/undo`.
- Rutas: `GET/POST /api/mobile/thematic-routes`, `PUT/DELETE /api/mobile/thematic-routes/{id}`, `POST /api/mobile/thematic-routes/{id}/copy` y `POST /api/mobile/thematic-routes/{id}/prepare-application`.

Todos los identificadores de usuario y viaje se validan contra la sesión. Una propuesta guarda la revisión en la que se calculó, caduca en 24 horas y se aplica con una sola revisión. Deshacer exige que no exista ninguna edición posterior.

## Estados y cuotas

El acceso distingue prueba, compra pendiente, activo, vencido y revocado. La prueba conserva tres resultados útiles totales. Un pase comprado conserva 30 resultados útiles por día UTC. El backend reserva cuota durante una operación y solo la confirma cuando existe un resultado útil.

Al vencer o revocarse un pase comprado, la sesión pasa a `BuilderReadOnly`: el viajero conserva la agenda, las rutas guardadas y las exportaciones, pero no puede editar, generar propuestas, consultar el catálogo premium ni usar el Assistant. Volver a seleccionar el viaje desde la cuenta aplica el mismo criterio.

Una hora exacta no implica reserva. `ItineraryFlexibility` separa actividades flexibles, horarios fijados por el viajero y reservas confirmadas. Vuelos, hospedajes, contenido curado, horarios fijados y reservas confirmadas están protegidos.

## Tiendas

El backend verifica JWS firmados por Apple contra un almacén de confianza limitado a las raíces configuradas y valida tokens con Google Android Publisher antes de activar un pase. También exige que `appAccountToken` u `obfuscatedExternalAccountId` coincida con la intención de compra. Una transacción es única por proveedor, entorno e identificador. Apple Server Notifications y Google RTDN se autentican, deduplican y pueden revocar el grant asociado.

Las compras pendientes y el consumo de Google se reintentan desde una cola persistente. El token se cifra con ASP.NET Core Data Protection, se concede el pase dentro de la transacción y solo después se consume la compra. Un segundo cobro para el mismo viaje queda registrado para revisión y reembolso sin sustituir el pase original.

La app conserva en `SecureStorage` la intención y la evidencia de la compra hasta que la API activa el pase. Al volver de una suspensión intenta continuar la verificación y, si todavía no recibió evidencia, consulta las transacciones restaurables de la tienda y las relaciona mediante el identificador opaco de cuenta. Este identificador es un UUID válido para `appAccountToken` de StoreKit y también se usa como identificador ofuscado de Google Play.

La app móvil contiene el contrato `IStorePurchaseService` y adaptadores por plataforma. Los adaptadores permanecen cerrados hasta configurar los productos y el puente StoreKit 2 / Play Billing firmado; nunca generan una compra exitosa simulada en producción. Windows y Mac Catalyst conservan restauración por cuenta y PIN de soporte.

## Operación

El Admin expone `/admin/conversion` para el embudo y `/admin/thematicroutes` para plantillas editoriales. Los ingresos se agrupan por moneda y excluyen sandbox. Las cohortes abiertas se marcan como incompletas.

La analítica de interfaz se mantiene en una cola móvil cifrada de hasta 200 eventos y solo se envía cuando el viajero activa el consentimiento desde “Mis viajes”. Los hechos transaccionales del servidor siguen registrándose sin depender de ese consentimiento. La variante del primer experimento se guarda en `ProductExperimentAssignments` y se conserva al vincular el borrador anónimo con el email verificado.

Los eventos de comportamiento se conservan durante 90 días. Antes de eliminar cada lote, el worker de retención incrementa agregados diarios por evento, origen, plataforma, versión y variante sin conservar identificadores de usuario.

La eliminación de cuenta revoca las sesiones y pases, borra itinerarios, rutas, propuestas, preferencias y eventos de comportamiento, y anonimiza los datos transaccionales que deben conservarse para conciliación y reembolsos de las tiendas.

Antes de habilitar compras se deben ejecutar compras reales de sandbox/TestFlight y pista interna de Google Play, comprobar cierre y reanudación de la app, y verificar las URL de notificación. La compilación local valida contratos y lógica, pero no sustituye esas pruebas de tienda.
