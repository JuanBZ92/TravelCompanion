# Travel Companion - Documentacion funcional

Este documento describe que producto estamos construyendo, que funcionalidades existen y que decisiones funcionales estan vigentes. Debe mantenerse actualizado cada vez que se agregue o cambie comportamiento visible para usuarios, admins o clientes.

## Vision

Travel Companion es una app movil companion de viajes para vender contenido curado por destino y acompanar al viajero durante su viaje.

La idea principal es ofrecer paquetes especificos, por ejemplo Japon, que pueden estar detras de:

- contenido gratuito;
- pago fijo;
- suscripcion;
- bundles o paquetes;
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
- definir si el contenido es gratis, pago, suscripcion o paquete.

## Destinos y paquetes

El destino demo actual es Japon.

Paquetes demo:

- `Japon Essentials`: paquete de pago fijo con recomendaciones, mapa y tips practicos.
- `Travel Companion Premium`: suscripcion con acceso ampliado, updates y soporte prioritario.

La estructura busca permitir mas destinos en el futuro sin cambiar la base conceptual del producto.

## Niveles de acceso

Los niveles funcionales actuales son:

- `Free`: contenido gratuito.
- `Paid`: contenido disponible con pago fijo.
- `Subscription`: contenido incluido con suscripcion activa.
- `Bundle`: contenido incluido por paquete o bundle.
- `AdminOnly`: contenido interno, no publico.

Comportamiento actual en la app:

- Las recomendaciones muestran su tipo de acceso.
- La app consulta los accesos del usuario logueado.
- Las recomendaciones aparecen como incluidas o bloqueadas segun el acceso.
- El usuario demo tiene acceso a Japon Essentials y Travel Companion Premium.
- Las cuentas de prueba separan escenarios: usuario free solo desbloquea contenido gratis, usuario subscription desbloquea gratis y suscripcion, usuario paid desbloquea gratis y pago fijo.

## App mobile

La direccion visual actual busca una experiencia minimalista y refinada:

- paleta sobria de papel calido, tinta, verde profundo y acentos dorados;
- cards planas de radio chico, borde fino y sin sombras decorativas;
- jerarquia clara entre titulo, contexto y metadata;
- categorias y niveles de acceso como metadata sobria;
- botones sobrios con variantes primaria y secundaria;
- tab bar inferior con iconos lineales y labels cortos;
- hero visual local de Japon en la entrada principal, sin depender de imagenes remotas.

### Login

Al iniciar, la app muestra una pantalla de ingreso.

Estado actual:

- login por email y password contra usuarios existentes en el CMS;
- los usuarios nuevos reciben una password temporal generada desde el admin;
- al primer ingreso la app obliga a crear una nueva password;
- sesion guardada localmente en el dispositivo;
- desbloqueo por biometria si hay sesion activa y el usuario lo tiene habilitado;
- fallback a password cuando la biometria falla, no esta disponible o el usuario la cancela;
- el email demo es `demo@travelcompanion.local`;
- la password temporal demo es `TravelDemo!2026`;
- la sesion usa un token opaco emitido por la API.

Esta es una decision de MVP para conectar usuarios, entitlements y schedule. Falta integrar envio real de email.

### Ideas

La tab `Ideas` permite:

- ver recomendaciones curadas de Japon;
- filtrar por categoria;
- filtrar favoritos;
- paginar resultados y elegir cuantas recomendaciones ver por pagina;
- refrescar datos manteniendo la pagina actual cuando el filtro sigue siendo valido;
- marcar o quitar favoritos;
- ver barrio, descripcion, duracion sugerida y nivel de acceso;
- abrir el detalle de una recomendacion.

La app oculta recomendaciones que la cuenta no tiene desbloqueadas. Por ejemplo, el usuario free solo ve contenido gratis; el usuario de suscripcion ve gratis y suscripcion; el usuario paid ve gratis y pago fijo.

Para que la experiencia inicial sea mas rapida y robusta, Ideas usa un paquete reducido con destino y recomendaciones ya filtradas por acceso. Esa copia queda guardada para consulta offline. Despues de pintar Ideas, la app precarga en segundo plano el paquete mobile completo con mapa, schedule y packs para acelerar las otras tabs.

El detalle de recomendacion permite:

- guardar o quitar favorito;
- abrir la ubicacion en mapas cuando el contenido esta desbloqueado;
- ver categoria, tipo de acceso, estado de acceso, descripcion y coordenadas.

### Mapa

