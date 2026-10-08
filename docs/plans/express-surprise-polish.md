# Sorpréndeme en Un plan para ahora — 8 de octubre de 2026

Referencia: captura 73812; cambios locales sobre `main` en `fbe32a7`.
El botón actual selecciona Comida, Relajar y Pasear de manera fija. El objetivo
es dejar abierta la elección de intereses y presentar la acción con un detalle
de misterio sobrio, coherente con los recursos editoriales existentes.

| ID | Criterio | Flujo y revisión prevista | Implementación |
|---|---|---|---|
| surprise-01 | Sorpréndeme no impone las tres categorías actuales ni presenta una personalización inexistente. | Elección, petición y filtros/ranking existentes del backend. | Implementado; revisión de fuentes realizada. |
| surprise-02 | El usuario puede volver a elegir intereses manuales; reintentos y cambios de sesión/viaje conservan sus protecciones. | Intereses, continuar, atrás, editar búsqueda, reinicio y petición. | Implementado; revisión de fuentes realizada. |
| surprise-03 | La acción se distingue con misterio sutil sin competir con Continuar; mantiene texto, tamaño táctil de 48 dp y estados de carga. | XAML, recursos existentes, textos largos y fuentes ampliadas a nivel de código. | Implementado; revisión de fuentes realizada. |
| surprise-04 | Textos nuevos disponibles en español e inglés; no se cambian permisos, cuotas ni contratos. | Recursos y rutas de búsqueda existentes. | Implementado; revisión de fuentes realizada. |

Por indicación vigente del usuario, no se ejecutan pruebas automatizadas ni
pruebas nativas. Se registrará por separado el resultado de compilación Android
y cualquier limitación real. Sin push, instalación ni cambios en producción en
esta tarea.

## Resultado y revisión de fuentes

La selección de sorpresa es independiente de las categorías manuales. Con
intereses guardados, vacía sus checks y continúa a la elección de zona sin imponer
intereses. Elegir una categoría manual desactiva la sorpresa. Una nueva búsqueda
o el reinicio de sesión limpian el estado. Sin intereses guardados se aplica el
sorteo descrito en la ampliación al final del documento.

Con intereses guardados, la petición envía `Category = null` y las nueve categorías válidas de los
intereses actuales en `Categories`. El normalizador existente del backend
conserva la categoría principal vacía para esta búsqueda amplia y utiliza el
modo equilibrado. Las categorías se combinan con OR; siguen aplicándose el
perfil guardado, permisos, cuotas, ventana, ubicación y exclusiones. En ese modo
no se añade azar ni se garantiza que cada búsqueda produzca resultados diferentes.

La tarjeta utiliza `AccentSoft`, `EditorialLine`, un título serif, ayuda breve,
una carta con interrogante y una flecha. Al volver a intereses, la selección
se identifica con borde y check. Un único botón cubre la tarjeta y conserva
el tamaño mínimo de 48 dp, descripción localizada y deshabilitación durante
operaciones. Los textos pueden ocupar varias líneas dentro del scroll actual.
La acción principal inferior sigue siendo Continuar.

Se revisaron las rutas de volver, editar, selección manual, reinicio, petición,
reintento, cancelación, error offline y aislamiento de cuenta/viaje en las
fuentes. Una segunda revisión no encontró defectos materiales. Esto no acredita
ejecución en Android, fuentes ampliadas ni revisión visual nativa.

## Compilación

Compilación Android Release ARM64 completada: código de salida 0, dos proyectos,
cero errores y 207 advertencias registradas. El registro contiene advertencias
de bindings existentes con `Source`; no se modificó esa configuración. Duración
informada por el ejecutor: 2 minutos y 47,25 segundos.

Comando:

```powershell
rtk dotnet build src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-android -c Release --artifacts-path artifacts/release149-20261007/android-clean --verbosity minimal -p:RuntimeIdentifiers=android-arm64 -p:NuGetAudit=false -p:TravelCompanionDiagnosticsEnabled=false
```

Registro local: `artifacts/express-surprise-20261008/android-build.log` y código
de salida en `android-build.exit`. La instantánea de las fuentes se registra en
`source-snapshot.json` dentro del mismo directorio. Incluye los ajustes previos
de Mañana a un vistazo y sus textos actuales; no se reutilizan pruebas antiguas.

Los nueve archivos de la instantánea conservaron sus hashes al terminar la
compilación. SHA-256 del manifiesto:
`6F01E6D548F16513B2ABFF2BE14D521EAAE6BBE87721A193CA5F3D2A82A392B3`.
SHA-256 del registro:
`1622E7A6514BA5716FBE6F3BCFACA20A67C89CFB4212A2E94B4111F7A90CDD3B`.

