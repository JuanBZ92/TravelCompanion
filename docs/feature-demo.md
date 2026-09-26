# Cuenta de ejemplo de funciones

Cuenta ficticia `features-demo@travelcompanion.local`, marcada como demo e interna.
No tiene correo verificado ni contraseña. No representa una compra real.

- PIN **3333**: viaje editable, pase de prueba habilitado durante 90 días, asistente,
  mapa completo, rutas, edición, documentos locales y descarga offline.
- PIN **3334**: copia curada del viaje para probar fichas de vuelos, hoteles y cuatro
  documentos PDF precargados. Este modo no habilita edición ni asistente.

Ambos accesos pertenecen a la misma cuenta, pero son viajes distintos: los cambios
realizados en uno no se replican al otro. El usuario 1111 no se modifica.

Cada viaje tiene 18 días, empezando en la fecha de creación en Japón, y cuatro
ciudades: Tokyo, Kyoto, Osaka y Fukuoka. El primer día tiene horarios espaciados;
el segundo tiene tres actividades solapadas intencionalmente; el tercero está vacío.
Los días 6 y 10 tienen trenes y el día 14 un vuelo. Hay cuatro hoteles y recordatorios
habilitados para reservas confirmadas y vuelos. La entrega de notificaciones requiere
permiso del dispositivo y la ejecución del servicio correspondiente; no se envían al crear la cuenta.

Los PDF en `/demo-documents/` son públicos, ficticios y sin validez para viajar.
Para probar adjuntos locales con 3333, descargarlos e importarlos desde Documentos.
Los datos personales y códigos de reserva son de demostración.

Creación: `CatalogAdmin --create-feature-demo` con `CATALOG_DATABASE_URL` definido
para la base elegida. No ejecuta migraciones ni reinicia el catálogo. Rechaza PIN
ocupado o cuenta existente para preservar cambios hechos durante las pruebas.
