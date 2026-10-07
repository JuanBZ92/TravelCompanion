# Travel Companion - Documentacion funcional

## Estado vigente del lanzamiento — octubre de 2026

Consultar [Japón bien preparado](japan-launch.md) para la pantalla de preparación, revisión editorial, alcance del pase y validación pendiente. El cliente ya dispone de compras nativas y recuperación, pero las ventas reales siguen desactivadas por defecto. El alta sin PIN y las políticas Free vigentes se describen en el plan incremental. Las referencias posteriores al login exclusivamente por contraseña o a ausencia de checkout son antecedentes del MVP, no limitaciones del producto actual.

## Actualización de conversión y valor del pase — septiembre de 2026

El [plan incremental de valor y conversión](launch-value-cycle.md) describe el comportamiento implementado, su activación y límites. Incluye creación sin PIN, Free persistente para nuevas cuentas compatibles (desactivado por defecto), paywall contextual, revisión de todos los días, preparación offline y documentos personales locales. Preparación, Journal y adjuntos personales están disponibles también con un viaje gratuito activo. Ante descripciones históricas de pruebas temporales, rutas o almacenamiento, prevalece ese alcance.

Este documento describe que producto estamos construyendo, que funcionalidades existen y que decisiones funcionales estan vigentes. Debe mantenerse actualizado cada vez que se agregue o cambie comportamiento visible para usuarios, admins o clientes.

## Vision

Travel Companion es una app movil companion de viajes para vender contenido curado por destino y acompanar al viajero durante su viaje.

La idea principal es ofrecer paquetes especificos, por ejemplo Japon, que pueden estar detras de:

- contenido gratuito;
- paquetes pagos;
- suscripcion por destino/pais;
- contenido solo administrable.

El usuario final deberia poder descubrir recomendaciones, verlas por cercania, guardar favoritos, abrir ubicaciones en mapas y consultar un schedule si contrato el viaje o tiene reservas gestionadas.

## Usuarios

### Viajero

Persona que usa la app para preparar o vivir el viaje.

Necesita:

- consultar recomendaciones confiables;
- identificar que contenido tiene incluido;
- guardar favoritos;
- abrir lugares en mapas;
- ver reservas y horarios;
- pedir soporte.

### Admin

Persona que carga y mantiene el contenido.

Necesita:

- entrar con login;
- crear y editar recomendaciones;
- crear y editar reservas del schedule;
- definir si el contenido es gratis, interno o asociado a paquetes pagos.

## Destinos y paquetes

El destino demo actual es Japon.

Paquetes demo:

- `Japon Essentials`: paquete de pago fijo con recomendaciones, mapa y tips practicos.
- `Japon Premium Pack`: paquete pago con recomendaciones premium.

La suscripcion no es un paquete: se asigna al destino Japon y desbloquea todos sus paquetes.

La estructura busca permitir mas destinos en el futuro sin cambiar la base conceptual del producto.

Modelo funcional vigente:

- un destino representa el pais o region vendible;
- cada destino puede tener muchos paquetes reutilizables;
- las recomendaciones gratis pueden quedar sueltas como `Free`;
- las recomendaciones exclusivas para suscriptores pueden quedar sueltas como `Subscription`;
- las recomendaciones pagas pueden asociarse a uno o mas paquetes;
- los usuarios se asignan a paquetes desde el CMS;
- el schedule pertenece al viaje gestionado para un usuario y no depende de paquetes ni niveles free/paid/subscription.

## Niveles de acceso

Los niveles funcionales actuales son:

- `Free`: contenido gratuito.
- `Paid`: acceso a un paquete puntual comprado/asignado.
- `Subscription`: acceso activo a todos los paquetes de un destino/pais.
- `AdminOnly`: contenido interno, no publico.

Comportamiento actual en la app:

- Las recomendaciones muestran su tipo de acceso o paquetes asociados.
- La app consulta los accesos del usuario logueado.
- Las recomendaciones aparecen como incluidas o bloqueadas segun el acceso.
- Las cuentas de prueba separan escenarios: usuario free solo desbloquea contenido gratis, usuario subscription desbloquea todos los paquetes de Japon, usuario paid desbloquea solo el paquete fijo asignado.
- Los paquetes son productos reutilizables por destino: asignar un paquete puntual concede `Paid`; asignar una suscripcion al destino concede `Subscription` y desbloquea todos los paquetes de ese destino.
- El usuario demo tiene acceso a Japon Essentials y suscripcion de Japon.

## App mobile