La tab `Mapa` muestra recomendaciones cercanas.

En plataformas mobile compatibles se usa mapa nativo. En Windows se muestra una experiencia fallback con lista cercana.

La lista cercana se deriva del paquete mobile completo precargado, calcula distancias localmente y muestra solo la pagina actual para evitar renderizar demasiados pins/list items juntos. Al refrescar, mantiene la pagina actual si sigue existiendo.

### Viaje

La tab `Viaje` muestra el viaje asignado al usuario logueado agrupado por dia.

Incluye:

- titulo del viaje;
- fechas del viaje;
- selector superior para alternar entre eventos, vuelos y hospedajes;
- selector para filtrar por ciudad dentro del tipo seleccionado;
- reservas por dia;
- para eventos: ciudad, hora, lugar, direccion y codigo de confirmacion;
- para vuelos: fecha/hora de salida, llegada, aerolinea, numero de vuelo, origen/destino y aeropuertos;
- para hospedajes: alojamiento, direccion, dia/hora de check-in y dia/hora de check-out;
- detalle de reserva;
- apertura de direccion en mapas.

Al abrir `Viaje`, la app prioriza la informacion relevante desde el momento actual: selecciona el tipo de reserva de la reserva vigente o proxima mas cercana, oculta reservas ya vencidas y solo muestra ciudades con reservas futuras o vigentes.

Este modulo apunta a cubrir viajes contratados o reservas gestionadas por el negocio.

El ultimo paquete mobile descargado incluye el schedule disponible offline por usuario. Si no hay conexion, la app muestra la copia local y avisa la fecha/hora de guardado.
La app mantiene preparadas las secciones de eventos, vuelos y hospedajes para que alternar entre tipos se sienta inmediato despues de la primera carga.

### Packs

La tab `Packs` lista paquetes disponibles para el destino demo.

Cada paquete muestra:

- nombre, descripcion y precio;
- si es pago fijo o suscripcion;
- nivel de acceso requerido;
- si esta incluido o no en la cuenta logueada.

El CMS puede activar paquetes manualmente para usuarios. Esta activacion representa una compra o suscripcion concedida por admin mientras no exista checkout real.

La ultima lista de paquetes queda incluida en el paquete mobile compartido por usuario para consulta offline.

### Cuenta

La tab `Cuenta` existe como punto de entrada para asistencia al viajero.

Tambien muestra la cuenta activa, permite activar/desactivar biometria, bloquear la app sin cerrar sesion y cerrar sesion completamente. Todavia no tiene flujo completo de tickets, chat o contacto real.

### Offline

La app tiene una primera capa offline para pantallas criticas de viaje.

Funciona con estrategia `local first`:

- si existe una copia local, la muestra primero para evitar esperas innecesarias;
- luego intenta descargar datos frescos y actualiza la pantalla/copia local;
- si falla la conexion, conserva la copia local y muestra un aviso de modo offline con la fecha/hora de guardado;
- si no existe copia local, necesita conexion y muestra el error normal.
- durante la primera carga sin contenido local, muestra un spinner en lugar de empty states prematuros.

Pantallas cubiertas:

- recomendaciones;
- mapa/lista cercana;
- viaje;
- paquetes y estado de acceso del usuario.

Limitaciones actuales:

- no hay sincronizacion bidireccional;
- favoritos siguen siendo locales del dispositivo;
- no descarga imagenes ni mapas tiles para offline;
- el primer uso de cada pantalla necesita conexion para generar la copia local.

Decision vigente: no se implementa sync/delta sync todavia porque el producto mobile actual es principalmente de lectura. Se mantiene el camino preparado para agregarlo cuando existan acciones editables desde la app.

## Admin CMS

El admin actual permite operar contenido basico sin tocar la base de datos manualmente.

Funciones existentes:

- login de admin;
- dashboard;
- formularios con campos obligatorios marcados y errores visibles;
- crear, editar y borrar destinos sin contenido asociado;
- crear, editar y borrar paquetes reutilizables sin accesos asociados;
- seleccionar un paquete y asignarle muchos usuarios;
- CRUD de recomendaciones;
- crear, editar y borrar viajes por usuario/destino;
- CRUD de reservas dentro de cada viaje;
- tipo de reserva: evento, vuelo u hospedaje;
- campos especificos para vuelos (aerolinea, vuelo, origen/destino, aeropuertos);
- campos especificos para hospedajes (check-in/check-out, direccion y alojamiento);
- ciudad obligatoria por reserva para organizar viajes multi-ciudad;
- salto directo desde un viaje hacia sus reservas filtradas;
- crear y editar usuarios;
- borrar usuarios;
- generar password temporal al crear usuario;
- resetear password temporal de usuarios existentes;
- asignar accesos a usuarios;
- activar paquetes para usuarios desde la pantalla de paquetes sin duplicar el paquete;
- quitar accesos asignados;
- seleccion de nivel de acceso para recomendaciones;

