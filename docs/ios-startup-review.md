# Revisión del cierre al arrancar en iOS — 29/09/2026

Caso reportado: iPhone 17, cierre al abrir, antes funcionaba, probablemente
TestFlight 65. No se dispone aún de versión exacta de iOS, archivo `.ips`, dSYM
ni diagnóstico exportado de ese dispositivo. No atribuir definitivamente el
cierre a uno de los siguientes hallazgos sin esa evidencia.

## Hallazgos corregidos

1. **Callback StoreKit sin anotación AOT.** El servicio nativo de compras creaba
   un puntero a un delegado administrado en un inicializador estático. El servicio
   se resuelve al construir la ventana, mediante StorePurchaseRecoveryService.
   iOS necesita un callback compatible con AOT: se añadió
   `ObjCRuntime.MonoPInvokeCallback` al método estático y se difirió la creación
   del puntero hasta una llamada real a StoreKit. El delegado sigue enraizado
   estáticamente para que el recolector no lo libere. Es el candidato más directo
   para investigar un fallo exclusivo de iOS al arrancar; todavía no reproducido.
2. **Excepciones en Window.Activated.** El evento era `async void` sin protección.
   La recuperación de compras deja propagar OperationCanceledException (incluye
   timeout HTTP), que podía escapar del evento. Ahora se registra la cancelación
   o el fallo en el diagnóstico local y se permite continuar con la aplicación.
   Las compras pendientes no se borran; se pueden recuperar en otra activación.
3. **Diagnóstico de memoria que no compilaba en iOS.** Se reproducía CS0115 en
   AppDelegate.ReceiveMemoryWarning con el SDK instalado. Se sustituyó por el
   observador tipado de UIApplication, retenido y liberado con el delegado.
   También se desambiguó el namespace Services frente a la propiedad heredada.
   Un error de compilación no explica un binario de TestFlight ya instalado.
4. **Limpieza de previews al iniciar.** Un archivo temporal sin acceso ya no
   impide abrir la app; se registra el fallo, además del caso IOException existente.
5. **Permiso de fotos.** Journal utiliza MediaPicker y faltaba
   NSPhotoLibraryUsageDescription. Se añadió la finalidad del acceso. Es una
   corrección de configuración del selector, no una explicación del arranque.

## Otras áreas inspeccionadas

- Arranque/DI, selección de pestañas y recuperación de sesión, biometría y Keychain.
- MapKit y personalización de anotaciones, comparados con MAUI 10.0.60.
- Notificaciones nativas y tareas de sincronización al activar la ventana.
- Carga de itinerario, eventos de caché y despacho de notificaciones a UI.
- Journal, carga de imágenes, límites del selector y diagnósticos locales.
- Configuración Release, framework Swift, imports nativos, Info.plist y permisos.

No se cambió el linker, ni se desactivó AOT, ni se habilitaron compras como supuesto
arreglo. Face ID y ubicación ya tenían declaraciones de uso. La ruta real de
arranque depende de la sesión guardada, no necesariamente de la última pestaña.

## Validación y siguiente TestFlight

Se prepara build iOS 66; comprobar que no esté usada en App Store Connect antes
de subir. La compilación C# del target iOS Release se puede comprobar en Windows
con `dotnet build ... -f net10.0-ios -c Release -t:Compile`; NO valida el enlace
nativo, firma, framework Swift, archive o ejecución AOT en un iPhone.

En Mac: generar un archive Release nuevo desde este código con el SDK/Xcode
compatible, conservar dSYM y publicar en TestFlight. Probar actualización sobre
65 sin borrar datos, apertura en frío con/sin biometría, modo avión, reanudar,
Viaje/Mapa, Journal/fotos y recuperación sandbox de compras pendientes.

Si vuelve a cerrar: obtener el `.ips` de esa ejecución y el número de build
exacto. Buscar fallo del cargador (`dyld`/framework), AOT/delegado, excepción
administrada, watchdog o jetsam. Si abre después, exportar Compartir diagnóstico
desde PIN o Cuenta. No reinstalar borrando datos como primer paso: las fotos
locales y los registros previos pueden perderse.

Referencias:
- https://learn.microsoft.com/en-us/dotnet/api/objcruntime.monopinvokecallbackattribute?view=net-ios-26.2-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device-media/picker?view=net-maui-10.0
- https://raw.githubusercontent.com/dotnet/maui/10.0.60/src/Core/maps/src/Platform/iOS/MauiMKMapView.cs