La navegación actual tiene Viaje, Mapa, Asistente, Journal y Cuenta. Documentos y la carpeta de preparación se abren desde el viaje o la cuenta; el pase aparece en contexto. Las tabs antiguas Ideas y Packs ya no forman parte de la app. Las pantallas comparten papel cálido, títulos serif legibles, acciones claras e iconos accesibles con áreas táctiles de al menos 48 dp.

### Acceso y cuenta

El viajero puede crear su viaje sin PIN, entrar con el PIN de un viaje o recuperar su cuenta con un código por correo. Se conservan los endpoints de email/contraseña para compatibilidad y administración. Una contraseña temporal exige cambio al primer acceso. Las sesiones usan tokens opacos; la app no concede acceso a contenido de pago por una declaración del cliente.

Cuenta permite verificar el correo, cambiar o archivar viajes, gestionar consentimiento de analítica, consultar documentos y el pase, compartir diagnóstico, cerrar sesión y eliminar la cuenta con confirmación. También permite elegir el desbloqueo mediante biometría o PIN/código por correo, guardando la elección por cuenta en este dispositivo. Elegir PIN conserva la verificación de acceso y requiere conexión. La biometría funciona con una sesión local guardada y mantiene el acceso sin conexión. El comportamiento previo de cuentas gratuitas sin una elección explícita se conserva.

### Viaje y planificación

Viaje reúne la agenda, la carpeta de preparación y los gastos. Permite elegir día, distinguir eventos/vuelos/alojamientos y filtrar por ciudad. La búsqueda encuentra actividades por título, lugar, ciudad y tipo en la copia local, y permite saltar al día o abrir el detalle. El resumen de mañana presenta primera reserva y hotel cuando mañana pertenece al viaje.

Un día con planes flexibles sigue ofreciendo alternativas aunque ya no queden reservas futuras. Las reservas con horario, vuelos y alojamientos conservan sus datos y acciones de mapas. La app muestra estados distintos cuando carga, no hay contenido, falla la actualización o trabaja con una copia sin conexión; el reintento conserva lo ya disponible.

La planificación ofrece añadir nuevas alternativas por momento del día o reorganizar planes elegidos según preferencias. También admite preparar varios días, por ejemplo 3, 5 o 7, con selección de fechas y comparación de la agenda actual con la propuesta antes de aplicar. Las reservas fijas se conservan. Los permisos y cuotas de la cuenta siguen vigentes; no se usa la cantidad de planes existentes como garantía de que un día esté completo. Los avisos de agenda ayudan a revisar solapamientos o exceso de opciones.

### Mapa y Asistente

Mapa muestra recomendaciones según accesos y zonas gratuitas configuradas, con filtros, favoritos y detalle de lugar. El permiso para contenido completo, cálculo de rutas y recomendaciones del asistente depende de la cuenta. Los paquetes y niveles de acceso siguen siendo parte del catálogo aunque ya no tengan una tab propia.

Asistente diferencia preguntar y preparar planes. Sus acciones guiadas permiten definir fechas, tiempo disponible y preferencias; un resultado indica qué propone y solicita revisión antes de modificar la agenda. Un error conserva el mensaje y ofrece reintento. La app muestra claramente el estado de la operación y permite cancelar sin aplicar resultados posteriores. La IA y las decisiones de autorización se ejecutan en el backend.

### Preparación y Documentos

Preparar el viaje organiza Transporte, Alojamiento, Reservas y actividades y Documentos de viaje. En Carpeta, tocar una categoría abre sus documentos; su botón de añadir abre directamente el selector con esa categoría elegida. También permite indicar «Ya lo tengo fuera de la app», «No lo necesito» o «Volver a pendiente». El resumen indica categorías organizadas y no certifica requisitos migratorios.

Los adjuntos personales están disponibles también para usuarios gratuitos con sesión y viaje activos. PDF, JPEG y PNG se validan por contenido, con un máximo de 20 MB por archivo. Documentos permite añadir, abrir, renombrar, eliminar y mover categorías; los archivos anteriores sin categoría aparecen en Otros. Los documentos incluidos en el pase mantienen permisos separados.

Los archivos se guardan solo en este dispositivo, cifrados y separados por cuenta/viaje. Hay que conservar el original: no se recuperan al reinstalar. Las decisiones manuales funcionan sin conexión. La importación de checks antiguos ocurre una sola vez y no bloquea las acciones locales ni sobrescribe decisiones nuevas. Cancelar el selector o cambiar cuenta/viaje descarta la operación.

Documentos distingue una sección vacía de un fallo de actualización. Conserva los archivos y el contenido ya cargado, explica la desconexión y ofrece reintento. La revisión del itinerario y la descarga offline son acciones secundarias con sus permisos actuales.