Pendiente funcional natural:

- cargar imagenes o media;
- publicar/despublicar contenido;
- ordenar recomendaciones.
- adjuntar vouchers, PDFs o QR a reservas.

## Autenticacion y acceso

Estado actual:

- Admin tiene login por cookie.
- API expone login mobile por email y password para usuarios creados en el CMS.
- API emite tokens opacos y guarda solo hash del token.
- Los errores de validacion de login/cambio de password y paginacion se devuelven en formato consistente (`ValidationProblemDetails`) para que la app pueda mostrar mensajes de forma uniforme.
- Mobile guarda la sesion local y usa token bearer para refrescar datos de viaje.
- Mobile usa un endpoint autenticado reducido para Ideas, con destino y recomendaciones ya filtradas por acceso.
- Mobile precarga un bootstrap autenticado compartido para Mapa, Viaje y Packs, con accesos, schedule y paquetes del destino activo.
- Mobile guarda copias offline del discover reducido y del bootstrap completo por usuario.
- Mobile puede desbloquear una sesion local con biometria del dispositivo.
- `Bloquear app` mantiene la sesion local para poder usar biometria.
- `Cerrar sesion` revoca y borra la sesion; despues hay que entrar con password.
- Admin puede asignar entitlements a usuarios desde el CMS.
- Admin puede activar un paquete a un usuario desde `/admin/packages`; el acceso queda asociado al paquete y destino.
- La password temporal obliga cambio en primer ingreso.
- Las passwords temporales no se escriben en logs; en desarrollo solo se muestran en el CMS para facilitar pruebas locales.
- No hay integracion de pagos todavia.

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
  - acceso a Travel Companion Premium.
  - viaje demo de Japon asignado.
- Usuarios de prueba:
  - `usuariofree@travelcompanion.local` / `PasswordFree`: viaje de 2 semanas por Tokyo, Osaka y Kyoto; solo contenido gratis incluido.
  - `usuariosub@travelcompanion.local` / `PasswordSub`: viaje de mas de 2 semanas por Tokyo, Kyoto, Osaka y Nara; contenido gratis y de suscripcion incluido.
  - `usuariopaid@travelcompanion.local` / `PasswordPAid`: viaje de 3 semanas por Tokyo, Osaka, Kobe, Hiroshima, Miyajima, Sapporo y Otaru; contenido gratis y de pago fijo incluido.

## Roadmap funcional sugerido

Proximos pasos de mayor valor:

1. Reemplazar login mobile por identidad real: password, magic link, Auth0, Azure AD B2C o similar.
2. Integrar envio real de email para passwords temporales.
3. Conectar paquetes con checkout real o simulacion de compra iniciada desde la app.
4. Aplicar bloqueo funcional mas fuerte en API, no solo en UI.
5. Agregar destinos multiples y selector de destino en mobile.
6. Mejorar soporte con formulario, email o chat.
7. Agregar contenido enriquecido: fotos, tips, horarios, links, tags y prioridades.

## Operacion e infraestructura

La direccion de infraestructura para el MVP es Azure con Terraform.

Objetivo funcional:

- poder publicar API/Admin en un ambiente cloud real;
- separar datos locales de datos dev/staging/prod;
- proteger credenciales fuera del codigo;
- tener base PostgreSQL administrada;
- preparar almacenamiento de imagenes y media;
- observar errores y comportamiento de la API.

Ambientes esperados:

- `local`: Docker Compose, API local y app en emulador/celular.
- `dev`: primer ambiente Azure para pruebas desde dispositivos reales.
- `staging`: validacion previa a produccion.
- `prod`: datos reales, backups, monitoreo y dominios reales.

## Regla de mantenimiento

Actualizar este documento cuando se cambie cualquiera de estos puntos:

- Tabs, pantallas o flujos visibles de la app.
- Funciones del admin/CMS.
- Reglas de acceso, pago, suscripcion o paquetes.
- Datos demo relevantes para entender el producto.
- Roadmap o decisiones funcionales.
