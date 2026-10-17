# Registro de cambios

Traducción al español de [CHANGELOG.md](CHANGELOG.md), versión por versión, a
partir de la 0.3.0. Cada sección se adjunta a la publicación
con el nombre `notes.es.md`: es la que la aplicación muestra en español.

Los apartados siguen los del inglés: `### Añadido`, `### Cambiado`,
`### Eliminado`, `### Corregido`.

Cada entrada cabe en una línea corta que dice lo que gana el jugador; el
porqué queda en los commits y en `docs/DECISIONS.md`.

## [Unreleased]

### Añadido

- Las misiones repetibles muestran su ritmo en las listas de guías: diaria,
  semanal, repetible o de temporada.

### Corregido

- Las guías de misiones vuelven a llenarse para los jugadores nuevos, y las
  misiones que el sitio añade llegan solas.

## [0.8.22] - 2026-10-16

### Cambiado

- La ayuda sobre la Carpeta segura de Samsung cubre también un inicio de
  sesión bloqueado en Island, y Parallel Space para una cuenta más.

### Corregido

- En Android 14 y anteriores, un clic en un juego cuyo teléfono se durmió
  despierta el teléfono, y los clics siguientes vuelven a llegar.

## [0.8.21] - 2026-10-16

### Cambiado

- Los atajos se agrupan en tres partes, y las cuentas 1 a 8 y los tamaños 1 a
  4 caben cada uno en una línea que se despliega.

## [0.8.20] - 2026-10-16

### Añadido

- F1 a F8 ponen en primer plano la cuenta de esa línea de la lista, en su
  pestaña si está en el marco. Su tecla aparece en el título de la ventana y
  junto a la cuenta. Se cambian en Atajos.

### Corregido

- Un error inesperado ya no abre una ventana nueva cada medio segundo: un solo
  informe, y DT Hub sigue usable detrás.
- El tiempo de juego ahora cuenta las ventanas cerradas desde DT Hub: la cruz
  de una pestaña, un atajo, Salir.
- Una ventana cerrada mientras se abría ya no sigue marcada como abierta, y su
  juego ya no sigue funcionando en el teléfono.
- Una misma cuenta ya no puede abrirse dos veces al lanzarla durante el
  arranque.
- Un teléfono lento en responder ya no cierra todas las ventanas de juego.
- Cerrar el marco de pestañas ya no hace aparecer las pestañas ocultas, y un
  ligero deslizamiento sobre la cruz de una pestaña cierra la cuenta en lugar
  de mover la pestaña.
- Ordenar las ventanas ya no le da el teclado a cada ventana minimizada.
- Un icono de cuenta dañado ya no provoca errores repetidos.
- Salir siempre cierra DT Hub, y relanzarlo mientras se cierra ya no lo hace
  fallar.
- Un ajuste que no se pudo guardar ahora lo dice en la banda.
- Tras una descarga demasiado larga, la ventana de las herramientas vuelve a
  ofrecer «Reintentar» y «Continuar sin».

## [0.8.19] - 2026-10-16

### Añadido

- Cada pestaña lleva una cruz que cierra solo su cuenta.

### Cambiado

- Ctrl + E apaga la pantalla del teléfono aunque «Apagar la pantalla del
  teléfono mientras se juega» esté desmarcado: la del juego en primer plano, o
  la de todos los teléfonos desde otra ventana de DT Hub.
- «En cuadrícula» aparece a partir de tres ventanas de juego.

### Corregido

- Tras «Apagar», la línea que indica que la pantalla del teléfono está
  encendida ya no vuelve durante unos segundos.

## [0.8.18] - 2026-10-15

### Añadido

- Si enciendes la pantalla del teléfono durante el juego, para una llamada o un
  mensaje, una línea bajo el teléfono permite volver a apagarla, igual que
  Ctrl + E desde una ventana de juego.
- «En cuadrícula» coloca cuatro ventanas de juego en los cuatro cuartos de
  la pantalla, también con Ctrl + G.
- Una opción muestra la batería, el calentamiento, el lag y el tiempo de
  juego en el título de cada ventana de juego.

## [0.8.17] - 2026-10-15

### Cambiado

- Las ventanas de juego y las pestañas muestran solo el nombre de la cuenta,
  sin «DT Hub» delante.

### Corregido

- En una pestaña, el icono de una cuenta sin color vuelve a pegarse al borde
  izquierdo.

## [0.8.16] - 2026-10-15

### Añadido

