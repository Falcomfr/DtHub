# Registro de cambios

Traducción al español de [CHANGELOG.md](CHANGELOG.md), versión por versión, a
partir de la que sigue a la 0.5.2. Cada sección se adjunta a la publicación
con el nombre `notes.es.md`: es la que la aplicación muestra en español.

Los apartados siguen los del inglés: `### Añadido`, `### Cambiado`,
`### Corregido`.

## [0.6.0] - 2026-09-28

### Añadido

- Un ajuste apaga la pantalla del teléfono mientras sus cuentas juegan en el PC.
  El juego sigue funcionando, el teléfono se calienta menos y su batería dura
  más, lo que importa cuando el puerto USB del PC carga más despacio de lo que
  el juego consume. La pantalla se vuelve a encender cuando se cierra la última
  cuenta.
- El botón + de un teléfono abre ahora una ventana con dos opciones distintas.
  Mostrar una aplicación que el teléfono ya tiene, encontrada buscando su
  nombre, sin instalar ni copiar nada: una copia del juego hecha por una
  aplicación de clonación, o cualquier otra aplicación, se abre entonces en su
  propia ventana como el juego. O clonar el juego en una cuenta nueva, como
  hacía el botón hasta ahora.
- Una aplicación añadida así puede quitarse de la lista con la papelera de su
  fila. Se queda en el teléfono. Las filas del propio juego se muestran siempre.

### Cambiado

- En el marco con pestañas, cada pestaña toma el color de su cuenta, para
  distinguir las cuentas de un vistazo y no solo por una barra fina.
