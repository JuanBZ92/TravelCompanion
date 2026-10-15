# Publicación Android 155 — R12, R13 y R14

Petición del 15 de octubre de 2026: subir los cambios R12–R14 a `main` e instalar
la APK habitual en el Android conectado. Autoriza esos dos pasos de publicación;
no solicita despliegue manual de API, migraciones ni subida a Drive.

## Contenido

- R12: importación y exportación de eventos individuales mediante ICS, con vista
  previa, confirmación, deduplicación y pausa de lote ante respuestas no confirmadas.
- R13: origen, transporte y margen para llegar al siguiente compromiso, con Maps
  disponible como alternativa y compatibilidad con el backend anterior.
- R14: mediciones reproducibles y eliminación de la doble lectura local de Journal.
- Documentación, pruebas y matrices de la entrega; explicación de R07, sin construirlo.
- Versión Android habitual incrementada de 154 a **155**. El paquete QA160 es
  independiente y su número no obliga a incrementar el paquete habitual a 161.
  Versión visible `1.0`, título e iOS 67 conservados.

## Criterios de publicación

| ID | Criterio | Evidencia |
|---|---|---|
| release155-build | APK Release ARM64, paquete habitual, versión 155, API de producción, diagnósticos desactivados y firma compatible. | `artifacts/release155-20261015/apk-proof155.json`, metadatos, firma, fuentes y log. |
| release155-main | Commit con el alcance revisado publicado en `main`, sin forzar historial. | Commit y lectura posterior de `origin/main` en `delivery-status.json`. |
| release155-device | Actualizar mediante `adb install -r`, conservando datos y ajustes. | Resultado de instalación y lectura de paquete/versión/fecha inicial en `delivery-status.json`. |

El estado final de push e instalación se registra después de ejecutarlos en
`artifacts/release155-20261015/delivery-status.json`; no se anticipa su resultado.
No se desinstala ni se limpia la aplicación habitual. No se abren recorridos que
soliciten biometría ni se cambian ajustes del sistema.

## Artefacto verificado

`YUKU-Japan-155-r12-r13-r14.apk`, **25.283.622 bytes**, SHA-256:
`C7C7CBA1D45F89B7E42878E62B04C1D122688E4217BA3C4647C5D3D4A5D234F0`.

La compilación terminó con cero errores. Paquete `com.yuku.travelcompanion.app`,
solo ARM64, API `https://travelcompanion-api-57dw.onrender.com` y diagnósticos
desactivados comprobados en el ensamblado compilado. La firma coincide con la APK
154 recuperada del teléfono. Las 364 fuentes/configuraciones del manifiesto de
compilación conservaron sus hashes.

Los 841 archivos runtime/tests de la instantánea QA160 se compararon con el árbol
actual: solo difiere `ApplicationVersion` de Android, de 154 a 155. La evidencia
funcional de QA160 permanece identificada como esa versión y snapshot en
[la entrega R12–R14](../roadmap-r12-r14-delivery.md); no se presenta como una prueba
nativa nueva del paquete habitual. La publicación comprueba compilación, identidad,
configuración, firma e instalación. No repite las pruebas funcionales por un cambio
exclusivo del número de versión ni elimina sus limitaciones nativas documentadas.

La compilación usa `-r android-arm64` singular y salida nueva `artifacts/r155`,
evitando el Shared obsoleto encontrado en las APK QA intermedias.

## Backend y reversión

No hay cambios de esquema ni migraciones. R13 añade campos opcionales al endpoint
de rutas existente: debe publicarse la API antes de utilizar sus nuevas estimaciones.
`render.yaml` mantiene `autoDeployTrigger: off`; este push no solicita ni acredita
un despliegue manual. El móvil conserva Maps si una API anterior no confirma el
origen/modo/margen solicitados.

Revertir código mediante un commit nuevo, conservando historial y datos. Para
volver a distribuir un móvil anterior, compilarlo con un número superior al
instalado; no desinstalar para forzar un downgrade. Los eventos ICS importados
siguen siendo actividades normales y no se borran al revertir binarios.
