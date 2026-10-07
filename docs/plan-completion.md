# Cierre de planes con evidencia

El objetivo es terminar el trabajo autorizado en una misma ejecución, sin depender de
que el usuario solicite sucesivas revisiones. Una suite verde no demuestra que se haya
implementado todo un plan: sólo demuestra los comportamientos que esas pruebas ejercitan.

## Procedimiento

1. Conservar el alcance aceptado. Descomponerlo en criterios independientes con ID
   estable, condición de aceptación, fuente de producción y comprobación concreta.
   Incluir los accesos desde la interfaz y los permisos, no sólo servicios auxiliares.
2. Diseñar la comprobación al abordar cada criterio. Para cambios de comportamiento,
   cubrir el fallo original y la recuperación. Revisar modos gratuitos, de pago,
   revocados y expirados cuando afecten al permiso; cambios de cuenta/viaje, cancelación
   y datos antiguos cuando afecten a persistencia u operaciones asíncronas.
3. Implementar y validar cada criterio. Distinguir código pendiente, pruebas automáticas,
   revisión de fuente y comprobación nativa. Una captura antigua sólo acredita la
   versión y el estado que muestra; no certifica cambios posteriores.
4. Hacer una revisión final independiente sobre los mismos criterios. Corregir todas
   las omisiones conocidas dentro del alcance antes de entregar. Una mejora nueva que
   no sea necesaria para cumplir el criterio se registra aparte y no prolonga el cierre.
5. Cuando el código quede estable, capturar su huella, ejecutar los checks afectados y
   comparar la huella al terminar. Conservar salidas, resultados y hashes. Si cambia una
   fuente después, repetir la validación afectada y actualizar la evidencia. No reutilizar
   un resultado anterior sobre otro código.
6. Entregar el estado exacto: criterios de implementación pendientes, validaciones
   fallidas y comprobaciones externas pendientes. Si falta el teléfono, avanzar con las
   verificaciones disponibles y dejar identificada la revisión nativa que falta. TalkBack
   está excluido permanentemente por petición del usuario; no se programa como pendiente.

## Matriz y comprobación del plan actual

[daily-use-clarity.json](plans/daily-use-clarity.json) es la lista de criterios del plan
«Auditoría de Yuku y propuesta de mejora», con prioridad de uso diario y claridad.
Las condiciones se revisan manualmente contra el código real. Cada criterio conserva
su estado, archivos y pruebas o tipo de evidencia. La comparación debe ser completa;
contar archivos o tests no sustituye esa revisión.

El comprobador local no ejecuta tests, no modifica archivos y no instala la app:

```powershell
pwsh -File scripts/check-plan-completion.ps1 -MatrixPath docs/plans/daily-use-clarity.json -EvidencePath artifacts/native-acceptance-20261007/closure-evidence-147.json
```

Verifica IDs únicos, referencias existentes, criterios sin código pendiente, huella
de fuentes y matriz, resultados TRX sin fallos/omisiones y pruebas referenciadas presentes,
salidas de compilación y hash del APK. Rechaza una evidencia obsoleta. Los logs/TRX/APK
locales están en artefactos ignorados por Git: en otro checkout su ausencia exige una
nueva validación, no un éxito implícito.

El ejemplo identifica la evidencia actual explícitamente. El archivo QA142 se
conserva como historia y debe ser rechazado después de cambiar fuentes o matriz;
no sustituirlo ni usar sus totales como prueba de una nueva versión.

Para obtener la huella antes de ejecutar las comprobaciones:

```powershell
pwsh -File scripts/check-plan-completion.ps1 -MatrixPath docs/plans/daily-use-clarity.json -ShowFingerprint
```

Para exigir también el cierre de todas las comprobaciones nativas aplicables, añadir
`-RequireNativeChecks`. El comando devuelve 2 mientras éstas sigan pendientes; el modo
normal puede aprobar código y pruebas, pero informa esa limitación explícitamente.
Salida 1 indica código pendiente, evidencia ausente/obsoleta o validación fallida.

El script no puede demostrar que un test sea suficiente ni que una pantalla sea buena.
Es una barrera contra omisiones de seguimiento y evidencia desactualizada; la revisión
funcional y visual sigue siendo necesaria. Después de un cierre sólo se reabre un
criterio por un defecto nuevo reproducible o un cambio de alcance, no para repetir
sin motivo toda la auditoría.

## Evidencia de esta entrega

El [informe de implementación](daily-use-implementation-2026-10.md) conserva mediciones,
compatibilidad, cobertura nativa histórica y procedimiento de publicación posterior.
La matriz distingue esa historia de la comprobación actual. No autoriza push, migraciones
ni despliegue; esos pasos conservan las instrucciones específicas del usuario.
