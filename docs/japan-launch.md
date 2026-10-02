# Lanzamiento: Japón bien preparado

## Alcance implementado

La propuesta inicial es ayudar al viajero independiente a preparar y utilizar su viaje a Japón con confianza. Se mantiene el pase por viaje, su precio configurado, la cuota del asistente y las políticas Free ya asignadas. Las compras reales siguen desactivadas por defecto. Este cambio no publica la app, modifica infraestructura remota ni certifica tiendas o dispositivos.

- Viaje ofrece **Preparar mi viaje a Japón**, también desde su menú. La pantalla reúne días con actividades, puntos de revisión, documentos locales, descarga offline y cuatro pendientes confirmados expresamente por el viajero. Las reservas nunca se confirman automáticamente.
- Los pendientes se guardan por viaje en el servidor, con revisión para detectar cambios de otro dispositivo. Repetir el mismo estado no crea duplicados. Free puede utilizarlos sin consumir cuotas ni ampliar permisos de itinerario. Se consultan desde la última copia local sin conexión; para modificar se requiere conexión. Las revisiones y el estado offline usan las últimas versiones conocidas, no información en tiempo real.
- El contenido curado admite fecha de revisión editorial explícita y fuente HTTPS. El detalle muestra fecha y aviso; sin fecha o transcurridos 90 días avisa de revisión pendiente. Ese plazo es una regla editorial inicial, no una garantía de vigencia ni verificación de horarios. Los imports no inventan fechas de revisión.
- El envío de códigos admite Resend por HTTPS y conserva SMTP. Los fallos no dejan un código presentado como enviado y devuelven 503 cuando el proveedor falla o agota el tiempo.
- Data Protection puede guardar sus claves cifradas en PostgreSQL usando un certificado externo. Compras, sesiones y notificaciones pueden recuperarse tras reinicios si se conservan base de datos y certificado.
- El canje de PIN comparte el límite de intentos de login por PIN, filtra por destino y utiliza concurrencia optimista y una transacción que incluye activación y sesión. El perdedor de dos canjes simultáneos recibe 409 sin perder su borrador.
- Se retiran las opciones sin consumidores `ProposalsEnabled` y `RoutesEnabled`. Los endpoints retirados continúan devolviendo 410 para clientes antiguos. Una variable antigua con esos nombres no reactiva funcionalidades.

## Infraestructura gratuita ahora

### Desarrollo local

Usar `docker compose -f docker-compose.yml -f docker-compose.free.yml up --build`. El perfil añade la API, desactiva OpenAI, Google Places/Routes y compras, y conserva claves de desarrollo en un volumen. Usa el PostgreSQL local existente: no debe apuntarse a producción. Las migraciones automáticas se permiten únicamente en este entorno Development. La API espera a que el healthcheck de PostgreSQL confirme que acepta conexiones.

### Piloto remoto separado

`render.free.yaml` crea recursos con nombres nuevos. No sustituye `render.yaml`, cuya base de datos ya es de pago. El piloto conserva `ASPNETCORE_ENVIRONMENT=Production`, desactiva proveedores facturables y no activa compras. El SMTP tradicional se sustituye por HTTPS.

Antes del primer arranque se debe aplicar la migración desde el entorno de release a la base de piloto. `/health/ready` devuelve 503 si la base no es accesible o faltan migraciones conocidas por esta versión. Comprobar además los flujos mediante los smoke tests. No activar migraciones automáticas de producción para resolverlo. El certificado y las credenciales se cargan como secretos, nunca en Git.

El Blueprint cierra el acceso externo a PostgreSQL con `ipAllowList: []`. Para la primera migración y los backups desde el equipo de release, permitir temporalmente solo su IP pública `/32` en Render, usar la URL externa de la base del piloto mediante una variable de entorno, ejecutar `--migrate` según el runbook y retirar esa regla al terminar. No abrir `0.0.0.0/0`. El servicio Free no ofrece shell ni trabajos de una sola ejecución; no depender de esas funciones para preparar la base.

