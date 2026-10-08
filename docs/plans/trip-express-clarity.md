# Viaje claro y recomendaciones exprés

Incremento solicitado el 8 de octubre de 2026 sobre `0416bbd`.
La [matriz](trip-express-clarity.json) registra los criterios independientes y
separa implementación, verificación automática y revisión nativa.

## Alcance

- Quitar de Viaje la instrucción de deslizar fechas y el aviso de itinerario actualizado.
  Conservar errores recuperables y carga inicial.
- Mostrar A continuación sólo con una actividad vigente o futura. Mostrar Mañana
  sólo cuando el siguiente día del viaje tiene planes; las noches de alojamiento
  por sí solas no generan una tarjeta. Conservar la exclusión mutua y la fecha del viaje.
- Presentar Mañana como una tarjeta editorial con fecha, primer plan, cantidad de
  planes y acceso al resumen, distinguible del bloque de hoy.
- Convertir Tengo un rato libre en una búsqueda para ahora o pronto: interés,
  zona actual o próximo plan del día, opciones concretas y reemplazo individual.
  Solicitar ubicación únicamente tras elegir la zona actual.
- Mantener el catálogo autorizado, la validación de compromisos y las confirmaciones
  al añadir. No repetir los lugares visibles ni los ya ofrecidos en esa búsqueda.

## Límites

No se modifican precios, cuotas, permisos, compras, datos existentes ni infraestructura.
Los campos adicionales de criterios deben ser opcionales y compatibles con clientes
anteriores. No hay migraciones PostgreSQL, push o despliegue en esta tarea.

El usuario indicó continuar sin teléfono. La revisión nativa queda pendiente;
una compilación o prueba lógica no acredita capturas, teclado ni composición real
con texto ampliado. TalkBack está excluido permanentemente y la revisión futura usará
PIN, conservando los ajustes globales del dispositivo.

## Comprobaciones previstas

Viaje vacío, mañana vacío, ideas flexibles, check-in y vuelos nocturnos; navegación al
resumen y ausencia de los textos eliminados. En exprés: orden interés/zona, fecha del
viaje, permisos, GPS cancelado o no disponible, próximo plan válido, cambios de
cuenta/viaje/fecha, ventana temporal, alternativas agotadas y reemplazo de una sola
tarjeta manteniendo las demás.

## Implementación y cierre

Los cinco pedidos están implementados y revisados en ocho criterios independientes.
La barra de fechas conserva su desplazamiento y el hint accesible, pero elimina la
instrucción visible. Las actualizaciones del itinerario conservan contenido y errores
recuperables sin publicar el aviso accesorio.

La tarjeta de mañana usa la superficie editorial cálida, fecha, primer plan y cantidad
de planes. Toda la tarjeta abre el resumen. Un día sin planes no genera contenido vacío:
las ideas sin hora y un check-in cuentan; una noche de hotel por sí sola no cuenta.

El acceso exprés se presenta como «Un plan para ahora», con «Ideas rápidas a tu medida».
Primero se eligen intereses; después, zona actual o próximo plan con ubicación. GPS sólo
se solicita al elegir la zona actual. Si falla, se puede reintentar o buscar explícitamente
por ciudad. La duración opcional está en el segundo paso; fecha y horario se calculan
para ahora o después del compromiso en curso, en la zona del viaje.

La API propone hasta dos opciones compatibles reales. La flecha de alternativa sustituye
únicamente la opción elegida, sin ordenar nuevamente las demás. Se excluyen identificadores
y alias de lugares ya ofrecidos durante el recorrido y actividades existentes. Si se
agotan las alternativas, se conserva la tarjeta y se explica el motivo. Cambiar la búsqueda
conserva su historial; salir e iniciar otra búsqueda comienza un recorrido nuevo.

El backend resuelve el próximo plan desde el viaje autenticado. Esa ubicación es un ancla
de búsqueda, no una supuesta posición del viajero. Horarios y traslados siguen siendo
estimaciones. El catálogo y las explicaciones de este recorrido se calculan de forma
determinista; se elimina la petición adicional al modelo cuyo texto no intervenía en
el resultado. No se afirma una reducción de latencia medida en Android.