Comparación final con los cuatro criterios: sin pendientes conocidos de
implementación; revisión de fuentes y compilación realizadas. No se ejecutaron
pruebas automatizadas ni revisión nativa, por indicación del usuario. El aspecto
en el dispositivo y los recorridos ejecutados no se certifican desde las fuentes.

Los recursos ES/EN volvieron a cambiar para los estados de Journal. La
compilación conjunta de las fuentes actuales, con hashes nuevos, se registra en
`docs/plans/journal-sync-feedback.md` y `artifacts/journal-sync-20261008`:
Android Release ARM64, salida 0, cero errores. La instantánea anterior continúa
describiendo su propio estado; no acredita los textos posteriores de Journal.

## Ampliación: sorpresa sin preferencias guardadas

Petición posterior del usuario: elegir intereses al azar cuando no haya
preferencias, sin obligarle a seleccionar antes. El backend ya admite Express
guiado sin perfil mínimo; el sorteo se aplica únicamente a esta búsqueda.

| ID | Criterio independiente | Flujo y revisión | Estado |
|---|---|---|---|
| surprise-random-01 | Sin intereses guardados del usuario activo, Sorpréndeme sortea un interés válido sin selección manual previa. | `Random.Shared.Next` sobre las nueve categorías válidas y distintas; selección visible, `Category` y `Categories=[sorteada]`, continuación directa a zona. | Implementado; fuente revisada |
| surprise-random-02 | Con intereses guardados, conserva la búsqueda amplia y ranking personalizado actuales. No guarda el interés sorteado en el perfil. | Perfil y caché del usuario actual; categoría principal vacía y las nueve categorías. El sorteo permanece sólo en el ViewModel. | Implementado; fuente revisada |
| surprise-random-03 | Reintentar o reemplazar mantiene el interés sorteado; volver a tocar Sorpréndeme sortea de nuevo. Elegir manualmente o cambiar cuenta/viaje limpia la sorpresa. | Campo temporal establecido sólo al tocar Sorpréndeme; reintento conserva petición y operación. Selección manual y reset limpian ambos campos de sorpresa. | Implementado; fuente revisada |
| surprise-random-04 | Ayuda ES/EN fiel al modo, permisos/tiempo/exclusiones intactos; revisión de fuentes y compilación separadas de pruebas. | Ayuda distinta para azar y perfil guardado. Backend sin cambios; comprobaciones existentes de acceso, ventana y duplicados conservadas. | Implementado; fuente revisada; Android compilado |

Se mantiene la indicación de no ejecutar pruebas automatizadas ni nativas.
Sin push, instalación ni cambios de producción para esta ampliación.

Se sortea un interés por nueva pulsación; una repetición casual del mismo interés
es posible. Las alternativas conservan ese interés y siguen excluyendo los
lugares ya propuestos. No se sortean ni guardan presupuesto, restricciones
alimentarias o límites de acceso. Un perfil sin intereses, aunque tenga otros
campos, utiliza el sorteo; los demás campos siguen entrando en el ranking del
backend. La caché de otra cuenta nunca se considera un perfil válido.

El backend mantiene el ranking determinista del catálogo autorizado. Su flujo
guiado no exige `HasMinimumPreferences`; por eso no se añade una pantalla para
definir preferencias ni una petición extra para guardarlas.

Instantánea conjunta de fuentes: `artifacts/express-random-20261008/source-snapshot.json`,
18 archivos, SHA-256
`7BF721D116950A39A3C677D11AB0867B77E80A452382F3D39FE03A124C92FB6B`.
Compilación Android Release ARM64 completada con el comando anterior: salida 0,
dos proyectos, cero errores y 207 advertencias; duración informada de 49,14
segundos. Los 18 archivos conservaron sus hashes al finalizar. Registro:
`artifacts/express-random-20261008/android-build.log`, SHA-256
`8EE3BF7961163CAE932F3622F39238EAA7E10676C5AF18AA27F60ED3AD73045E`.
Código de salida en `android-build.exit`. Copia local de la APK v152:
`artifacts/express-random-20261008/Yuku-local-express-random-v152.apk`, SHA-256
`8DF39532B8C0034EA1EB742AFAB54B0FA2EA21C4E07F5650657711E9E54303D7`.
No se instaló ni publicó.

La comparación final y una revisión independiente de fuentes no encontraron
pendientes conocidos de implementación en estos cuatro criterios. Esto no
acredita pruebas funcionales ni ejecución nativa, excluidas por el usuario.