Render Free puede suspender la API tras inactividad; los trabajos de conciliación se reanudan al despertar. No ofrece puntualidad de notificaciones ni un checkout comercial fiable. Su PostgreSQL gratuito caduca a los 30 días y no incluye backups gestionados. Exportar y restaurar en una base separada antes de su vencimiento, o subir de plan; nunca usar la recreación periódica como almacenamiento de viajeros reales. Consultar las [limitaciones oficiales](https://render.com/docs/free).

Resend requiere cuenta, remitente/dominio verificado y credenciales. Su cuota gratuita debe comprobarse en la cuenta antes del piloto; no se asume envío ilimitado. Configuración: `TransactionalEmail__Provider=Resend`, `TransactionalEmail__ApiKey` y `TransactionalEmail__From`. El adaptador sigue la [API oficial](https://resend.com/docs/api-reference/emails/send-email). No registra cuerpos, códigos ni respuestas del proveedor.

## Claves, migración y recuperación

La migración `AddLaunchPreparationAndDurableKeys` añade `DataProtectionKeys`, `TripPreparationItems` y `Recommendations.EditorialReviewedOn`. No modifica políticas Free ni borra contenido. Aplicar primero la migración; después API compatible; finalmente clientes.

Para activar claves duraderas:

1. Generar y custodiar un certificado RSA con clave privada en formato PFX. Guardar su copia recuperable y contraseña en un almacén de secretos separado del backup de PostgreSQL.
2. Configurar `DataProtection__UseDatabase=true`, `DataProtection__CertificateBase64` con el PFX codificado y `DataProtection__CertificatePassword` con su contraseña. No usar un certificado de pruebas.
3. Antes de cambiar desde claves de archivo, exportar el anillo existente y probar su importación en la tabla con el proveedor de Data Protection en staging. La activación del nuevo almacén no importa claves antiguas automáticamente. Conservar el anillo anterior para poder volver atrás. Si ya se perdieron claves efímeras, este cambio no recupera sus recibos.
4. Para rotación, conservar los certificados antiguos mediante `DataProtection__PreviousCertificates__0`, `__1`, etc. Los PFX antiguos deben ser legibles con la contraseña configurada. No retirar certificados mientras existan claves o backups que los necesiten.
5. Probar un recibo protegido antes de reiniciar y descifrarlo después. Repetir desde una restauración de base de datos en staging, con el certificado recuperado del almacén externo.

La dependencia criptográfica se fija en `System.Security.Cryptography.Xml` 10.0.10 para los consumidores no web: corrige los avisos de las versiones anteriores que aparecieron al añadir el proveedor EF. Mantener también actualizado el runtime del host. Véase el [aviso de Microsoft](https://github.com/advisories/GHSA-23rf-6693-g89p).

Inventario de configuración sin secretos:

```powershell
dotnet run --project src/TravelCompanion.Api --no-launch-profile -- --check-launch
```

Ejecutarlo con las variables del entorno que se quiere revisar. No inicia HTTP, workers ni migraciones y no consulta proveedores. Los campos indican presencia de configuración, no éxito operativo.

## Paso progresivo a pago

| Momento | Inversión y condición |
| --- | --- |
| Preparación | Perfil local gratuito y piloto remoto descartable. Sin ventas reales. Registrar consumo y límites de cada cuenta. |
| Antes de ventas públicas | API siempre activa, PostgreSQL duradero con backups y restauración probada, claves recuperables y correo operativo. Mantener un único despliegue sencillo. |
| Primeras ventas | Alertas de pago sin pase, finalización pendiente, notificaciones agotadas, errores y latencia. Reutilizar `CommerceOperationsTelemetry`; configurar alertas externas, ya que escribir logs no avisa a un operador. Revisar cualquier pago sin acceso inmediatamente. |
| Crecimiento | Separar workers cuando el retraso o la carga de la API lo justifiquen. Ampliar PostgreSQL según conexiones, almacenamiento y consultas observadas. No crear microservicios preventivamente. |
| IA y proveedores facturables | Habilitar uno por vez, con presupuesto y límites de llamadas por viaje. Medir coste real antes de cambiar el precio del pase. Mantener las alternativas deterministas. |

Los dos Blueprints desactivan el despliegue por commit. La etapa `ReleaseCandidate` depende de Build, PostgreSQL/Android/iOS y Terraform, exige que el modelo tenga migraciones y genera `validated-release`: API publicada, SQL idempotente y manifiesto del commit/build/rama. Las pruebas publican resultados TRX. Publicar manualmente el commit de ese manifiesto después de verificar que corresponde a la rama de release; el artefacto también puede generarse para validar un PR y no autoriza su despliegue. Configurar protección de rama y permisos de despliegue en las cuentas externas; este repositorio no puede imponerlas por sí solo. Tras publicar, comprobar `/health/ready`, login, preparación, catálogo y compra sandbox. No activar compras como parte de la migración.

## Validación y piloto

Las pruebas automatizadas añadidas cubren correo HTTPS y fallos, recuperación de claves entre proveedores DI, certificado incorrecto, lista Free y aislamiento, idempotencia, conflictos, límite de PIN y doble canje PostgreSQL. La prueba `PostgresLaunchRecoveryTests` crea un esquema aleatorio con cuenta, sesión y recibo pendiente cifrado, ejecuta `pg_dump`, elimina exclusivamente ese esquema y lo restaura con `pg_restore`; después recupera la sesión y descifra el recibo usando un certificado rotado y el anterior. No equivale a una restauración remota ni a la conciliación de una compra real. En local requiere herramientas PostgreSQL compatibles en PATH; en CI usa las del contenedor de servicio mediante `TRAVELCOMPANION_TEST_POSTGRES_CONTAINER`, evitando la diferencia de versión del cliente del runner. Las pruebas PostgreSQL usan una base local descartable y esquemas aislados; nunca configurar `TRAVELCOMPANION_TEST_POSTGRES` con producción.

Antes de ventas, completar en Android e iOS Release:

- Compra sandbox, cancelación, pago pendiente, cierre de app entre pago y activación, restauración, devolución, expiración y webhook duplicado. Reiniciar API entre recepción y conciliación.
- Modo avión, descarga interrumpida, copia anterior, poco espacio, archivos inválidos, logout/login, cambio de usuario y borrado de cuenta. Los documentos y fotos personales son locales y no se restauran desde nube.
- ES/EN, texto grande, TalkBack/VoiceOver, teclado, atrás, PDF y fotos. Journal PDF continúa siendo exclusivo de Android y no se promete como beneficio común. Probar especialmente miniaturas y memoria de fotos en iOS.
- Fallos y latencia de red, arranque en frío del piloto y recuperación de base y claves. Con servicios de pago, verificar alertas desde un fallo controlado.

Observar primero entre cinco y ocho viajeros independientes preparando un día: guardar una actividad, revisar el día, explicar qué compra y descargar/consultar offline. Registrar los bloqueos sin datos sensibles. Los eventos nuevos `trip_preparation_viewed` y `trip_preparation_updated` respetan el consentimiento existente. Mantener el embudo de activación, email, compra y uso posterior; distinguir fallo técnico de abandono. No cambiar simultáneamente precio, Free y mensaje del pase. No concluir una mejora de conversión con esa muestra cualitativa.

La ampliación posterior de alternativas para lluvia o cansancio debe reutilizar el flujo de adaptación existente y pedir confirmación antes de modificar actividades. Colaboración en tiempo real, OCR, sincronización de fotos y otros destinos siguen fuera de este lanzamiento.

## Evidencia de esta implementación

- Suite Shared: 60 pruebas aprobadas. Lógica Mobile: 216 aprobadas, incluidas seis de progreso, aislamiento por viaje y descargas incompletas/interrumpidas/desactualizadas.
- API: suite final de 398 pruebas aprobadas con PostgreSQL 17 local habilitado, incluidos backup/restore real, readiness antes/después de migrar, doble canje, aislamiento, conflicto y borrado de pendientes al eliminar la cuenta. Total API + Shared + Mobile: 674 pruebas aprobadas.
- Migración aplicada por las pruebas en esquemas PostgreSQL aislados; EF confirma que no hay diferencias pendientes entre modelo y migración.
- Android Release y worker compilan. Los avisos XamlC de bindings `Source` corresponden a la configuración intencional del proyecto. Se corrigió el acceso nullable en `TopInsetCorrection`.
- `docker compose ... config --quiet` valida el perfil local. La API de Render devuelve `valid: true` para `render.free.yaml`. El Blueprint existente devuelve `need_payment_info` para su base de pago; no se modificó ni contrató infraestructura.
- Android SM-S948B conectado: instalación Release separada `com.yuku.travelcompanion.review`, API local y base descartable. Se verificaron creación de viaje, preparación, confirmación persistente tras actualizar la app, lectura de caché y bloqueo de edición con backend inaccesible, y paywall contextual de offline con ventas deshabilitadas. Capturas y logs locales en `artifacts/android-review-*`. Se amplió el ancho de las fechas para evitar que el candado ocultara la ciudad.
- Revisión visual repetida después de corregir barra superior y botón de volver; fechas bloqueadas legibles. Itinerario y preparación revisados con fuente al 130%, restaurada después al 100%. Se comprobó volver al itinerario desde preparación. Esto no sustituye una auditoría completa con TalkBack. La app habitual conserva su instalación y sus datos.
- No se ejecutaron iOS/Xcode, compras de tiendas sandbox, envío real de correo ni una restauración remota. No se publicó ni aplicó la migración remota. La prueba con backend inaccesible no certifica modo avión, poco espacio ni todos los permisos nativos.

## Revisión iterativa del plan

### Ejecución de release solicitada el 2 de octubre de 2026

- Se comprobó la configuración real: el servicio Render seguía con autodeploy activo en `main`; el YAML local por sí solo no había cambiado esa configuración. La base existente `travelcompanion-db-v2` es Free y declara vencimiento el 16 de octubre de 2026, aunque `render.yaml` propone un plan de pago. No se cambió el plan ni se aplicó el Blueprint.
- Se exportó un backup remoto y se restauró correctamente en una base PostgreSQL local aislada. El archivo está en `artifacts/release-before-launch-20261002.dump`, excluido de Git y con datos de la aplicación; debe custodiarse como backup, no compartirse como artefacto público.
- Se probó y aplicó únicamente el SQL transaccional de `20261002115937_AddLaunchPreparationAndDurableKeys`, primero sobre esa restauración y después en la base remota. El historial remoto confirma la migración. No se activaron proveedores, compras ni el nuevo almacén de claves por configuración.
- Android `1.0 (105)` instalado por actualización sobre `com.yuku.travelcompanion.app`, conservando datos y apuntando a `https://travelcompanion-api-57dw.onrender.com`. El control final del despliegue del commit y los smoke tests se registra en la entrega de esta sesión; esta actualización documental no sustituye una ejecución completa del pipeline remoto.

| Punto | Estado y evidencia | Condición restante |
| --- | --- | --- |
| Correo HTTPS conservando SMTP | Adaptador y pruebas de contrato/fallo; configuración Free | Verificar dominio y envío real en la cuenta de correo |
| Claves duraderas y recuperación | Almacén PostgreSQL cifrado, certificados anteriores, backup/restore con sesión y recibo | Custodia externa e importación del anillo anterior si existe; restauración remota |
| Conciliación y notificaciones con API suspendida | Se conserva procesamiento existente; perfil Free sin ventas y runbook de reanudación | Always-on y alertas antes de vender |
| Canje de PIN | Rate limit, transacción, concurrencia optimista, prueba PostgreSQL | Smoke de entorno desplegado |
| Configuración real | CLI sanitizada `--check-launch`, proveedores y compras apagados en piloto | Ejecutarla con las variables del entorno remoto y verificar proveedores |
| CI, migraciones y readiness | Artefacto condicionado a todas las validaciones, SQL explícito, readiness con migraciones | Ejecutar pipeline remoto y configurar permisos/protecciones de rama |
| Android/iOS | Contratos comunes; compilación/prueba visual Android; PDF de Journal limitado a Android | iOS físico, compras sandbox y matriz nativa completa |
| Primer valor y pase contextual | Se reutilizan creación de viaje y primer día existentes; acceso directo a preparación; entrada offline al pase verificada | Piloto para comprobar comprensión y conversión |
| Progreso y pendientes | Días válidos distintos, incidencias, documentos y recursos descargados; cuatro confirmaciones manuales, caché y revisiones | Ensayo con viajeros reales |
| Revisión editorial | Administración, fuente HTTPS, fecha/aviso en catálogo y actividades del itinerario | Revisión humana del contenido y asignación de fechas reales |
| Analítica y piloto | Nuevos eventos con consentimiento, embudo existente, protocolo 5–8 viajeros | Reclutar y ejecutar piloto; cohortes posteriores |
| Deuda técnica y documentación | Flags sin consumidores retirados, contratos 410 conservados, documentación histórica señalada | Revisión continua, sin migraciones destructivas |
| Infra gratuita y paso a pago | Perfiles independientes, arranque local con healthcheck, Blueprint validado, hoja de ruta | Crear/configurar piloto remoto cuando corresponda; no sustituir base de pago |
| Lluvia/cansancio y ampliaciones | Diferidos en el plan; se conserva adaptación existente | Evaluación posterior; sin OCR, colaboración en vivo, fotos cloud ni nuevos destinos |

Las filas con condiciones externas permanecen abiertas. Implementación de código, ejecución local, validación remota y resultado del piloto son evidencias distintas; ninguna se marca como realizada por estar documentada.

### Validación del pipeline

Modo: ampliación .NET de CI con empaquetado, sin despliegue automático. Validadores locales Python de la skill Azure Pipelines: sintaxis y seguridad aprobadas, cero bloqueos y dos sugerencias informativas (paralelización y plantillas). No se ejecutó yamllint ni Azure Pipelines remoto. Referencias usadas: `yaml-schema.md`, `best-practices.md` y ejemplo `dotnet-cicd.yml` de la skill generadora; referencias de plantillas omitidas porque no se añadieron plantillas. Impacto: el artefacto deja de poder generarse si falla una etapa requerida. No requiere secretos nuevos para empaquetar; publicación y protecciones requieren configuración externa.

El identificador del servicio PostgreSQL se obtiene de `AGENT_CONTAINERMAPPING`, siguiendo el [código del agente de Azure Pipelines](https://github.com/microsoft/azure-pipelines-agent/blob/master/src/Agent.Worker/ContainerOperationProvider.cs). La [imagen Ubuntu 22.04](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2204-Readme.md) documenta PostgreSQL 14 en el host; por eso el backup usa las herramientas del contenedor PostgreSQL 16 del job. Esa ruta Docker debe confirmarse en la primera ejecución remota; aquí se ejecutó la variante local con PostgreSQL 17.
