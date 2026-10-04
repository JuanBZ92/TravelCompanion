# Auditoría móvil de UX y presentación — octubre de 2026

## Cobertura y criterio

Inventario inicial: **32 páginas concretas**, **dos paneles** y los estados internos
del Asistente, mapas, compras y gastos. Se excluyen las clases base `TripScopedPage`
y `JournalScopedPage`. La auditoría combina rutas de Shell, navegación modal,
registros DI y lecturas dirigidas de XAML/C#; no equivale a una revisión visual en
dispositivo de todas las combinaciones.

La entrega añade la página de propuestas del planificador: el inventario actual
contiene **33 páginas concretas**. La página de personalización anterior se
conserva para compatibilidad; la navegación principal utiliza el flujo unificado.

La referencia editorial son Journal, Cuenta, Carpeta, Gastos y Pase: papel cálido,
títulos serif, texto legible, tarjetas discretas, iconos con intención clara y una
acción principal. `EditorialUi` y los estilos `Editorial*` reúnen estos recursos
para las nuevas pantallas. La aplicación fuerza el tema claro; añadir modo oscuro
queda fuera de esta entrega.

Se auditan todas las pantallas; el rediseño profundo se concentra en el planificador.
Las demás reciben correcciones concretas de prioridad alta, conservando contratos,
datos, navegación y permisos. No se eliminan pantallas consideradas legadas sin
confirmar su uso.

## Matriz de pantallas

| Grupo | Pantalla o superficie | Entrada y estados que deben cubrirse | Resultado de la revisión estática |
|---|---|---|---|
| Acceso | Login | Inicio, PIN, recuperación, carga/error | Campo PIN conserva edición accesible; acciones secundarias pasan a 48 dp. |
| Acceso | Desbloqueo biométrico | Sesión bloqueada, rechazo, alternativa | Mantiene flujo existente. Quedan textos españoles y disposición centrada para una revisión posterior. |
| Acceso | Cambiar contraseña | Contraseña obligatoria, teclado, validación | Formulario desplazable, placeholders claros y error visible con foco semántico. |
| Acceso | Logout | Registro DI | No encontradas llamadas actuales; conservar como legado hasta comprobar uso. |
| Viaje | Itinerario | Día vacío, cargado, offline, bloqueo, muchos planes | Referencia de línea temporal; el nuevo planificador unifica la entrada de creación. Revisar en dispositivo las nuevas acciones y letra grande. |
| Viaje | Crear/configurar viaje | Sin viaje, ciudades/hoteles, sugerencias, errores | Cabecera y tarjetas comunes; eliminar ciudad tiene descripción; sugerencias y reintento mantienen 48 dp; error se revela. |
| Viaje | Editor de actividad | Nuevo/existente, sugerencias, hora/reminder, error | Textos modificados ES/EN, cabecera editorial, switch con descripción y error accesible. |
| Viaje | Detalle de actividad | Hora flexible/fija, documento, Journal, gasto | Mapa compacto de 176 dp, navegación 48 dp, textos y acciones ES/EN. Conserva permisos y vínculos. |
| Viaje | Preparación/Carpeta | Categorías, adjuntar, estados manuales, offline | Se reutiliza el panel actual; filas y acciones ya ofrecen 48 dp y descripciones. |
| Viaje | Documentos | Categoría/todos, personales/pase, vacío, error | Acciones de documento y pestañas de vuelo amplían su área táctil; etiquetas ES/EN; carga y reintento visibles. |
| Viaje | Revisión del viaje | Vacío, conflictos, caché, permisos | Lista virtualizada y herramientas 48 dp; desde el planificador se filtra el intervalo elegido. |
| Viaje | Conflictos del día | Datos locales/remotos, sin conflictos, edición | Mantiene datos locales y reintento existentes; revisar continuidad al editar y volver. |
| Planificación | Mejorar el día | Fecha/ciudades, opciones disponibles, error | Fecha y duración 1/3/5/7 días, preferencias desplegables y una acción principal para generar. |
| Planificación | Personalizar el día | Ritmo, presupuesto, intereses | Preferencias integradas en el flujo principal; página anterior conservada para compatibilidad. |
| Planificación | Selección de alternativas | Una/múltiples actividades, cancelar | Mantiene identidad y contratos actuales para reemplazar ideas; separar esta acción de añadir ideas. |
| Planificación | Asistente | Inicio, conversación, tarjetas, detalle, permisos | Auditar junto con planificación: distinguir generar, añadir, reemplazar y revisar; conservar chat. |
| Explorar | Mapa completo | Búsqueda, vacío, carga/error, selección, páginas | Textos ES/EN, navegación y cierre de 48 dp; contenedores ampliados para evitar recorte. |
| Explorar | Mapa gratuito | Ciudad, radio, selección desbloqueada/bloqueada, pase | Textos ES/EN y controles 48 dp, conservando las restricciones actuales. |
| Explorar | Detalle de recomendación | Lugar, descripción, mapa, bloqueo | Mapa de 176 dp, título y texto prioritarios; regreso accesible; textos ES/EN. |
| Explorar | Discover | Registro DI, filtros/lista | No encontrada entrada actual en Shell/navegación; registrar como legado y conservar. |
| Journal | Lista | Sin viaje/vacío, recuerdos, borradores, sync/error | Destino y resumen pueden ocupar varias líneas; toda la tarjeta ofrece botón accesible incluso con fotos. |
| Journal | Lectura | Texto/fotos, actividad, error | Conserva cierre y permite reintento si falla la lectura; contenido local sigue disponible. |
| Journal | Editor | Libre/actividad, borrador, fotos, guardar/error | Reutiliza paleta común. Mantiene acciones 48 dp, autosave, límites y almacenamiento existentes. |
| Journal | Selector de actividad | Día seleccionado, búsqueda, vacío, cancelar | Lista virtualizada y acción de cierre existentes; verificar selección con teclado y fuente ampliada. |
| Journal | Fotos | Portada, anterior/siguiente, archivo ausente | Mensajes de ausencia usan recursos ES/EN existentes; controles 48 dp. |
| Journal | Exportación | Fechas, portada, progreso, cancelación, offline | Mantiene exclusión de borradores y fotos locales; verificar texto ampliado y fotos ausentes. |
| Gastos | Editor | Nuevo/existente, categoría, importe, actividad, error | Mantiene flujo actual e iconos 48 dp; reutiliza paleta/tarjetas comunes. |
| Gastos | Selector de actividad | Fecha, búsqueda, vacío, ninguna | Mantiene lista virtualizada y cancelación; revisar fila con texto largo. |
| Gastos | Moneda/presupuesto | Opcional, validación, cambio de moneda | Mantiene confirmación y precisión; usa paleta/tarjetas comunes. |
| Cuenta | Cuenta | Sin/con viajes, sesión, preferencias, error | Error ofrece reintento sin borrar contenido; navegación y acciones existentes conservadas. |
| Comercial | Pase | Oferta, gratis/pase, compra pendiente, restaurar/error | Referencia para CTA persistente; compras/tienda requieren comprobación específica, no simular éxito. |
| Comercial | Packages | Registro DI | No encontrada ruta actual; conservar como legado hasta comprobar uso. |

