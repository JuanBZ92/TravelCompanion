# Entrega R12, R13 y R14 — 15 octubre 2026

Implementación sobre `21827cd`, sin push, despliegue ni cambios de producción. La
[matriz](plans/roadmap-2026-2027.json) conserva 15 criterios verificables y separa
código, pruebas automáticas y cobertura nativa. El resto del roadmap no forma parte
de esta entrega; [R07](roadmap-r07-collections.md) se explica, sin implementarlo.

## Cambios disponibles

- **R12:** Viaje → menú → Calendario del viaje. Exportar las actividades elegidas
  mediante ICS; importar un archivo, revisar eventos compatibles y confirmar cuáles
  añadir. Identidad persistente por evento, propietario y viaje: reimportar conserva
  el original. Recurrencias y adjuntos externos se explican sin ejecutarse. Los
  fallos de transporte o respuestas no confirmadas pausan el lote y conservan los
  pendientes; no retroceden la revisión del viaje.
- **R13:** A continuación y reservas con horario → Llegar a tiempo. Elegir ubicación
  actual, hotel o actividad anterior elegible, transporte y margen. Mantener la
  reserva y Maps ante errores. Descartar respuestas tardías o de otro contexto y
  rechazar estimaciones incompatibles con un backend anterior. No garantiza llegada.
- **R14:** Instrumentación opcional, ejecutores reproducibles y comparación sobre
  Android real. La apertura local de Journal evita leer y descifrar dos veces el
  mismo índice; la sincronización conserva la relectura necesaria tras fusionar.
  No se añadieron índices, cachés, proveedores ni recursos.

Detalles: [R12](plans/r12-calendar.md), [R13](plans/r13-routes.md) y
[mediciones R14](roadmap-r14-performance.md).

## Resultado medido

Samsung SM-S948B, Android Release ARM64, backend PostgreSQL local por USB, 250
recuerdos, misma cuenta y viaje. Cinco calentamientos y treinta repeticiones por
operación; ambas variantes comparten las fuentes runtime salvo el override
documentado de la doble lectura anterior de Journal.

| Journal local | Inicial limpia | QA160 |
|---|---:|---:|
| p50 | 154,27 ms | 76,08 ms |
| p95 | 184,52 ms | 123,76 ms |
| Lecturas del índice | 2 | 1 |
| Bytes JSON leídos | 2.283.060 | 1.141.530 |
| Asignaciones p50 | 18.725.240 B | 9.574.072 B |

También se midieron bootstrap, Viaje y generación de propuestas. Se investigó un
aumento inicial de p95 en planificación; una repetición completa no lo reprodujo
(30,12 ms frente a 34,87 ms iniciales). La causa de los dos intervalos altos sigue
sin demostrarse. No se atribuyen mejoras a SQL ni a infraestructura. Arranque frío
de QA160: espera p50/p95 357/383 ms; primer dibujo 356/382 ms. No equivale a login,
red o pantalla completamente utilizable, ni demuestra rendimiento de producción.

## Validación del código final

- Mobile: **721** pruebas aprobadas, sin omitidas ni fallos.
- Shared: **109** aprobadas, sin omitidas ni fallos.
- API: **27** pruebas afectadas aprobadas; no se presenta como suite API completa.
- Android Release ARM64 QA160: compilación aprobada; 207 avisos XamlC existentes
  sobre bindings `Source` deliberadamente no compilados por la configuración del
  proyecto. No se cambió esa política.
- iOS ARM64: compilación C# aprobada, sin errores ni advertencias. No se ha generado
  ni probado un archivo nativo firmado: requiere Mac/Xcode y dispositivo/simulador.
- Comparación final de fuentes: los 841 archivos runtime y de tests del manifiesto
  siguen sin cambios tras generar QA160. Las huellas de cierre incluyen además
  fuentes, herramientas y matriz; no se reutiliza una APK intermedia como final.

La APK intermedia QA156 empaquetó una DLL Shared antigua al usar el plural
`RuntimeIdentifiers` con salida aislada. Se descartó como evidencia final, se
recompiló con `-r android-arm64` en una carpeta limpia y se verificó Calendario en
el dispositivo. La baseline limpia se reconstruyó con Shared actual; el informe
conserva los intentos anteriores como historia, sin mezclarlos en la comparación.

## Cobertura Android realmente ejecutada

QA160 se instaló sobre la instalación QA existente, conservando su fecha inicial
del 5 de octubre y sus datos. La app habitual no se actualizó ni se borró.

- Calendario ES/EN: navegación, selección, confirmación de exportación y selector
  Android con un ICS adjunto; se canceló sin enviarlo a otra persona o servicio.
