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
| Acceso | Desbloqueo biométrico | Sesión bloqueada, rechazo, alternativa | Textos y diálogo nativo ES/EN; diseño editorial desplazable, botones de 48 dp, estado anunciado y error con foco. Conserva el mecanismo y las comprobaciones de sesión/contexto. |
| Acceso | Cambiar contraseña | Contraseña obligatoria, teclado, validación | Formulario desplazable, placeholders claros y error visible con foco semántico. |
| Acceso | Logout | Registro DI | No encontradas llamadas actuales; conservar como legado hasta comprobar uso. |
| Viaje | Itinerario | Día vacío, cargado, offline, bloqueo, muchos planes | Entrada unificada «Planificar»/«Plan your days»; pestañas, menú y controles modificados ES/EN. Origen de ruta con botón nativo de 48 dp; selector de fechas ampliado tras detectar recorte a 150 %. |
| Viaje | Crear/configurar viaje | Sin viaje, ciudades/hoteles, sugerencias, errores | Cabecera y tarjetas comunes; eliminar ciudad tiene descripción; sugerencias y reintento mantienen 48 dp; error se revela. |
| Viaje | Editor de actividad | Nuevo/existente, sugerencias, hora/reminder, error | Textos modificados ES/EN, cabecera editorial, switch con descripción y error accesible. |
| Viaje | Detalle de actividad | Hora flexible/fija, documento, Journal, gasto | Mapa compacto de 176 dp, navegación 48 dp, textos y acciones ES/EN. Conserva permisos y vínculos. |
| Viaje | Preparación/Carpeta | Categorías, adjuntar, estados manuales, offline | Se reutiliza el panel actual; filas y acciones ya ofrecen 48 dp y descripciones. |
| Viaje | Documentos | Categoría/todos, personales/pase, vacío, error | Acciones de documento y pestañas de vuelo amplían su área táctil; etiquetas ES/EN; carga y reintento visibles. |
| Viaje | Revisión del viaje | Vacío, conflictos, caché, permisos | Lista virtualizada y herramientas 48 dp; desde el planificador se filtra el intervalo elegido. |
| Viaje | Conflictos del día | Datos locales/remotos, sin conflictos, edición | Mantiene datos locales y reintento existentes; revisar continuidad al editar y volver. |
| Planificación | Mejorar el día | Fecha/ciudades, opciones disponibles, error | Fecha y duración 1/3/5/7 días, preferencias desplegables y una acción principal para generar. Error fijo bajo la cabecera, con foco semántico; no queda dentro del scroll. |
| Planificación | Propuesta de varios días | Días completos/parciales/vacíos, selección, guardar/error | Lista virtualizada agrupada, revisión y selección por idea; error fijo bajo la cabecera. Conserva propuesta y selección ante fallos recuperables. |
| Planificación | Personalizar el día | Ritmo, presupuesto, intereses | Preferencias integradas en el flujo principal; página anterior conservada para compatibilidad. |
| Planificación | Selección de alternativas | Una/múltiples actividades, cancelar | Mantiene identidad y contratos actuales para reemplazar ideas; separar esta acción de añadir ideas. |
| Planificación | Asistente | Inicio, conversación, tarjetas, detalle, permisos | Cabecera flexible con fecha en una segunda fila, sugerencias mediante botones nativos de 48 dp y acciones que pueden envolver. Título español «Asistente»; conserva chat y navegación existentes. |
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

### Hallazgos resueltos en la revisión adicional

P1 identifica recorte, interacción o recuperación que dificultan usar el flujo;
P2 identifica coherencia visual o de idioma. La evidencia de esta tabla es
estática salvo la observación de recorte indicada; no acredita TalkBack ni una
revisión visual completa de las 33 páginas.