Los paneles **Carpeta** y **Gastos** forman parte de Viaje y no aumentan el conteo de
páginas. Gastos dispone además de estados de desglose y resolución de conflictos
creados dinámicamente; se incluyen en el recorrido aunque no tengan archivo de
página independiente. Los nuevos pasos de propuesta/revisión del planificador se
auditan como superficies del flujo unificado, además del inventario inicial.

## Prioridades implementadas fuera del planificador

1. **Recursos compartidos:** paleta editorial, cabeceras, tarjetas, botones e iconos;
   Gastos y Journal reutilizan la paleta, sin incorporar dependencias.
2. **Interacción accesible:** controles de mapa/documentos/acceso de al menos 48 dp,
   navegación con descripciones y tarjetas de Journal/Gastos con botón accesible.
3. **Idiomas:** textos visibles y descripciones de mapas, detalles, editor y
   Documentos en español e inglés; ausencia de fotos localizada.
4. **Formularios:** contraseña desplazable y errores revelados en contraseña,
   configuración del viaje y actividad. El PIN mantiene la acción nativa de edición.
5. **Lectura:** mapa de detalle más compacto y metadatos del Journal sin fila rígida.
6. **Recuperación:** reintento visible en Cuenta, Documentos y lectura de recuerdos,
   manteniendo las acciones de navegación.
7. **Continuidad:** regla de calidad UX en `AGENTS.md`; limpieza del borrador del
   planificador al eliminar cuenta y al vincular una cuenta anónima.

## Validación y límites de la evidencia

Completado por inspección: inventario de rutas/páginas y lectura de los grupos
anteriores; XML XAML válido; claves `Ux*` presentes en ambos recursos y sin
duplicados. Esta comprobación no verifica el aspecto final, el orden real de
TalkBack ni la interacción con el teclado.

La compilación Android final aprobó sin advertencias ni errores; la APK separada
y el resultado definitivo de las suites se registran en el informe del planificador.
El Android no aparece en las tres comprobaciones recientes de ADB. No se pudieron
obtener capturas ni ejecutar esta revisión visual en el dispositivo, y no se
consideran verificadas en dispositivo las pantallas solo inspeccionadas.