### Validación del snapshot final

- Mobile: **695/695**; Shared: **85/85**; API afectada: **117/117**. Sin fallos ni omisiones.
  Los totales provienen de cada TRX, no del resumen agregado de RTK.
- Incluye PostgreSQL 17 real, sólo en localhost y con esquema sintético aislado:
  historial de 104 propuestas, recarga y compatibilidad con la columna antigua de 512
  caracteres. El servidor temporal quedó detenido. No se migró producción.
- Se comprobaron selección interés/zona, GPS explícito, alternativas individuales,
  aliases, reintento idempotente, agotamiento, offline, cancelación, medianoche, zonas
  horarias, compromisos nuevos, acceso gratuito, último cupo y acceso revocado.
- API compiló sin errores ni avisos. Android Release ARM64 compiló sin errores; permanecen
  207 avisos XC0025 de bindings con `Source`. XAML válido y 30 claves modificadas usadas
  en el recorrido presentes en ES/EN, sin duplicados.
- QA150: `com.yuku.travelcompanion.plannerpaidreview`, firma de revisión compatible,
  25.147.843 bytes. SHA256:
  `33A76039921BB62EA57E7BD09EC28AA303BCCF064EB20527AE8422A7D2681D3F`.
- Huella de 828 fuentes final:
  `ECE7ACADF1AF3AD7434CBC1CA9BF67D3EEAFED77777A4171527CA9D1206CE51D`.
  Las entradas Mobile/Shared de la APK permanecieron intactas durante publicación.

Artefactos ignorados por Git en `artifacts/trip-express-20261008`: TRX, logs, inventarios,
APK, `apk-proof150.json` y `closure-evidence-150.json`. El comprobador normal aprueba código,
resultados y artefacto; el modo que exige validación nativa devuelve 2 mientras el teléfono
no esté disponible. No se certifican composición, navegación, teclado o texto ampliado
por pruebas de lógica. No se instaló la APK, ni se usó ADB, biometría o TalkBack ni se
modificaron ajustes del usuario.

```powershell
pwsh -File scripts/check-plan-completion.ps1 -MatrixPath docs/plans/trip-express-clarity.json -EvidencePath artifacts/trip-express-20261008/closure-evidence-150.json
```

### Revisión nativa pendiente

La QA150 apunta al backend local `http://127.0.0.1:5188`; necesita la API revisada activa y
el acceso local correspondiente desde el teléfono. Cuando vuelva a estar disponible,
instalar sobre QA conservando datos y revisar con PIN, sin cambiar ajustes globales:

1. En hoy, comprobar actividad futura/en curso, fin de la última y mañana con/sin planes;
   abrir la tarjeta de mañana y volver conservando el día seleccionado.
2. Abrir «Un plan para ahora»: seleccionar interés y después ambas zonas. Cancelar GPS,
   recuperar por ciudad, comprobar error/offline y regreso sin perder elecciones.
3. Generar opciones y sustituir una: conservar la otra y sus posiciones. Añadir mediante
   confirmación; comprobar actividad nueva y alternativa agotada sin borrar propuestas.
4. Revisar ES/EN, nombres de iconos, textos largos y fuentes ampliadas mediante override
   exclusivo de QA. TalkBack está excluido, no pendiente.

### Publicación posterior

No se hizo push ni despliegue. Publicar primero la API y luego el cliente: los campos
opcionales de ancla e historial requieren el backend nuevo para garantizar el comportamiento
exprés. No se requieren migraciones PostgreSQL. La versión normal del proyecto permanece
149; QA150 usa overrides de compilación y backend local.

Para revertir, retirar primero el cliente con este recorrido y después la API; no eliminar
el contenido de `StateJson`. Clientes anteriores conservan contratos y huellas JSON porque
los nuevos campos nulos se omiten. La columna histórica mantiene una proyección limitada,
y la versión nueva conserva el historial completo en el JSON existente.