| Prioridad | Hallazgo | Corrección implementada | Archivo y evidencia |
|---|---|---|---|
| P1 | La disposición centrada de biometría no ofrecía margen para texto ampliado ni error revelado. | Contenido desplazable, cabecera y tarjeta editoriales, botones mínimos de 48 dp, error con foco semántico y anuncios de estado. | `src/TravelCompanion.Mobile/Pages/BiometricUnlockPage.xaml` y `.xaml.cs`: `UnlockScroll`, `UnlockError`, `OnUnlockStateChanged`. |
| P1 | Un fallo al iniciar el desbloqueo necesitaba conservar una salida visible y evitar mostrar el detalle técnico. | Mensaje localizado recuperable y alternativa de contraseña; se conservan las comprobaciones de sesión, biometría habilitada y `ContextVersion` durante la operación. | `src/TravelCompanion.Mobile/ViewModels/BiometricUnlockViewModel.cs`: `UnlockAsync`; la página captura también el fallo del intento automático. No cambian mecanismo ni permisos. |
| P2 | Biometría mantenía mensajes y diálogo nativo solo en español. | Recursos `Biometric*` ES/EN para estado, rechazo, error, acciones y solicitud nativa. | `src/TravelCompanion.Mobile/Services/BiometricUnlockService.cs` y `Localization/AppResources*.resx`: `AuthenticationRequest` utiliza los recursos existentes en ambos idiomas. |
| P1 | El Asistente reservaba una columna estrecha para la fecha y algunas sugerencias dependían de gestos. | Fecha en una segunda fila, cabecera flexible, sugerencias nativas y acciones de 48 dp; botones secundarios pueden envolver. | `src/TravelCompanion.Mobile/Pages/TravelChatPage.xaml`: cabecera `48,*` con dos filas, `OnSuggestedReplyClicked` y mínimos de 48 dp. |
| P2 | El título español del Asistente y la entrada inglesa de planificación no coincidían con el flujo nuevo. | «Asistente» en ES; «Plan your days» y subtítulo para uno o varios días en la entrada EN de Viaje. | `src/TravelCompanion.Mobile/Localization/AppResources.es.resx`: `AssistantTitle`; `AppResources.resx`: `TodayImproveDay` y `TodayImproveSubtitle`. |
| P1 | La ciudad del selector de fechas se recortaba con fuente al 150 %. | Selector de 92 dp, tarjetas de al menos 88 dp y ancho 112 dp, etiquetas centradas sin alturas rígidas y botón nativo accesible. | `src/TravelCompanion.Mobile/Pages/SchedulePage.xaml`: `DayFilters`; el recorte original se detectó en Android al 150 %. La comprobación visual de la corrección pertenece a la revisión en curso. |
| P1 | «Desde mi ubicación» era una superficie con gesto de menos de 48 dp. | Botón nativo con mínimo de 48 dp; mantiene comando y parámetro de ruta. | `src/TravelCompanion.Mobile/Pages/SchedulePage.xaml`: botón con `CalculateRouteCommand` y `CurrentLocationRoute`; texto mediante recursos ES/EN. |
| P2 | Pestañas y menú de Viaje usaban textos literales o traducciones asignadas una sola vez. | Bindings de recursos para pestañas, opciones y conflictos; menú localizado al abrirlo y comparaciones contra sus mismas etiquetas. | `src/TravelCompanion.Mobile/Pages/SchedulePage.xaml` y `.xaml.cs`: recursos `UXAudit*`, `OnItineraryMenuClicked`; se eliminan los overrides de texto del constructor. |
| P2 | Los encabezados de los períodos conservaban la etiqueta española de la API al usar inglés. | `morning`, `midday`, `afternoon` y `night` resuelven recursos ES/EN, también durante la carga. Día y estado libre usan recursos; períodos desconocidos y descripciones personales/editoriales conservan su texto recibido. | `src/TravelCompanion.Mobile/ViewModels/ScheduleTodaySectionViewModel.cs`: `ResolvePeriodLabel`, `FormatDayTitle` y `NormalizeDescription`; `ScheduleViewModel.cs`: `BuildTodayLoadingSections`. |
| P1 | Los errores de generar o guardar podían quedar fuera del área visible al desplazarse. | Error fijo bajo la cabecera en formulario y propuesta; foco con `EditorialUi.RevealError(Label)` y comprobación del control visible/con handler. | `src/TravelCompanion.Mobile/Pages/ImproveDayPage.xaml`, `DayPlanProposalPage.xaml` y `EditorialUi.cs`: `PlannerError` fuera del contenido desplazable. |
| P1 | Viaje y Documentos materializaban todas las tarjetas dentro de un scroll. | Una lista virtualizada por pantalla, con actividades/reservas y documentos/tramos como filas individuales; no hay límite de elementos. | `SchedulePage.xaml`, `DocsPage.xaml`, selectores y modelos de presentación. Casos de cien reservas/quinientas filas aprobados; perfil nativo posterior pendiente. |
| P1 | Volver a Itinerario después de cambiar de cuenta desde Carpeta podía mostrar una página vacía. | Iniciar la carga al seleccionar Itinerario si el reset de sesión vació sus datos. | `SchedulePage.xaml.cs`: `OnItinerarySectionClicked`. Reproducción anterior nativa; verificación posterior pendiente. |
| P1 | Un error de Documentos podía quedar oculto al recorrer una lista larga. | Error fijo y reintento fuera de la lista; foco semántico y suscripciones limitadas a la página visible. | `DocsPage.xaml` y `.xaml.cs`: `DocumentError`, `OnDocumentStateChanged`. Compilación Android aprobada; revisión nativa pendiente. |

La localización acreditada comprende los textos modificados y sus recursos. No
se afirma que todos los textos históricos de las pantallas o flujos legados estén
traducidos.

## Validación y límites de la evidencia

Completado por inspección: inventario de rutas/páginas y lectura de los grupos
anteriores; XML XAML válido; claves `Ux*`, `UXAudit*` y `Biometric*` modificadas presentes en ambos recursos y sin
duplicados. Esta comprobación no verifica el aspecto final, el orden real de
TalkBack ni la interacción con el teclado.