### Journal

Mi diario reúne recuerdos libres y recuerdos de actividades en orden cronológico, agrupados por fecha. No hace falta tener actividades para escribir. Las tiles muestran fecha, título o lugar y portada; el detalle presenta el texto completo y acciones visibles de editar, añadir fotos y consultar actividad.

El editor admite fecha, título y lugar opcionales, hasta 2.000 caracteres y diez fotos, incluidos recuerdos solo con fotos. Permite buscar una actividad del día para usar su lugar sin abandonar el editor. Un borrador se guarda localmente tras una pausa y al salir; se puede continuar o descartar con confirmación. Guardar recuerdo confirma la entrada y solicita sincronización, con estados de guardando, pendiente y conflicto que conservan el contenido ante errores.

Las entradas confirmadas sincronizan su texto; fotos, portadas y borradores permanecen en este dispositivo. Un fallo o archivo ausente no impide leer el resto del recuerdo. Borrar una actividad conserva su recuerdo. El álbum PDF está disponible en Android con selección de fechas y recuerdos confirmados; los borradores quedan excluidos.

Si otro dispositivo eliminó un recuerdo que conserva escritura o fotos locales pendientes, aparece un conflicto y el contenido local permanece disponible hasta resolverlo. La app ofrece «Aceptar eliminación» o «Cancelar» y explica que se puede conservar una copia antes de aceptar. Aceptar también elimina esa copia del dispositivo; cancelar mantiene el conflicto y el contenido. El registro eliminado no se resucita durante los reintentos.

### Gastos

Con viaje activo, el registro básico de gastos permite añadir, editar y eliminar importes con categoría, fecha, moneda y actividad opcional, sin modificar el itinerario. Los cambios locales se conservan cuando falla la red. Las cotizaciones y conflictos se muestran sin convertir un importe pendiente en cero confirmado.

El pase habilita desglose por categoría/día y exportación según los permisos vigentes. El desglose puede derivarse de la copia local autorizada sin conexión. Los cálculos usan la moneda configurada y señalan cotizaciones ausentes o de otra moneda. Cambiar de cuenta/viaje o de configuración durante el editor protege contra guardar datos en otro contexto.

### Gratis, pase y funcionamiento sin conexión

Las cuentas gratuitas pueden organizar documentos personales, Preparación, Journal y gastos básicos con un viaje activo. La política de creación y edición puede ser Free persistente o prueba temporal según configuración; las cuotas del asistente y la planificación se resuelven en el servidor.

El pase presenta beneficios exclusivos y funciones gratuitas por separado. Su cabecera explica el beneficio del contexto que lo abrió; los detalles de acceso y conservación se pueden desplegar. Las compras nativas y restauración requieren verificación del servidor y su disponibilidad depende de la configuración de lanzamiento.

La app muestra primero la copia local disponible de agenda y catálogo. Las notas confirmadas, gastos y mutaciones de itinerario sincronizan al recuperar conexión cuando el usuario dispone del permiso correspondiente. No se prometen mapas descargados ni copia en la nube para archivos personales, fotos o borradores. Cada cuenta mantiene su contenido aislado.

## Admin CMS

El admin actual permite operar contenido basico sin tocar la base de datos manualmente.

Funciones existentes:

- login de admin;
- dashboard y navegación agrupada en Catálogo, Viajes y Negocio;
- formularios con validación inline, foco en errores y entradas inválidas resaltadas; los borradores fallidos conservan el contenido y la revisión original; las confirmaciones de publicación y descarte se mantienen;
- crear, editar y borrar destinos sin contenido asociado;
- crear, editar y borrar paquetes reutilizables sin accesos asociados;
- seleccionar un paquete y asignarle muchos usuarios sin crear paquetes por persona;
- ocultar del selector de asignacion a usuarios que ya tienen acceso activo al paquete;
- CRUD de recomendaciones con acceso `Free`, `Suscripcion` o `Paquete`;
- asociacion de recomendaciones a uno o mas paquetes cuando el acceso elegido es `Paquete`;
- crear, editar y borrar viajes por usuario/destino; lista paginada de 50, búsqueda, filtros de estado y conteos;
- CRUD de reservas dentro de cada viaje;
- mostrar solo los campos relevantes para evento, vuelo u hospedaje al cargar reservas;
- tipo de reserva: evento, vuelo u hospedaje;
- campos especificos para vuelos (aerolinea, vuelo, origen/destino, aeropuertos);
- campos especificos para hospedajes (check-in/check-out, direccion y alojamiento);
- ciudad obligatoria por reserva para organizar viajes multi-ciudad;
- salto directo desde un viaje hacia sus reservas filtradas;
- crear y editar usuarios;
- borrar usuarios;
- generar password temporal al crear usuario;
- avisar visualmente cuando un usuario tiene cambio de password pendiente;
- resetear password temporal de usuarios existentes;
- asignar accesos a usuarios;
- activar paquetes para usuarios desde la pantalla de paquetes sin duplicar el paquete;
- quitar accesos asignados;
- seleccion de nivel de acceso para recomendaciones;

