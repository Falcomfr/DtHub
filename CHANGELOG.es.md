# Registro de cambios

Traducción al español de [CHANGELOG.md](CHANGELOG.md), versión por versión, a
partir de la 0.3.0. Cada sección se adjunta a la publicación
con el nombre `notes.es.md`: es la que la aplicación muestra en español.

Los apartados siguen los del inglés: `### Añadido`, `### Cambiado`,
`### Eliminado`, `### Corregido`.

Cada entrada cabe en una línea corta que dice lo que gana el jugador; el
porqué queda en los commits y en `docs/DECISIONS.md`.

## [Unreleased]

### Cambiado

- Una fila de mazmorra empieza por su nivel, termina con su llave y vuelve a
  mostrar su piedra de alma, con su icono.

### Corregido

- Todas las posiciones de mazmorra se escriben con una coma.

## [0.7.1] - 2026-10-02

### Cambiado

- Las filas de mazmorras son más ligeras: la cabecera del tramo indica la
  piedra de alma común, y una fila solo muestra el tamaño si es distinto.
- Los botones con marco toman un leve contorno azul al pasar el ratón, el
  foco del teclado se ve como un anillo redondeado y cada botón responde al
  clic.

### Corregido

- El nivel de las mazmorras de nivel 200 ya no aparece cortado.

## [0.7.0] - 2026-10-01

### Cambiado

- El informe de error indica la dirección de la página tras el lugar del
  error, para que Papycha la abra directamente.

### Corregido

- La barra superior de Papycha ya no tapa las guías ni el informe de error
  desde la actualización de octubre del sitio.

## [0.6.2] - 2026-09-30

### Corregido

- Cerrar la ventana de pestañas ya no olvida sus cuentas ni su lugar: todo
  vuelve en el siguiente inicio.

## [0.6.1] - 2026-09-28

### Corregido

- Quitar una app añadida muestra una carga en lugar de parecer que no hace
  nada.

## [0.6.0] - 2026-09-28

### Añadido

- Opción para apagar la pantalla del teléfono mientras juegas: se calienta
  menos y la batería dura más.
- El botón + puede mostrar cualquier app ya instalada, como un DOFUS Touch
  clonado, o clonar el juego en una cuenta nueva como antes.
- Una app añadida así se quita con la papelera de su fila.

### Cambiado

- Las pestañas toman el color de su cuenta.

## [0.5.2] - 2026-09-27

### Añadido

- La fila de cada teléfono avisa cuando su memoria está llena, el momento en
  que las acciones empiezan a ir lentas.

## [0.5.1] - 2026-09-27

### Corregido

- Tras un Ctrl+Tab, una cuenta podía dejar de mover a su personaje.

## [0.5.0] - 2026-09-27

### Añadido

- El panel muestra qué hace cada cuenta, y desde hace cuánto.
- Cada teléfono muestra su batería y su Wi-Fi, y su temperatura o su espacio
  libre cuando importa.
- Cada cuenta tiene un color, en su fila, su pestaña y su ventana (Windows 11
  para la ventana).
- Un teléfono que no responde indica dónde mirar, con los pasos para su marca.
- Un botón al pie del panel abre el Discord de DT Hub.

### Cambiado

- Filas más claras: iniciar y detener en el mismo sitio, los ajustes de la
  cuenta en una sola etiqueta.

### Corregido

- El idioma elegido se aplica en todas partes, y un teléfono ya no aparece dos
  veces.

## [0.4.0] - 2026-09-14

### Añadido

- La distancia en el juego se ajusta por cuenta, como la calidad.

### Eliminado

- El ajuste «Registrar las imágenes por segundo».

### Corregido

- Las ventanas de juego ya no se quedan en negro, y el primer inicio ya no
  pide un segundo clic.
- Una cuenta renombrada conserva su nombre y su lugar en la lista.
- Un teléfono cuya dirección cambió se vuelve a conectar, y vincular un
  teléfono nuevo vuelve a funcionar.
- Un inicio o una acción que falla se indica y sigue siendo legible.

### Cambiado

- La lista de teléfonos aparece más rápido.

## [0.3.0] - 2026-09-12

### Añadido

- El final de una misión indica lo que desbloquea.

### Cambiado

- El panel muestra la batería y el resumen de cada teléfono, y el tiempo de
  juego de la semana por cuenta.
- Un nivel de calidad por cuenta, con sus propias imágenes por segundo.
- Un ratón simulado, como último recurso para los teléfonos que rechazan los
  clics.
- Los ajustes se pueden guardar y restaurar.

### Corregido

- La aplicación se abre cuatro veces y media más rápido y ya no falla al
  arrancar.
- Una conexión perdida ya no cierra la aplicación.
- Cerrar una ventana también cierra el juego en el teléfono.
- El ajuste de idioma funciona, y el español ya no mezcla tú y usted.