- «Todas las versiones», en Novedades, abre las notas de cada versión en
  GitHub, en el idioma de la aplicación.

### Cambiado

- La casilla de cuenta «Corregir un juego desplazado» pasa a ser «Formato
  del teléfono (si el juego se desplaza)», y su descripción dice dejarla sin
  marcar en la mayoría de los casos.

## [0.8.15] - 2026-10-15

### Corregido

- Bitdefender ya no señala los anuncios del mapa mostrado en el Portal DT:
  sus dominios quedan inaccesibles desde todas las ventanas de DT Hub.
- Los campos de la vinculación muestran un ejemplo de lo que hay que escribir.

## [0.8.14] - 2026-10-15

### Añadido

- Con tu seguimiento de Papycha vinculado, cada zona de misiones muestra
  cuántas misiones están hechas sobre su total, y cuántas están en curso.
- Al pasar el ratón, «Por hacer» y «Accesibles» dicen qué deja cada filtro.

### Cambiado

- Con un filtro activo, el número junto a una zona es el de las misiones que
  siguen mostrándose.
- Las zonas de misiones ya no muestran rangos de niveles.

## [0.8.13] - 2026-10-15

### Corregido

- La ventana del Portal DT ya no dispara la alerta de Bitdefender: sus
  anuncios se rechazan, como en el mapa.
- El nivel del personaje queda alineado con su nombre en las guías.

### Cambiado

- «¿Dónde encuentro mi frase?» lleva directamente a tu página de seguimiento
  de Papycha y dice exactamente qué copiar.

## [0.8.12] - 2026-10-15

### Corregido

- La pantalla completa (Ctrl+5) en modo pestañas cubre toda la pantalla, sin
  barra de pestañas ni franjas negras.
- El mapa y DofusRoom se abren en el idioma de DT Hub y no en el de Windows;
  las herramientas que solo existen en francés lo indican.

### Añadido

- Seguimiento de misiones de Papycha en las guías: vincula tu seguimiento,
  una misión pasa a «en curso» cuando sigues sus etapas, se marca terminada
  con un clic, y la lista muestra el estado de cada misión con filtros.
- «Mis misiones» abre tu panel de Papycha.
- Un botón «Portal DT» abre lomitall.fr en una ventana de DT Hub.
- Un botón junto a la cruz minimiza el panel; Ctrl+P lo hace volver.
- La distancia «Algo lejana», entre Lejana y Normal.
- Al pasar el ratón, una calidad o una distancia muestra lo que ajusta:
  definición, imágenes por segundo, tasa de bits, densidad.

### Cambiado

- El botón de Discord pasa a la fila de herramientas de DOFUS Touch.

## [0.8.11] - 2026-10-14

### Corregido

- El juego vuelve a llenar la pantalla del PC, como antes de la 0.8.10, que
  le daba el formato del teléfono con franjas alrededor.

### Añadido

- «Corregir un juego desplazado», en el menú de una cuenta: para un
  dispositivo que dibuja el juego desplazado o cortado en tu pantalla, como
  una Galaxy Tab S10 Ultra en una pantalla ultrapanorámica, el juego conserva
  el formato de la pantalla del teléfono o la tableta.
- El panel de DT Hub se estira en altura por su borde inferior.

## [0.8.10] - 2026-10-14

### Corregido

- El juego se muestra con el formato de la pantalla del teléfono, sea cual
  sea la pantalla del PC: en una pantalla ultrapanorámica ya no se dibuja
  desplazado con su panel cortado, como en una Galaxy Tab S10 Ultra. La
  ventana del juego también toma ese formato.

### Eliminado

- El relanzamiento automático añadido en 0.8.9: el fallo al que apuntaba no
  era este. El informe de diagnóstico sigue diciendo cómo coloca el teléfono
  cada juego.

## [0.8.9] - 2026-10-14

### Corregido

- Un juego que el teléfono deja con un tamaño incorrecto en su ventana,
  desplazado con una franja negra y su panel cortado, se relanza una vez a
  tamaño completo.

### Cambiado

- El informe de diagnóstico dice cómo coloca el teléfono cada juego abierto,
  para encontrar lo que falla en un dispositivo que no podemos probar.

## [0.8.8] - 2026-10-13

### Corregido

- DofusRoom: el editor de mapa queda justo bajo la barra de título, y la
  lista ya no se desplaza al vacío cuando un anuncio queda vacío.

### Cambiado

- El menú de herramientas dice para qué sirve DofusRoom: las líneas de
  visión de los mapas de combate.