## Autenticacion y acceso

Estado actual:

- Admin tiene login por cookie.
- API expone acceso por PIN y recuperación con código por correo; mantiene email/contraseña para compatibilidad.
- API emite tokens opacos y guarda solo hash del token.
- Los errores de validacion de login/cambio de password y paginacion se devuelven en formato consistente (`ValidationProblemDetails`) para que la app pueda mostrar mensajes de forma uniforme.
- Mobile guarda la sesion local y usa token bearer para refrescar datos de viaje.
- Mobile consulta catálogo y recomendaciones autorizadas mediante contratos autenticados.
- Mobile usa un bootstrap autenticado compartido para Mapa y Viaje, con accesos, agenda y paquetes del destino activo.
- Mobile guarda copias offline del discover reducido y del bootstrap completo por usuario.
- Mobile puede desbloquear una sesion local con biometria del dispositivo.
- La preferencia de desbloqueo por cuenta conserva la verificación incluso al elegir PIN.
- Cerrar sesión revoca y borra credenciales; después exige un nuevo acceso válido.
- Admin puede asignar entitlements a usuarios desde el CMS.
- Admin puede activar un paquete a un usuario desde `/admin/packages`; el acceso queda asociado al paquete y destino.
- La password temporal obliga cambio en primer ingreso.
- Las passwords temporales no se escriben en logs; en desarrollo solo se muestran en el CMS para facilitar pruebas locales.
- Compras y restauración nativas están integradas con verificación del backend; las ventas reales dependen de la configuración de lanzamiento.

El modelo de entitlements ya prepara la app para compras, paquetes o suscripciones reales.

## Datos demo

Contenido demo actual:

- Destino: Japon.
- Recomendaciones: mas de 35 recomendaciones repartidas entre Tokyo, Kyoto, Osaka, Hiroshima, Miyajima, Sapporo y excursiones, con niveles `Free`, `Paid` y `Subscription` para probar scroll, filtros, mapa y paginacion.
- Recomendaciones base:
  - Tsukiji Outer Market: gratis.
  - Fushimi Inari Taisha: pago fijo.
  - Dotonbori: suscripcion.
- Schedule demo:
  - TeamLab Borderless.
  - Cena omakase.
- Usuario demo:
  - `demo@travelcompanion.local`
  - password temporal `TravelDemo!2026`;
  - acceso a Japon Essentials;
  - suscripcion activa a Japon.
  - viaje demo de Japon asignado.
- Usuarios de prueba:
  - `usuariofree@travelcompanion.local` / `PasswordFree`: viaje de 2 semanas por Tokyo, Osaka y Kyoto; solo contenido gratis incluido.
  - `usuariosub@travelcompanion.local` / `PasswordSub`: viaje de mas de 2 semanas por Tokyo, Kyoto, Osaka y Nara; contenido gratis y todos los paquetes de Japon incluidos por suscripcion.
  - `usuariopaid@travelcompanion.local` / `PasswordPAid`: viaje de 3 semanas por Tokyo, Osaka, Kobe, Hiroshima, Miyajima, Sapporo y Otaru; contenido gratis y el paquete Japon Essentials incluido.

## Revisión inteligente del día

La agenda muestra una tarjeta **Revisar mi día** para la fecha seleccionada. Detecta solapamientos confirmados, traslados con poco margen y jornadas con demasiados planes horarios. Cuando el día está equilibrado también lo confirma, para que la función aporte tranquilidad además de alertas.

Desde cada alerta se puede abrir el plan afectado. Los elementos creados por el viajero se abren en el editor; las reservas curadas se abren en modo detalle. La revisión es determinista, funciona con la agenda guardada sin conexión y se actualiza tras cada alta, edición o borrado.

La tarjeta también permite pedir al Assistant una alternativa para la fecha seleccionada. La solicitud lleva la fecha y la ciudad al backend, conserva las reservas confirmadas y ofrece alternativas por momentos del día. Guardar una sugerencia sigue abriendo el editor antes de modificar el itinerario.