La suite móvil completa aprobó **273 pruebas**, sin omisiones. Incluye
**16 pruebas del `DayPlannerViewModel` de producción**, enlazado al
proyecto de pruebas con cliente HTTP y almacén reales e I/O controlado. Todas
aprobaron, sin omisiones ni advertencias de compilación. Las dependencias nativas
de Shell, conectividad, lector de pantalla y bootstrap se sustituyen por stubs;
estos casos verifican estado y flujo, no el renderizado ni TalkBack.

Los casos cubren la identidad de generación tras timeout y cambio de revisión;
recuperación de fecha/duración/preferencias, también offline; fecha explícita nueva;
revisión tras aplicar parcialmente; recibo de guardado pendiente tras refresco y
reinicio; cancelación explícita; selección de ideas ya guardadas; descarte de una
respuesta tras cambio de contexto; días vacíos/parciales; intervalo de revisión y
límites existentes de la vista gratuita y regreso al primer día de la propuesta
mediante «Ver en mi viaje» tras guardar todas las ideas seleccionadas. Los fallos locales de I/O, permisos y
cifrado preservan la propuesta y su selección sin escapar de la salida de página.
La comprobación detectó y permitió corregir
dos fallos: la duración restaurada offline mostraba un día y una fecha explícita
nueva reutilizaba la generación pendiente del día anterior.

```powershell
dotnet test tests/TravelCompanion.Mobile.Tests/TravelCompanion.Mobile.Tests.csproj --verbosity minimal --filter 'FullyQualifiedName~DayPlannerViewModelTests'
```

El resultado final de la suite completa se conserva en
`artifacts/day-planning-tests/mobile-audit-final.trx`.

La revisión estática del nuevo planificador detectó también puntos de regresión:
conservar preferencias al refrescar opciones; guardar la configuración editada
independientemente de la solicitud idempotente; actualizar la revisión tras añadir;
distinguir timeout de cancelación; impedir la selección de ideas ya guardadas y
serializar la limpieza durante la vinculación de cuentas. Son criterios de las
pruebas funcionales, no resultados visuales acreditados por esta inspección.

La propuesta mantiene fija únicamente una cabecera compacta y la acción de añadir.
El resumen de fechas, explicación, estado, errores y acción de otra propuesta se
desplazan dentro de la cabecera de la lista virtualizada. El progreso y la cancelación
comparten una fila de al menos 48 dp; las casillas combinan el estado de guardado por
idea con el bloqueo global durante operaciones. Esta estructura reduce el espacio
fijo consumido con fuente ampliada. El aspecto a 150 % y los grupos sin resultados
requieren comprobación real en Android.

Recorridos para la revisión visual:

- Crear viaje y editar actividad con teclado abierto, validación fallida y texto largo.
- Generar propuestas para uno y varios días, revisar, seleccionar, añadir y volver al viaje.
- Abrir mapas gratis/completo; buscar sin resultados, cerrar preview y cambiar lugar.
- Abrir detalle de actividad/recomendación con título largo y todas las acciones.
- Journal vacío, destino largo, recuerdo con/sin fotos, error de lectura y borrador.
- Carpeta/Documentos: categoría vacía, adjuntar/cancelar, abrir y cambiar categoría.
- Gastos vacíos/con datos: abrir fila accesible, editar, presupuesto y conflictos.
- Cuenta sin/con viajes y Pase con oferta/error; compra real solo con entorno adecuado.

Repetir los recorridos prioritarios en español e inglés, con ancho compacto,
fuente ampliada y TalkBack. Probar offline, red lenta y errores sin borrar datos
personales; usar contenido sintético para capturas y cambios.

## Mejoras posteriores registradas

La APK final `artifacts/mobile/DayPlannerReview-v115.apk` compiló sin advertencias ni errores. Sus metadatos confirman `com.yuku.travelcompanion.plannerreview`, versión 115, nombre `Yuku Planner` y backend local 5188. No se instaló: tres comprobaciones de ADB devolvieron cero dispositivos. Esta evidencia confirma empaquetado; no confirma apariencia ni interacción en Android. Las pruebas automatizadas finales fueron 273/273, incluidas 16 del ViewModel real.

- Comprobar listas extensas de Viaje/Documentos/mapa y virtualizar donde un perfil
  real confirme coste; Journal/Gastos/revisión ya usan listas virtualizadas.
- Completar localización y ergonomía de biometría, evitando alterar el mecanismo
  de desbloqueo durante una revisión visual.
- Confirmar uso de Discover/Packages/Logout antes de consolidar o retirar legados.
- Revisar la cabecera estrecha del Asistente, estados de sincronización, compra y
  autenticación con datos sintéticos y dispositivos de distintos tamaños.