La compilación Android de la entrega inicial aprobó sin advertencias ni errores;
la APK separada y los resultados de las suites se registran en el informe del
planificador. El dispositivo Android está ahora conectado y la revisión visual
parcial está en curso con contenido sintético. La falta de detección de ADB del
intento inicial dejó de ser un bloqueo. Los recorridos, idiomas, escala de texto,
capturas y resultados finales deben acreditarse individualmente; las pantallas
solo inspeccionadas siguen sin considerarse verificadas en dispositivo.

La suite móvil completa de la entrega inicial aprobó **273 pruebas**, sin omisiones. Incluye
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

La revisión adicional de localización ejecutó **37 pruebas focales de Schedule**,
incluidos **15 casos nuevos** con recursos ES/EN reales. Comprueban los cuatro
períodos, títulos de día, estado libre, fallback de períodos desconocidos y
conservación de descripciones recibidas. La suite móvil completa posterior aprobó
**307/307 pruebas**, sin omisiones ni advertencias de compilación. Resultados en
`artifacts/day-planning-tests/schedule-localization.trx` y
`artifacts/day-planning-tests/mobile-localization-final.trx`; siguen siendo pruebas
de lógica y recursos, no de renderizado ni TalkBack.

La revisión estática del nuevo planificador detectó también puntos de regresión:
conservar preferencias al refrescar opciones; guardar la configuración editada
independientemente de la solicitud idempotente; actualizar la revisión tras añadir;
distinguir timeout de cancelación; impedir la selección de ideas ya guardadas y
serializar la limpieza durante la vinculación de cuentas. Son criterios de las
pruebas funcionales, no resultados visuales acreditados por esta inspección.

La propuesta mantiene fijos una cabecera compacta, el error cuando existe y la
acción de añadir. El resumen de fechas, explicación, estado y acción de otra
propuesta se desplazan dentro de la cabecera de la lista virtualizada. El formulario
también coloca su error bajo la cabecera, fuera del scroll. Ambos llaman a
`EditorialUi.RevealError(Label)` para dar foco al mensaje. El progreso y la cancelación
comparten una fila de al menos 48 dp; las casillas combinan el estado de guardado por
idea con el bloqueo global durante operaciones. Esta estructura reduce el espacio
fijo consumido con fuente ampliada. El aspecto final a 150 % y los grupos sin
resultados requieren evidencia específica de la revisión Android en curso.

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

### Revisión nativa complementaria del 5 de octubre

Se utilizó un Samsung Android físico (384 dp de ancho), una API local con datos
sintéticos y la aplicación separada `com.yuku.travelcompanion.plannerreview`,
«Yuku Planner», versión 116. La aplicación habitual permaneció en la versión 115
y conservó sus datos. Las capturas siguientes corresponden al corte anterior a la
virtualización final y a la última corrección de períodos; no acreditan esos
cambios nuevos.

| Recorrido | Comprobación realizada | Evidencia y límite |
|---|---|---|
| Planificación ES/EN con pase | Fechas y ciudades Tokyo/Kyoto antes de generar; propuestas de 3/5/7 días; selección por día y tarjeta; guardado parcial de nueve ideas y guardado de 28 ideas. | `artifacts/mobile/planner-after-form.png`, `planner-seven-days-en.png`. El contador pasó de tres a doce y después a cuarenta planes. |
| Recuperación y error | La propuesta y selección sobrevivieron al reinicio/actualización de la APK. Detener la API local mostró el error bajo la cabecera sin borrar la propuesta. | `planner-error-final-en.png`. La recuperación posterior de conexión queda pendiente de repetir en la APK final; las pruebas de lógica cubren reintento. |
| Acceso gratuito | Recuperación por correo con el emisor de desarrollo local; generar y guardar cuatro ideas para un día, conservando la reserva previa; elegir cinco días abrió el pase. | `planner-free-one-day-en.png`, `planner-free-pass-gate.png`. No hubo compra ni envío de correo externo. La captura precede a la corrección de «1 days». |
| Texto ampliado | Formulario/propuestas y tarjetas largas examinados con fuente al 150 %; el selector de fecha anterior recortaba la ciudad. | `planner-five-days-150.png`, `planner-five-days-150-cards.png`. El selector se amplió en código; su corrección final requiere revisión nativa. |
| Journal y teclado | Abrir un recuerdo sintético largo, editar con el teclado, guardar y comprobar el texto conservado al volver. | `journal-reader-review.png`, `journal-editor-keyboard.png`. Se observó estado pendiente de sincronización; no se acredita aquí la sincronización remota de Journal. |
| Documentos personales | Cancelar el selector, adjuntar un PDF sintético, moverlo de Transporte a Alojamiento y abrir su contenido local. | `documents-review-synthetic.png`, `documents-open-synthetic-pdf.png`. Estas acciones preceden al cambio de lista; falta repetirlas offline y con reciclado de filas. |
| Gastos | Estado vacío, teclado al introducir 1.000 JPY, guardar y comprobar la fila y el total aproximado de 6,33 USD. | `expenses-empty-en.png`, `expense-keyboard-review.png`, `expenses-saved-en.png`. No se acredita editar o resolver conflictos de gasto en este recorrido. |
| TalkBack | Servicio activado temporalmente; foco nativo mediante teclado en Itinerario y navegación entre controles de fecha. | `talkback-itinerary-keyboard.png`. El servicio previo se restauró; no se verificaron todos los anuncios ni el audio hablado. |
| Listas extensas | Recorrer cien reservas manuales y cien documentos incluidos, hasta el documento número cien, sin ANR observado. | `docs-100-before.png`, `docs-memory-100-before.txt`, `docs-gfx-100-before.txt`; métricas anteriores a la virtualización, detalladas abajo. |

