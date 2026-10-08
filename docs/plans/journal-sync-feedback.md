# Guardado y estado de sincronización de Journal — 8 de octubre de 2026

Referencia: capturas 73816 y 73818, cambios locales sobre `main` en `fbe32a7`.
La revisión inicial encontró que el editor guarda localmente, muestra siempre
«Pendiente de sincronizar» y no solicita sincronización al confirmar. El editor
y la lectura no observan su finalización. No se consultó producción; un posible
error HTTP o de despliegue no está demostrado por las capturas.

| ID | Criterio independiente | Flujo y revisión prevista | Estado |
|---|---|---|---|
| journal-sync-01 | Guardar confirma primero la persistencia local y solicita sincronización del texto en segundo plano, sin esperar HTTP ni generar una cola por cada pulsación. | `SaveConfirmedAsync` conserva el bloqueo local breve; el editor solicita un runner por contexto después de confirmar. Solicitudes nuevas se agrupan en un ciclo posterior, incluso al reconectar tras fallo. | Implementado; fuente revisada |
| journal-sync-02 | Editor y lectura distinguen guardado local, sincronización, confirmación, falta de conexión y fallo; ofrecen reintento cuando corresponde. | Eventos observados sólo durante la pantalla visible, recargas locales y recursos ES/EN. La lista descarta snapshots anteriores mediante una generación común. | Implementado; fuente revisada |
| journal-sync-03 | La confirmación remota no sobrescribe edición/borrador nuevos ni oculta conflictos; las respuestas de otro contexto se descartan. | Identidad de mutación y revisión confirmadas, edición durante HTTP, guardados consecutivos sin observar cada evento, borrador recuperado y conflicto sobrevenido. Guardas de contexto antes de persistir y presentar. | Implementado; fuente revisada |
| journal-sync-04 | Se conserva acceso gratuito y almacenamiento local de fotos/borradores; el mensaje de sincronización describe sólo texto/datos remotos. | Autorización existente sin cambios; fotos/borradores permanecen en almacenamiento cifrado local. Nota de almacenamiento conservada; estados y reintento ES/EN. | Implementado; fuente revisada |
| journal-sync-05 | Los errores quedan diagnosticados sin parámetros, texto del recuerdo ni credenciales; no se declara un fallo de producción sin evidencia. | Token ausente, respuesta nula, HTTP y timeout producen estado recuperable. Diagnóstico existente registra tipo/HResult/frames y código HTTP; excluye mensajes, URLs, payloads y credenciales. | Implementado; fuente revisada |

Por indicación vigente del usuario, no se ejecutan pruebas automatizadas ni
pruebas nativas. Se registran por separado revisión de fuentes y compilación
Android. Sin push, instalación, migraciones ni cambios en producción.

## Revisión final de implementación

Se revisaron editor, lectura, lista, persistencia y los puntos de entrada a la
sincronización. No quedaron pendientes conocidos de implementación en los cinco
criterios. Esto describe una revisión de fuentes; no certifica ejecución ni
comportamiento nativo.

El índice cifrado añade un campo opcional `Acknowledgement` con identificador de
mutación y revisión, escrito sólo tras una confirmación remota propia. Los
registros anteriores no requieren ese campo. Permite reconocer una cadena de
guardados propios aun cuando el editor no observa los eventos intermedios; no
autoriza adoptar una revisión ajena. `SaveAsync` mantiene su resultado `Task`;
`SaveConfirmedAsync` devuelve internamente la mutación local realmente persistida.
No cambian contratos HTTP, DTOs compartidos ni PostgreSQL.

Las fusiones conservan mutaciones nuevas, fotos y borradores; guardar tampoco
borra un conflicto recibido entre la lectura del editor y la escritura local.
La resolución explícita sigue siendo necesaria. Cambiar usuario o viaje invalida
las operaciones anteriores. Los nuevos metadatos forman parte del índice ya
incluido en limpieza de cuenta/viaje y vinculación de cuentas.

No se consultó la API ni los logs de producción. Un posible error adicional de
servidor, sesión o despliegue permanece sin demostrar. Los nuevos estados y
diagnósticos permiten distinguirlo de un guardado local o una sincronización
todavía activa.

## Evidencia de compilación

Instantánea antes de compilar: `artifacts/journal-sync-20261008/source-snapshot.json`,
18 archivos, base `fbe32a701342e33c556b06fc3b1c538ff366909b`. Incluye las fuentes
actuales de Journal y los ajustes locales anteriores de Mañana y Sorpréndeme.
SHA-256 del manifiesto:
`8EBCDADB70EAFFC2BF2F311E7EE51262BD1357968EA27C529DBEDC40DFF15482`.

Comando:

```powershell
rtk dotnet build src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-android -c Release --artifacts-path artifacts/release149-20261007/android-clean --verbosity minimal -p:RuntimeIdentifiers=android-arm64 -p:NuGetAudit=false -p:TravelCompanionDiagnosticsEnabled=false
```

Compilación Android Release ARM64 completada con código de salida 0: dos
proyectos, cero errores y 207 advertencias registradas. El resumen muestra
advertencias de bindings con `Source`; no se cambió esa configuración. Tiempo
informado: 49,50 segundos. Los 18 hashes de fuentes permanecieron iguales tras
compilar y la revisión final `git diff --check` terminó sin errores.

Registro local en `artifacts/journal-sync-20261008/android-build.log`, SHA-256
`4FF8BEDA5F4BEE747C6EC24CD2B652E1F0691C5FC391E5BC24B9167B37052280`;
código de salida en `android-build.exit`. Copia de la APK compilada en
`artifacts/journal-sync-20261008/Yuku-local-journal-v152.apk`. Es un artefacto
local de compilación con la versión actual del proyecto; no se instaló ni publicó.
SHA-256 de la APK:
`F3E7309CCD9DEA1C41B3A08C536843EA730DB4A2ED3C21B790FAD81775398E64`.

Pruebas automatizadas, conexión a API y revisión nativa no ejecutadas por
indicación del usuario. No se reutilizan resultados anteriores como evidencia
de estas fuentes. La matriz separa implementación y revisión de fuentes de
esa cobertura funcional todavía no ejecutada.

Tras añadir el sorteo de Sorpréndeme y sus textos ES/EN, se compiló de nuevo el
conjunto actual: Android Release ARM64, salida 0, cero errores. La instantánea de
18 fuentes y la APK exacta están registradas en
`docs/plans/express-surprise-polish.md` y `artifacts/express-random-20261008`.
La evidencia anterior conserva su alcance histórico; no se ejecutaron pruebas.
