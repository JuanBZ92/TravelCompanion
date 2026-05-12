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
- La app consulta los accesos del usuario demo.
- Las recomendaciones aparecen como incluidas o bloqueadas segun el acceso.
- El usuario demo tiene acceso a Japon Essentials y Travel Companion Premium.

## App mobile

### Recomendaciones

La tab `Recomendaciones` permite:

- ver recomendaciones curadas de Japon;
- filtrar por categoria;
- filtrar favoritos;
- marcar o quitar favoritos;
- ver barrio, descripcion, duracion sugerida y nivel de acceso;
- abrir el detalle de una recomendacion.

El detalle de recomendacion permite:

- guardar o quitar favorito;
- abrir la ubicacion en mapas cuando el contenido esta desbloqueado;
- ver categoria, tipo de acceso, estado de acceso, descripcion y coordenadas.

### Mapa

La tab `Mapa` muestra recomendaciones cercanas.

En plataformas mobile compatibles se usa mapa nativo. En Windows se muestra una experiencia fallback con lista cercana.

### Schedule

La tab `Schedule` muestra un viaje demo agrupado por dia.

Incluye:

- titulo del viaje;
- fechas del viaje;
- reservas por dia;
- hora, lugar, direccion, codigo de confirmacion y nivel de acceso;
- detalle de reserva;
- apertura de direccion en mapas.

Este modulo apunta a cubrir viajes contratados o reservas gestionadas por el negocio.

### Paquetes

La tab `Paquetes` lista paquetes disponibles para el destino demo.

Hoy funciona como catalogo inicial. Mas adelante deberia conectarse con compra, suscripcion o checkout.

### Soporte

La tab `Soporte` existe como punto de entrada para asistencia al viajero.

Todavia no tiene flujo completo de tickets, chat o contacto real.

## Admin CMS

El admin actual permite operar contenido basico sin tocar la base de datos manualmente.

Funciones existentes:

- login de admin;
- dashboard;
- CRUD de recomendaciones;
- CRUD de reservas;
- crear y editar usuarios;
- borrar usuarios;
- asignar accesos a usuarios;
- quitar accesos asignados;
- seleccion de nivel de acceso para recomendaciones;
- seleccion de nivel de acceso para reservas.

Pendiente funcional natural:

- gestionar destinos;
- gestionar paquetes;
- cargar imagenes o media;
- publicar/despublicar contenido;
- ordenar recomendaciones.

## Autenticacion y acceso

Estado actual:

- Admin tiene login por cookie.
- API publica expone endpoints demo.
- Mobile usa usuario demo para simular acceso.
- Admin puede asignar entitlements a usuarios desde el CMS.
- No hay login real de viajero todavia.
- No hay integracion de pagos todavia.

El modelo de entitlements ya prepara la app para compras, paquetes o suscripciones reales.

## Datos demo

Contenido demo actual:

- Destino: Japon.
- Recomendaciones:
  - Tsukiji Outer Market: gratis.
  - Fushimi Inari Taisha: pago fijo.
  - Dotonbori: suscripcion.
- Schedule demo:
  - TeamLab Borderless.
  - Cena omakase.
- Usuario demo:
  - `demo@travelcompanion.local`
  - acceso a Japon Essentials;
  - acceso a Travel Companion Premium.

## Roadmap funcional sugerido

Proximos pasos de mayor valor:

1. Agregar administracion de usuarios y entitlements en el CMS.
2. Agregar login real para viajeros.
3. Conectar paquetes con una pantalla de compra o simulacion de compra.
4. Aplicar bloqueo funcional mas fuerte en API, no solo en UI.
5. Agregar destinos multiples y selector de destino en mobile.
6. Mejorar soporte con formulario, email o chat.
7. Agregar contenido enriquecido: fotos, tips, horarios, links, tags y prioridades.

## Regla de mantenimiento

Actualizar este documento cuando se cambie cualquiera de estos puntos:

- Tabs, pantallas o flujos visibles de la app.
- Funciones del admin/CMS.
- Reglas de acceso, pago, suscripcion o paquetes.
- Datos demo relevantes para entender el producto.
- Roadmap o decisiones funcionales.