El perfil anterior de Documentos, con proceso recién abierto y cien documentos
incluidos, registró 1.560 vistas y 794.175 KB de PSS. Tras recorrer la lista registró
790.916 KB de PSS; 1.383 frames, 0,43 % fuera del plazo y p95 de 18 ms. Viaje registró
3.748 vistas y 986.680 KB de PSS tras varios recorridos en un proceso de depuración;
672 frames, 1,49 % fuera del plazo y p95 de 19 ms. Son observaciones locales de una
APK Debug, no comparaciones controladas ni medidas de Release. La lectura previa
de memoria de Viaje ya contenía la lista larga y no sirve como baseline de una
lista pequeña. No se atribuye causalmente esa memoria a un cambio concreto.

La inspección de código confirmó que `ScrollView` con `BindableLayout` creaba
todas las tarjetas de Viaje y Documentos. Ambos ahora usan un único
`CollectionView` vertical: períodos con actividades/reservas como filas en Viaje;
categorías personales, documentos incluidos, hoteles, selector de vuelo y cada
tramo como filas en Documentos. Se conservan todos los elementos, su orden,
acciones e identidades; no se añade un máximo. El error de Documentos queda fijo
con su reintento. Las pruebas cubren cien reservas, quinientas filas de documentos,
orden e independencia de permisos. Esta corrección está implementada; su ahorro
nativo todavía no se ha medido.

