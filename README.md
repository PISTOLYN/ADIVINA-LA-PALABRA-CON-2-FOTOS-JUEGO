# Adivina la Palabra con 2 Fotos

Juego web de concursos para **Desafíos Locos**, compatible con GitHub Pages. Está construido en un solo archivo HTML y no necesita instalar dependencias.

## Cómo publicarlo en GitHub Pages
1. En el repositorio, abre **Settings → Pages**.
2. En **Build and deployment**, selecciona **Deploy from a branch**.
3. Selecciona la rama principal y la carpeta **/(root)**; guarda.
4. Espera a que GitHub Pages publique el sitio.

## Archivos e imágenes
- Coloca las imágenes predeterminadas en la carpeta `images`.
- Primera foto: `images/foto1.jpg`
- Segunda foto: `images/foto2.jpg`
- Logo opcional: `images/logo-desafios-locos.png`

También puedes cambiar las dos fotografías directamente desde el juego: presiona **M** y selecciona los archivos de tu computadora. Para cambiar la respuesta, edita el campo **Palabra respuesta** en ese mismo menú. Los archivos seleccionados desde el menú se cargan en el navegador y no se guardan automáticamente en GitHub.

## Controles
- **ENTER**: iniciar una ronda de 10 segundos.
- **Q**: pulsador del Equipo Rojo.
- **P**: pulsador del Equipo Azul.
- **M**: abrir o cerrar configuración.
- **ESC**: reiniciar el estado de los pulsadores.

El primer equipo que pulse Q o P detiene el reloj y obtiene el derecho a responder. El otro equipo queda bloqueado durante esa ronda.

## Audios de celebración
En el menú de configuración (M), selecciona un MP3 para cada botón de celebración. Los audios se asignan en el navegador actual; vuelve a cargarlos si abres el juego en otro dispositivo o navegador.

## Marcadores
- **GANA ROJO / GANA AZUL**: suma un punto al equipo y reinicia el estado de la ronda.
- **PUNTO +**: suma un punto sin reiniciar la ronda.
- **REINICIAR PULSADORES**: detiene el reloj y deja la ronda lista para volver a iniciar con ENTER.

> Nota: el botón SALIR detiene la ronda; por las restricciones de los navegadores, la página no puede cerrar una pestaña que el usuario abrió manualmente.