## [0.8.7] - 2026-10-13

### Añadido

- DofusRoom se suma a las herramientas de DOFUS Touch: las líneas de visión
  de los mapas de combate de Touch, en una ventana junto al juego.
- Una alerta de batería se puede ocultar desde su página de ayuda, en un
  dispositivo que no tiene ese ajuste, y volver a mostrar desde la ayuda.
- Un teléfono sin el juego puede mostrar igualmente sus otras aplicaciones,
  con el «+».

### Cambiado

- La ayuda para un dispositivo que sigue sin conexión tras el emparejamiento
  nombra las causas habituales: banda wifi, red pública, cortafuegos, VPN.

## [0.8.6] - 2026-10-13

### Añadido

- La calidad personalizada ofrece 90 y 120 imágenes por segundo, para los
  teléfonos con pantalla rápida.

## [0.8.5] - 2026-10-12

### Corregido

- Cambiar de pestaña en el marco con pestañas ya no hace parpadear en blanco
  una parte de la ventana.

## [0.8.4] - 2026-10-12

### Cambiado

- La opción «Imagen más nítida» ya no aumenta el ancho de banda Wi-Fi, que
  hacía ir el juego a tirones con varias cuentas abiertas.

### Corregido

- Ctrl+5, pulsado una segunda vez, sale de la pantalla completa y devuelve a
  las ventanas su tamaño y su sitio.
- El panel de ajustes ya no se puede maximizar arrastrándolo a la parte de
  arriba de la pantalla, y un panel que se quedó maximizado vuelve a abrirse a
  su tamaño normal.
- El marco con pestañas sale de la pantalla completa al tamaño que tenía.

## [0.8.3] - 2026-10-12

### Añadido

- Una casilla «Imagen más nítida» bajo los niveles de calidad dibuja el juego
  al doble del tamaño de la ventana: el texto de color, como el rojo sobre
  gris, queda tan nítido como el blanco.

## [0.8.2] - 2026-10-11

### Corregido

- En tableta, el juego se abre a pantalla completa en su ventana en lugar de
  una ventana flotante dentro de ella, y el chat vuelve a verse.

## [0.8.1] - 2026-10-11

### Corregido

- La densidad de la calidad personalizada por fin llega al juego: se ignoraba
  en todas las cuentas. Su lista también muestra su valor, se puede escribir y
  se aplica en cuanto se elige.
- Un ajuste cambiado mientras las ventanas se reabren ya no se pierde.
- La ventana de Dofus Touch Map ya no envía sus ventanas publicitarias al
  navegador.
- Los lectores de pantalla nombran los botones de icono del panel y leen las
  opciones de sus listas tal como se muestran.

## [0.8.0] - 2026-10-11

### Añadido

- Un menú de herramientas de DOFUS Touch en la pestaña Dispositivos: Dofus Touch
  Map y Calcofus se abren en una ventana de DT Hub, DTStuff y OcrApp se
  instalan y se abren desde el PC, DofusBook se abre en el navegador.
- Cada línea de alerta tiene un enlace Ayuda, adaptado a la aplicación en
  cuestión y a la marca del teléfono.
- Un teléfono Honor o Huawei conectado pero no detectado recibe los pasos de
  sus propios menús.
- Un controlador que retiene la depuración del teléfono sin pasársela a DT Hub
  se nombra, con los pasos para sustituirlo.
- Un juego guardado en una Carpeta segura de Samsung bloqueada se indica, con
  cómo desbloquearla y mantenerla abierta.
- La vinculación inalámbrica acepta la dirección escrita a mano cuando el
  teléfono no aparece.
- La calidad personalizada permite ajustar la densidad de la pantalla del
  juego: la interfaz mantiene su tamaño cuando la ventana es más pequeña.
- El Almanax lista las ofrendas de la semana, cada una lista para copiar en la
  búsqueda del mercadillo, y la ofrenda del día se copia con un clic.
- Buscar un oficio en el Almanax, como Minero, lista sus días de bonus en los
  próximos dos meses, recolección y fabricación.
- El Almanax señala los días que tienen además una misión repetible especial
  o una bendición, con adónde ir.

### Cambiado

- Un atajo puede ser una sola tecla, como Tab; solo las teclas que escriben
  en el chat del juego reciben un aviso.
- El clic derecho hace Atrás en las ventanas de juego, como en scrcpy.
- La tarjeta «Ningún aparato detectado» y el mensaje de controlador ausente
  dan la causa real y qué hacer.