El corte final aprobó **321/321 pruebas móviles**, sin fallos ni omisiones; TRX
`artifacts/day-planning-tests/mobile-final/mobile-final.trx`. La compilación Android
final aprobó con cero advertencias y errores. El artefacto de revisión y su hash
figuran en [validación del planificador](day-planner-validation.md#apk-y-comprobación-http-final).

Se corrigió además el regreso a Itinerario después de cambiar de cuenta mientras
Carpeta seguía seleccionada: la selección de la pestaña inicia la carga si el
itinerario fue vaciado por el reset de sesión. Antes quedaba en blanco hasta hacer
pull-to-refresh. La reproducción original fue nativa; la comprobación posterior
de la corrección está pendiente.

**Pendiente por instrucción del usuario:** el teléfono dejó de estar disponible y
el usuario pidió continuar sin él y anotar las pruebas visuales restantes. La APK
final requiere instalación y revisión de Viaje/Documentos con cien elementos,
reciclado de filas y acciones de la primera/última; comparación de vistas/memoria
en procesos recién abiertos; fuente al 150 %, ES/EN y TalkBack; regreso tras cambiar
de cuenta; error/reintento al recuperar conexión; apertura offline de documentos;
y los recorridos de edición pendientes de la matriz. No se presenta la
compilación ni la suite de lógica como sustituto de estas pruebas.

El empaquetado inicial `artifacts/mobile/DayPlannerReview-v115.apk` confirmó el
identificador separado `com.yuku.travelcompanion.plannerreview`, nombre `Yuku
Planner` y backend local 5188. Es evidencia histórica de compilación y empaquetado;
no sustituye la comprobación de la APK que se utilice para la revisión actual.
El informe del planificador registra los artefactos y suites definitivos. La
auditoría estática de 33 páginas y dos paneles se mantiene separada de la revisión
visual parcial del Android conectado.

- Comprobar en Android el ahorro y la interacción de las nuevas listas
  virtualizadas de Viaje/Documentos; Journal/Gastos/revisión ya utilizan listas
  virtualizadas. El mapa mantiene su comprobación específica de muchas ubicaciones.
- Comprobar en dispositivo el rechazo/error biométrico, la alternativa de
  contraseña, el foco y los anuncios de estado en ES/EN y con texto ampliado.
- Confirmar uso de Discover/Packages/Logout antes de consolidar o retirar legados.
- Confirmar cabeceras y botones modificados del Asistente y Viaje con fuente
  ampliada y TalkBack; revisar estados de sincronización, compra y autenticación
  con datos sintéticos y distintos anchos. Estos recorridos pendientes son límites
  de validación, no funcionalidades ausentes.

## Ajuste posterior de planificación y selector de días

La revisión local posterior compacta la duración en cuatro columnas, añade flechas para cambiar una idea en su tarjeta y combina fecha/hotel en el selector central de Viaje. Los días adyacentes y los gestos horizontales usan la misma selección y cancelan la carga anterior; el nombre del hotel conserva la acción existente de Maps. Los botones mantienen 48 dp mínimos, ajuste de texto y recursos ES/EN; el centro muestra hasta dos líneas y conserva el nombre completo para accesibilidad.

El usuario volvió a conectar el Android y pidió publicar en `main`. El corte inicial se publicó como `b48619f`; la revisión posterior instalada es v120, separada de la app habitual y con backend local. La [validación de este ajuste](day-planner-refinement.md) registra artefacto/hash, 384 pruebas móviles finales y la cobertura física.

Se comprobaron ES/EN, las cuatro duraciones, letra al 150 % en español, reemplazos repetidos de una tarjeta desmarcada, caída/reinicio/reintento del backend y conservación de la selección. El dispositivo encontró que SwipeGestureRecognizer interceptaba Clicked en Android; el intento de compartir tap/swipe en el Button activaba dos veces la acción. La solución final usa superficies físicas separadas y conserva los botones para accesibilidad/teclado. Se verificaron toques adyacentes, gestos desde las tres tarjetas, bloqueo gratuito, hotel de varias noches, cambio Tokyo/Kyoto y apertura de Maps con la consulta sintética. Se encontró también WebException envolviendo Java.IO.IOException: v120 presenta el aviso localizado y conserva el itinerario al arrancar sin backend.

Se recorrieron las nuevas listas virtualizadas de Viaje (cien reservas manuales) y Documentos (cien incluidos), se abrió el detalle correcto de una tarjeta reciclada y se descargaron los documentos 001/100. El documento 100 se abrió en el visor PDF con el backend apagado. Estas acciones no incluyen adjuntar/mover adjuntos personales en la lista final. Descargar recompone Documentos y vuelve al principio; conservar ese scroll queda como mejora pendiente.

Las métricas Debug actuales no son una comparación controlada: Viaje pasó de 1.332 a 4.693 vistas y de 449.103 a 515.172 KB PSS tras recorrer la lista; Documentos, en ese proceso ya usado, de 1.842 a 2.378 vistas y de 604.483 a 582.263 KB PSS. No prueban ahorro por virtualización. Siguen pendientes perfiles Release con procesos recién abiertos, anuncios hablados de TalkBack, otros anchos, catálogo agotado/conflicto desde dos dispositivos físicos y las ediciones restantes de la matriz. El usuario solicitó no volver a usar huella; se autenticaron las cuentas sintéticas mediante PIN. La app habitual y sus datos se conservaron.

## Revisión posterior de la franja continua de fechas

Una fase local posterior reemplaza el selector de tres posiciones por una franja horizontal de todas las fechas: recorrerla no selecciona ni carga días, y activar un botón elige la fecha explícitamente. La [validación de la franja continua](day-planner-refinement.md#revisión-posterior-franja-continua-de-fechas-local-sin-push) registra la fixture de treinta días, 399 pruebas móviles, llamadas observadas, correcciones de altura/ancho al 200 % y el estado de la revisión nativa v124. El histórico v120 anterior conserva su cobertura; esta fase no amplía por sí sola las comprobaciones de TalkBack, Release, otros anchos ni las ediciones pendientes.

## Botones de Viaje, conversación y fotos de Journal

Esta fase modifica exclusivamente el cliente móvil. Viaje presenta botones con
borde y relleno de selección; su cabecera permite desplazamiento horizontal cuando
la fuente ampliada necesita más ancho. El modo de conversación del Asistente
organiza fecha, preguntas sugeridas, mensajes y un único compositor con envío por
icono o teclado. Las sugerencias rellenan el mensaje antes de enviarlo. Se
conservan comandos, contexto, permisos y menú secundario.

La franja negra de Documentos se reprodujo con una cuenta sintética incluso sin
documentos: el espaciador `BoxView` heredaba un `BackgroundColor` oscuro del estilo
global. Se reemplazó por un `Grid` transparente y se explicitó el fondo del
separador de hoteles. No se accedió a los documentos personales de la cuenta 3333.

El lector de Journal coloca miniaturas cuadradas después del texto y abre el visor
modal existente. «Añadir fotos» abre directamente el selector, conserva el texto,
la revisión y los cambios pendientes, y guarda los archivos cifrados localmente.
Si existe un borrador del mismo recuerdo, añade las referencias también a ese
borrador sin sustituir su contenido. Se conserva el límite de diez fotos y la
portada usada en otros contextos. Los fallos revierten las escrituras del lote;
si también falla la reversión del borrador se mantienen sus archivos recuperables.
El editor sitúa portada y eliminación en controles de 48 dp inmediatamente bajo
cada miniatura, y distribuye las acciones inferiores en columnas con etiquetas
ajustables al tamaño de fuente.

La suite móvil aprobó **414/414 pruebas**, sin omisiones
(`artifacts/ux-polish-20261005/mobile-tests.log`). Incluye 35 pruebas de
`JournalStore`: límites, concurrencia, conservación de notas/conflictos/borradores,
cambios de cuenta/viaje y fallos de almacenamiento. Las pruebas de lógica no
sustituyen la comprobación de XAML o de los selectores nativos. No se cambiaron
API, contratos ni esquema de base de datos.

Se utilizó el Samsung físico de 384 dp, una API local en 5188 con OpenAI desactivado
y datos sintéticos, y el paquete separado
`com.yuku.travelcompanion.plannerpaidreview`. Las revisiones v125/v126 conservaron
los datos de QA; la instalación habitual `com.yuku.travelcompanion.app` v116 se
mantuvo intacta. La autenticación de la cuenta sintética se hizo por PIN.

| Recorrido nativo | Resultado observado | Evidencia local |
|---|---|---|
| Viaje ES, tamaño normal | Alternar Itinerario, Carpeta y Gastos; selección con relleno y borde. | `trip-tabs-v125.png`, `trip-expenses-v125.png`, `trip-folder-v125.png`. |
| Documentos | Reproducción de la franja negra y desaparición tras actualizar QA, con el mismo estado vacío. | `documents-before.png`, `documents-after-v125.png`. |
| Journal: adjuntar y cancelar | El selector abre desde el lector; cancelar conserva la entrada. Seleccionar dos PNG sintéticos vuelve al lector con dos miniaturas y el mismo texto, sin editor. | `journal-picker-cancel-v125.png`, `journal-direct-photos-v125.png`. |
| Journal: abrir y eliminar | Miniatura abre modal; cancelar eliminación conserva la foto; confirmar elimina una; guardar y volver actualiza el contador. La actualización v126 conserva la foto restante. | `journal-photo-modal-v125.png`, `journal-editor-removed-v125.png`, `journal-reading-v126.png`. |
| Editor y portada | Controles situados a 8 dp de la imagen, con 48 dp de área táctil; portada seleccionada con contraste visible. | `journal-editor-v126.png`. |
| Asistente y teclado | Pregunta sugerida rellena y enfoca sin enviar; enviar desde teclado devuelve respuesta local; compositor visible con teclado; menú conserva agenda/preferencias/ayuda. | `assistant-question-empty-v125.png`, `assistant-keyboard-v125.png`, `assistant-answer-v125.png`, `assistant-menu-v125.png`. |
| EN y texto ampliado | Lista y lector de Journal en inglés al 200 %, sin pérdida de la nota ni de su foto. | `journal-list-en-200-v126.png`, `journal-reading-en-200-v126.png`. |
| Cabecera de Viaje al 200 % | Botones sin palabras partidas; desplazamiento horizontal hasta Gastos y selección de Gastos/Carpeta. | `trip-tabs-en-200-v128.png`, `trip-tabs-en-200-scroll-v128.png`, `trip-expenses-en-200-v128.png`, `trip-folder-en-200-v128.png`. |
| Editor al 200 % | Las tres acciones inferiores permanecen visibles; controles de portada/eliminación justo debajo de la miniatura. | `journal-editor-en-200-v128.png`, `journal-editor-photo-controls-en-200-v128.png`. |
| Sugerencias al 200 % | Se pueden recorrer las tres sugerencias completas, con ajuste de texto y compositor visible. | `assistant-suggestions-en-200-v128.png`. Los dos textos EN se ajustaron posteriormente para ser reconocidos por el clasificador. |
| Foto con conexión ADB retirada | La foto cifrada y el modal siguen disponibles al retirar la conexión ADB. La conexión se restableció después. | `journal-photo-modal-no-backend-en-v126.png`. Este recorrido no prueba por sí solo el cierre de conexiones HTTP ya abiertas. |
| Sugerencias definitivas EN | Los dos textos actualizados se leen completos al 200 %; elegir agenda y enviar desde el teclado devuelve la agenda del día, en inglés. | `assistant-starters-en-200-v129.png`, `assistant-starter-answer-en-200-v129.png`. |
| Sin API en v129 | Se detuvo la API local y se comprobó que 5188 ya no tenía listener. Journal abre desde otra pestaña con dos recuerdos y una foto; el lector y su modal cargan la foto local. | `journal-offline-list-es-v129.png`, `journal-reading-offline-es-v129.png`, `journal-photo-offline-es-v129.png`. |

La revisión al 200 % encontró palabras partidas en la cabecera de Viaje,
sugerencias truncadas y «Guardar» fuera de la pantalla en el editor. Se corrigieron
con desplazamiento horizontal, ajuste de texto y columnas, respectivamente.
Las comprobaciones de v128 en la tabla acreditan estas tres correcciones. La
revisión EN encontró además dos sugerencias sin señales reconocidas por el
clasificador existente. Se adaptaron sus textos; **62/62 pruebas de
`TravelChatIntentClassifierTests`** aprobaron, incluyendo las seis sugerencias
ES/EN, sin cambiar las reglas del backend. La comprobación nativa de v129 acredita
los dos textos actualizados y la respuesta de agenda.
No se acredita perfil Release, otros anchos, anuncios hablados de TalkBack
ni sincronización de fotos en nube: las fotos siguen siendo locales.

El cierre QA usa `artifacts/ux-polish-20261005/Yuku-Planner-QA-v129.apk`,
93.541.664 bytes, SHA-256
`3873266176D568C16C35D1AAF9F370CD774FD654F6C13081AFD9566AFE855DB6`.
La compilación Android terminó con cero advertencias y errores; se verificaron
paquete, versión y firma y se instaló con `-r`, conservando los datos QA.
Log: `android-build-v129.log`. Al terminar se restauraron la fuente a 1,0,
los locales de QA a `[]` y el ajuste de pantalla activa original; se retiró la
conexión ADB de 5188 y se detuvo únicamente la API creada para esta revisión.

### Publicación de la app habitual v117

El usuario autorizó después publicar en `main` e instalar la app habitual. Se
incrementó la versión Android a 117 y se ejecutó
`dotnet publish src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-android -c Release --no-restore --verbosity minimal -p:AndroidPackageFormat=apk -p:TravelCompanionApiBaseUrl=https://travelcompanion-api-57dw.onrender.com`.
La publicación terminó con exit 0; mantuvo las 203 advertencias XC0025 ya
registradas en v116. La compilación de bindings con `Source` sigue deshabilitada
según las instrucciones del proyecto. El atributo de ensamblado generado confirma
el backend habitual. No se requieren migraciones ni cambios de configuración.

La APK `artifacts/ux-polish-20261005/YUKU-Japan-117-ux.apk` mide 40.067.701 bytes;
SHA-256 `623DE1950E5FC95976F4695967D957F3C455FF455D3F8B01B9B2436EF7FD7759`.
Se verificaron versión 117, paquete `com.yuku.travelcompanion.app`, ARM64/x86_64,
ausencia de marca debuggable y firma v1/v2/v3. El certificado SHA-256
`d69a6cf0fcd1a96867f619c3222b2d1e936db07637c275c306a0de1e88f50cf2` coincide
con la APK extraída de la instalación habitual justo antes de actualizarla.

`adb -s R5GL84VBTWJ install --no-incremental -r` devolvió `Success`; Android
confirma versión 117 y conserva `firstInstallTime=2026-09-25 18:48:34`.
El arranque de la actividad devolvió `Status: ok`. No se desinstaló ni se borraron
datos personales. La revisión detallada de las pantallas corresponde a QA con
datos sintéticos; en la instalación habitual se comprobó actualización y arranque.
Logs y metadatos: `android-publish-v117.log`, `badging-release-v117.log`,
`signature-release-v117.log`, `install-release-v117.log`, `start-release-v117.log`
en `artifacts/ux-polish-20261005/`.

## Barra discreta de Viaje, fotos en abanico y documentos pendientes

La cabecera de Viaje reúne Itinerario, Carpeta y Gastos en un único contenedor
suave. La selección usa una superficie clara y texto destacado; se conserva el
desplazamiento horizontal para fuentes grandes. El menú secundario elimina los
accesos duplicados a Carpeta y Documentos, conservando las demás acciones y sus
permisos. Las categorías pendientes de Carpeta muestran un triángulo amarillo
suave; archivos personales o declaraciones manuales restauran el icono habitual.

Journal presenta hasta tres fotos en abanico, con la portada elegida delante y
algo más alta. Tocar las fotos abre directamente el álbum completo; tocar el texto
abre el recuerdo. El lector carga una única miniatura cuadrada después del texto,
que también abre el álbum. Se mantienen las diez fotos, los índices originales,
el almacenamiento cifrado y la lista virtualizada. Una guardia compartida evita
navegaciones simultáneas desde texto y fotos; el contexto de cuenta y viaje debe
seguir siendo el de la lista mostrada.

La revisión nativa detectó que Preparación almacenaba decisiones por idioma.
Ahora utiliza una clave neutral por usuario y viaje. La primera lectura recupera
las copias anteriores de todos los idiomas, conserva la última declaración por
categoría y el indicador de importación de checks antiguos, y escribe el resultado
cifrado. Los pendientes sin fecha de las versiones anteriores no se distinguen
de los valores iniciales: se tratan como valores iniciales durante esa recuperación.
Las decisiones futuras de volver a pendiente conservan fecha y quedan protegidas
frente a la importación de checks. Un estado neutral existente es la autoridad;
las copias antiguas se conservan y la limpieza de cuenta/viaje cubre ambos formatos.

La suite móvil aprobó **434/434 pruebas**, sin omisiones
(`artifacts/ux-minimal-20261005/mobile-tests-final.log`). Incluye selección de
portada, límite de previsualizaciones, estados y contadores de documentos,
recuperación entre idiomas, decisiones posteriores, cancelación, aislamiento y
limpieza. Los tests de almacenamiento utilizan la dependencia de caché simulada;
la recuperación de los archivos cifrados reales se comprueba en el Android.

Se revisó el Samsung físico de 384 dp, con una cuenta y archivos sintéticos,
API local en 5188 y el paquete separado de QA. No se cambiaron API, contratos,
permisos de pago ni esquema PostgreSQL. La autenticación de QA se hizo por PIN.

| Recorrido nativo | Resultado observado | Evidencia en `artifacts/ux-minimal-20261005/` |
|---|---|---|
| Viaje ES normal | Barra agrupada y selección de Carpeta/Gastos; menú sin accesos duplicados. | `trip-tabs-es-v130.png`, `trip-expenses-v130.png`, `trip-menu-v130.png`. |
| Documentos pendientes | Triángulo suave; adjuntar un PNG cambia a documento, borrar el último devuelve el aviso; declaración fuera de la app también restaura el icono. | `folder-pending-es-v130.png`, `folder-file-saved-v130.png`, `folder-return-pending-v130.png`. |
| Álbum desde la lista y lector | Abanico abre portada 3/3; navegación hasta 2/3 y 1/3; cerrar vuelve directamente a la lista. El lector muestra una miniatura y abre las tres fotos. | `journal-fan-modal-v130.png`, `journal-modal-second-v130.png`, `journal-modal-first-v130.png`, `journal-reader-single-es-v130.png`. |
| EN al 200 % | Barra horizontal hasta Expenses, selección funcional; lector con una miniatura y controles visibles. | `trip-en-200-scroll-v130.png`, `trip-expenses-en-200-v130.png`, `journal-reader-en-200-v130.png`. |
| Abanico final y una foto | Márgenes evitan recorte de esquinas; una foto se muestra sola. Apertura rápida, cierre a la lista y nueva apertura funcionan. | `journal-fan-en-200-v131.png`, `journal-one-modal-v131.png`, `journal-after-fast-taps-v131.png`, `journal-reopen-v131.png`. |
| Recuperación cifrada en Release | QA132 recupera Transporte «No lo necesito» de EN y Alojamiento «Fuera de la app» de ES: 2/4 organizadas. | `folder-recovered-es-v132.png`. |
| Decisión posterior y reinicio | Cambiar Alojamiento a pendiente en ES y reiniciar en EN mantiene 1/4, Transporte organizado y Alojamiento pendiente; la copia antigua ES no resucita la declaración. | `folder-new-pending-es-v132.png`, `folder-new-pending-en-restart-v132.png`. |
| Dos y tres fotos en Release | Añadir la segunda foto desde el lector vuelve a él, con una sola miniatura. La lista muestra los dos abanicos y mantiene la portada seleccionada. | `journal-reader-two-v132.png`, `journal-fans-two-three-v132.png`. |
| Sin backend en Release | API local detenida y puerto 5188 sin listener: desde Carpeta se abre Journal con dos recuerdos y cinco fotos; el abanico abre el visor y el texto abre el lector con una sola miniatura. | `journal-offline-list-en-v132.png`, `journal-offline-modal-en-v132.png`, `journal-offline-reader-en-v132.png`. |

Las compilaciones Android Debug de QA130 y QA131 terminaron sin advertencias ni
errores. No se acredita TalkBack hablado ni otras plataformas; la revisión con
datos personales se limita a instalación y arranque de la app habitual.

### Publicación de la app habitual v118

QA132 y la app habitual se publicaron en Release con exit 0. Se mantienen 205
advertencias XC0025 de bindings con `Source`, cuya compilación está deshabilitada
por las instrucciones del proyecto; v117 tenía 203 y esta fase añade dos bindings
de navegación del abanico. Su funcionamiento se comprobó en las APK nativas.
Los registros son `android-publish-qa-v132.log` y `android-publish-v118.log`.

QA132 usa `Yuku-Planner-QA-v132.apk`, SHA-256
`D5B9E51DF2906F07E4413649EA6DF49251BC262DA36E698A752C613749F425AB`.
La APK habitual `YUKU-Japan-118-ux.apk` mide 40.129.597 bytes, SHA-256
`C57E241ABEDF51CD8CF9F38DEC2DE0BD297D3550028E64ADD29F610581346C3F`.
Se verificaron paquete `com.yuku.travelcompanion.app`, versión 118, ARM64/x86_64,
ausencia de marca debuggable, firma v1/v2/v3 y el mismo certificado que v117.
El atributo de ensamblado confirma el backend habitual de Render.

La instalación con `adb install --no-incremental -r` devolvió `Success`; Android
confirma versión 118 y conserva `firstInstallTime=2026-09-25 18:48:34`.
El arranque devolvió `Status: ok`. No se desinstaló ni se borraron datos personales.
Se restauraron fuente 1,0, locales QA `[]` y pantalla activa original 2; se retiró
la conexión ADB de 5188 y se detuvo solo la API local de esta revisión. No se
requieren migraciones ni despliegue de backend para estos cambios.