Preparar planes diferencia añadir alternativas y reorganizar los planes seleccionados. El viajero elige fechas, ritmo, intereses y presupuesto, revisa criterios y compara la propuesta con la agenda antes de aplicarla. Las preferencias generales solo se actualizan si lo solicita. Las cuotas de Free se comprueban en el servidor y el pase conserva los criterios para continuar tras la activación.

El menú del itinerario permite actualizar una copia offline de la agenda, recomendaciones y contenido de Today para la fecha seleccionada. También permite compartir una versión de texto ordenada por día; se omiten códigos de confirmación y notas privadas.

Desde el mismo menú se puede crear una ruta temática de comida local, historia, arte, naturaleza o compras. La fecha y ciudad seleccionadas se pasan al Assistant, que propone varias paradas cercanas sin mover reservas. Cada parada se puede revisar, cambiar por otra y guardar por separado mediante el editor del Builder.

## Evolución del producto

Las prioridades y condiciones de publicación se mantienen en [japan-launch](japan-launch.md) y [launch-value-cycle](launch-value-cycle.md). Creación de viaje, recuperación por correo, compras verificadas, permisos de API y contenido enriquecido ya forman parte de la app; su activación se rige por la configuración y las validaciones del entorno.

## Operacion e infraestructura

El repositorio incluye infraestructura Azure con Terraform y procedimientos Render. El entorno publicado se opera mediante su configuración y runbook; las revisiones usan backend y PostgreSQL locales aislados.

Objetivo funcional:

- poder publicar API/Admin en un ambiente cloud real;
- separar datos locales de datos dev/staging/prod;
- proteger credenciales fuera del codigo;
- tener base PostgreSQL administrada;
- preparar almacenamiento de imagenes y media;
- observar errores y comportamiento de la API.

Ambientes esperados:

- `local`: Docker Compose, API local y app en emulador/celular.
- `dev`: backend de revisión separado de producción para pruebas desde dispositivos reales.
- `staging`: validacion previa a produccion.
- `prod`: datos reales, backups, monitoreo y dominios reales.

## Journal, propuestas y navegación Android

- Journal organiza recuerdos por fecha y ciudad, con notas editables, portada, galería y accesos al lugar. Permite agregar recuerdos a cualquier actividad consultable, incluso reservas protegidas y cuentas Free sin edición de itinerario.
- Las notas tienen revisión propia y se sincronizan con el backend. Sin conexión quedan pendientes; los conflictos conservan ambas versiones hasta elegir. Se importan las notas personales existentes, sin textos editoriales ni placeholders. Borrar una actividad conserva el recuerdo; borrar viaje o cuenta elimina sus notas.
- Las fotos se copian al almacenamiento privado del dispositivo, separadas por usuario y viaje: hasta 10 por recuerdo, máximo 2048 px y miniaturas, orientación normalizada y sin metadatos GPS. No hay nube ni cámara; cerrar sesión no borra fotos. Se puede elegir portada o quitar una copia sin tocar el original.
- En Android, el álbum PDF incluye todo el viaje o días seleccionados, título y portada elegibles, notas completas y fotos. Ofrece vista previa, destino del sistema y compartir, con progreso y cancelación. No incluye códigos de reserva ni adjuntos. Se avisa si falta una foto.
- La navegación principal de viajes builder tiene cinco destinos: Viaje, Mapa, Asistente, Journal y Cuenta. Los viajes sin Asistente mantienen sus restricciones. Documentos se abre desde Viaje o Cuenta; Pase Japón y Salir están en Cuenta. Journal requiere un viaje seleccionado.
- En el detalle de una idea nueva de «Añadir alternativas», el viajero puede elegir Mañana, Mediodía, Tarde o Noche antes de guardar. No modifica automáticamente reservas existentes ni fija una hora exacta.
- Las tarjetas de propuestas permiten guardar el plan del día sin abrir el detalle y pedir otra opción mediante iconos. El detalle conserva la elección de franja y usa las mismas acciones con nombres accesibles y estados de carga.
- Añadir alternativas conserva los planes flexibles y reservas existentes y ofrece opciones nuevas para los momentos del día. El viajero revisa las propuestas y los avisos de la agenda antes de decidir qué usar.
- Atrás cierra primero el detalle o estado abierto, luego recorre páginas y pestañas visitadas. En la raíz, Android muestra «Volvé a presionar Atrás para cerrar» y requiere otra pulsación dentro de dos segundos.

## Regla de mantenimiento

Actualizar este documento cuando se cambie cualquiera de estos puntos:

- Tabs, pantallas o flujos visibles de la app.
- Funciones del admin/CMS.
- Reglas de acceso, pago, suscripcion o paquetes.
- Datos demo relevantes para entender el producto.
- Roadmap o decisiones funcionales.