- La ventana Dofus Touch Map se carga sin los anuncios que activaban los
  antivirus, y ofrece abrir el sitio en el navegador o hacer una donación.
- Una Carpeta segura que Samsung cierra al PC se dice tal cual, y el consejo
  de Samsung ahora lleva a un perfil de trabajo creado con Island.

### Eliminado

- La línea del perfil Android bajo una cuenta renombrada.

### Corregido

- El panel sigue visible tras salir con cuentas en pestañas.

## [0.7.11] - 2026-10-10

### Cambiado

- La ayuda de Samsung indica qué hacer cuando la depuración USB aparece en
  gris por el Bloqueador automático.

### Corregido

- Kaspersky eliminaba DT Hub como troyano: ahora busca sus actualizaciones
  sin la API de GitHub, y ya no reescribe sus accesos directos cuando ya son
  correctos.

## [0.7.10] - 2026-10-10

### Cambiado

- La ayuda del botón de vinculación y la confirmación de eliminación indican
  que la vinculación solo sirve para la conexión inalámbrica, y cómo recuperar
  un aparato eliminado.

### Corregido

- Un aparato eliminado vuelve en cuanto se conecta de nuevo su cable USB.
- La pastilla de ajustes y los botones de cada cuenta quedan alineados de una
  fila a otra, sea cual sea la distancia o la calidad mostrada, y junto a una
  aplicación añadida.
- Los botones de la derecha ya no se desplazan cuando la lista de aparatos
  empieza a desplazarse.

## [0.7.9] - 2026-10-04

### Añadido

- La ventana de informe de problemas también lleva al servidor Discord, con
  el informe copiado, para recibir ayuda sin cuenta de GitHub.

### Corregido

- En una pestaña, é, à, ? y las demás teclas que no son letras vuelven a
  escribir en el juego.

## [0.7.8] - 2026-10-04

### Cambiado

- Un aviso de lag aparece en un minuto aproximadamente en lugar de dos, y
  los avisos de calor y memoria se actualizan cada treinta segundos.

### Corregido

- Un teléfono que pierde el Wi-Fi sale del panel en un segundo
  aproximadamente, y ya no en varios.
- Un teléfono conectado durante un barrido ya no espera al siguiente para
  aparecer.

## [0.7.7] - 2026-10-03

### Añadido

- El título del marco con pestañas recuerda los atajos, como las ventanas
  libres.
- Seis nuevos colores de cuenta, un tono claro para cada uno de los seis.

### Cambiado

- Una ventana solo lleva el color de su cuenta en el borde, suavizado, en
  el marco como en ventana libre; la barra de título queda como la dibuja
  Windows.
- Las pestañas pierden su fondo teñido, y la activa se subraya con el color
  de su cuenta.
- Los colores de cuenta son más vivos.

### Corregido

- Una cuenta sacada del marco recupera el color de su borde.

## [0.7.6] - 2026-10-03

### Corregido

- Tras un Alt+Tab, el teclado sigue llegando al juego: una ventana de juego
  podía creer que Alt seguía pulsado y quedarse con todas las teclas.

## [0.7.5] - 2026-10-03

### Cambiado

- La calidad media pasa a 30 imágenes por segundo: alrededor de medio núcleo
  menos en el teléfono, y la mitad de tasa de bits.

### Corregido

- Un teléfono que se ralentiza porque su superficie está demasiado caliente
  ahora se indica, en teléfonos como los Xiaomi que solo lo dicen por ese
  sensor.
- Ctrl+Tab pulsado en las guías, el panel o el almanax devuelve el marco con
  pestañas y cambia de cuenta.

## [0.7.4] - 2026-10-03

### Corregido

- En el marco con pestañas, Ctrl+Tab sigue cambiando de cuenta tras volver
  a él con Alt+Tab.

## [0.7.3] - 2026-10-03

### Añadido

- El panel indica cuándo el teléfono, su Wi-Fi o el PC hacen que el juego
  vaya a tirones, y qué hacer.

### Cambiado

- Un teléfono muestra una sola línea de avisos: el más grave, con el número
  de los demás, todos visibles al pasar el ratón.
- Los consejos de Wi-Fi pasan a la casilla de conexión junto al nombre, que
  solo se pone ámbar por un problema en curso.

### Corregido

- Un teléfono conectado a la vez por su nombre y por su dirección ya no
  muestra sus avisos dos veces.

## [0.7.2] - 2026-10-02

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
