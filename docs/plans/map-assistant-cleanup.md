# Ajustes de Mapa y Asistente — 8 de octubre de 2026

Alcance: dos cambios de presentación solicitados después de QA150. Se conservan
los cambios anteriores, permisos, contratos y ajustes del dispositivo.

| ID | Criterio | Flujo y comprobación | Implementación |
|---|---|---|---|
| map-text-01 | Mapa muestra el texto autorizado completo, sin Ver detalles/Mostrar menos. | Vista previa normal y gratuita: texto sin límite de líneas, desplazable; datos completos, offline y cambios de selección conservan permisos. Revisar fuentes, pruebas existentes y compilación Android. | Implementado; fuente, pruebas existentes y Android comprobados. |
| assistant-star-02 | Quitar únicamente la estrella sobre ¿Qué necesitás saber? y su espacio decorativo. | Estado vacío del modo preguntar; conservar otros iconos. Revisar XAML y compilación Android. | Implementado; XAML y Android comprobados. |

La revisión nativa permanece pendiente: el usuario indicó continuar sin teléfono.
QA150 y su cierre acreditan el snapshot anterior, no estos ajustes posteriores.
Sin push, instalación o cambios en producción en esta tarea.

## Revisión final

`MapPage.xaml` y `FreeMapPage.xaml` muestran título, descripción y metadatos sin límite
de líneas, dentro del scroll existente. Se retiraron el botón, la fila vacía y el estado
de expansión de ambos ViewModels. El mapa normal recupera automáticamente el detalle
al seleccionar un lugar autorizado; conserva caché, cancelación al cambiar de lugar y
comprobación de cuenta/viaje. Revalida acceso vigente antes y después de HTTP, también
si el permiso vence durante la petición. Offline conserva el texto local disponible.
El mapa gratuito sigue ocultando el texto de lugares bloqueados.

En `TravelChatPage.xaml` se retiró sólo la decoración con estrella sobre la pregunta;
los demás iconos, acciones y cambios del recorrido exprés permanecen intactos. No se
introdujeron textos ni cambios de permisos, contratos, API o PostgreSQL.

- Suite móvil actual: **695/695**, sin fallos, omisiones ni avisos. La suite existente
  incluye el mapa gratuito; las guardas de hidratación del mapa normal se revisaron en
  fuente y se compilaron, sin afirmar una nueva prueba automática específica.
- Los tres XAML son válidos; `git diff --check` no encuentra errores.
- Android Release QA151 compiló sin errores. Permanecen 207 avisos XC0025 de bindings
  con `Source`. Package de revisión, ARM64, API local `http://127.0.0.1:5188` y firma
  compatible; no se instaló.
- APK: `artifacts/map-assistant-cleanup-20261008/Yuku-QA-v151.apk`, 25.131.459 bytes.
  SHA256: `AB5B7BBE4B2047DD58E9F5DA040807AAAA158590617FCE35643F1D05FEFCDE60`.
- Evidencia actual: `apk-proof151.json`, inventarios antes/después, TRX y logs en ese
  mismo directorio ignorado por Git. Las 354 entradas Mobile/Shared de la APK no
  cambiaron durante la compilación final.

Pendiente nativo: revisar textos largos y desplazamiento de ambas vistas de Mapa,
cambio rápido de lugar, regreso y estado vacío del Asistente, en ES/EN y con fuentes
ampliadas mediante override sólo de QA. Sin TalkBack, biometría ni cambios globales.
La APK requiere el backend local revisado; QA150 se conserva como artefacto anterior.