- Selector nativo, vista previa de tres eventos válidos y dos incompatibles,
  UTC/Japón y hora flotante; confirmación e importación al PostgreSQL sintético.
  El viaje pasó de 11 a 14 filas. Reimportar los tres mantuvo 14 filas y tres eventos
  importados. Cancelar el selector conservó el estado.
- Rutas ES/EN: destino y zona, hotel/ubicación actual como opciones disponibles,
  modos a pie/transporte/coche, margen de 10 a 15 minutos, scroll, error del
  proveedor QA desactivado, detalle y apertura real de Maps con hotel y transporte
  público. No se inició navegación. No se pidió GPS real al seleccionar el origen.
- Los cuatro escenarios de rendimiento, su repetición de planificación y el arranque
  frío se vinculan a las APK y manifiestos por SHA-256.
- Fuente 1.0, densidad 600, modo oscuro activado y rotación automática 1 permanecen
  iguales. El idioma se cambió sólo para QA y se restauró a su valor original `[]`.
  Sin TalkBack ni autenticación biométrica. Se retiró el archivo ICS sintético de
  Descargas al finalizar.

No se certifican offline real, desconexión a mitad del lote, cambios de cuenta
durante el selector, revocación en UI, GPS real, presentación nativa del origen
anterior o una estimación del proveedor real. El intento de retirar el túnel USB
conservó una conexión HTTP ya abierta y **no acredita pérdida de red**. La lógica
de permisos, contexto y fallbacks sí tiene las pruebas y revisión identificadas
en la matriz. Fuentes ampliadas 150/200 %, otros anchos e iOS nativo siguen sin
comprobar; no se alteraron ajustes globales para simularlos. TalkBack está excluido
por petición del usuario, no programado como pendiente.

## Artefacto y reproducción

Raíz local ignorada por Git: `artifacts/roadmap-r12-r14-20261015`.

- APK: `Yuku-QA160.apk`, paquete `com.yuku.travelcompanion.plannerpaidreview`.
- SHA-256: `DCA51E1298C3F38F7F7E13B40402C0351D27AE2FFEAB4EC0BC436E9410418C37`.
- Manifiesto: `qa160-source.json`; cobertura nativa: `native-qa160.json`.
- Tests: `mobile-results/mobile-closure159.trx`,
  `shared-results/shared-closure.trx`, `api-results/api-closure.trx`.
- Builds: `android160-build.log`, `ios-closure160.log`.
- Evidencia consolidada: `closure-evidence160.json`. El comprobador normal verifica
  código y evidencia; `-RequireNativeChecks` debe devolver 2 mientras haya cobertura
  nativa pendiente. No convierte una limitación en aprobación.

La revisión usa backend local en `127.0.0.1:5189` por `adb reverse`, exclusivamente
con datos sintéticos. No es una APK para distribuir a clientes. El PIN de prueba
y la receta quedan en los artefactos locales; no se incorporan al código ni a la
APK. Los procesos locales se identifican en `api-runtime.json` y no se modificó
la configuración de infraestructura de producción.

```powershell
dotnet build src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj `
  -f net10.0-android -c Release -r android-arm64 `
  --artifacts-path artifacts/r1214-clean --verbosity minimal `
  -p:TravelCompanionDiagnosticsEnabled=true `
  -p:TravelCompanionApiBaseUrl=http://127.0.0.1:5189 `
  -p:ApplicationId=com.yuku.travelcompanion.plannerpaidreview `
  '-p:ApplicationTitle=Yuku Planner QA' -p:ApplicationVersion=160
```

Usar RID singular y una carpeta nueva si hay salidas antiguas. No precompilar ni
copiar manualmente una DLL Shared RID para salvar errores de empaquetado.

## Publicación posterior y reversión

1. Completar las comprobaciones nativas aplicables de la matriz antes de publicar
   cada plataforma. Validar también proveedor real y cuotas con un viaje propio.
2. Publicar API/Shared primero: R13 añade campos opcionales al endpoint existente.
   Los clientes antiguos conservan hotel/GPS y margen anterior. **Sin migraciones**.
3. Generar el móvil habitual con URL de producción, diagnósticos QA desactivados,
   identidad y firma habituales, incrementando su versión correspondiente. No
   distribuir la APK local anterior como release de producción.
4. Comprobar origen/modo/margen, importación y reimportación; revisar errores y cuota
   del proveedor. Las mediciones locales no justifican cambiar recursos o índices.
5. Revertir el móvil conserva compatibilidad con API nueva. Si API vuelve primero
   a una versión anterior, el móvil descarta estimaciones de origen/margen distintos
   y mantiene Maps. Los eventos importados siguen siendo actividades normales;
   revertir binarios no los borra ni deshace decisiones del usuario.

No se ejecutó ninguno de esos pasos de publicación en esta entrega.
