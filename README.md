# Adivina la Palabra con 2 Fotos — Desafíos Locos

Aplicación de escritorio Windows con instaladores x86 y x64.

## Compatibilidad
- Windows 10 de 32 bits: instalador x86.
- Windows 10 de 64 bits: instalador x64.
- Windows 11 de 64 bits: instalador x64.

Los instaladores son autocontenidos y no requieren instalar .NET por separado.

## Controles del juego
- Pantalla inicial: ENTER para comenzar.
- ENTER durante el juego: iniciar o reiniciar la cuenta regresiva de 10 segundos.
- Q: pulsador del Equipo Rojo.
- P: pulsador del Equipo Azul. El primer pulsador detiene el tiempo y bloquea al otro equipo.
- REINICIAR PULSADORES: desbloquea Q y P.
- NOMBRE: revela la respuesta de la escena actual; la respuesta permanece oculta hasta pulsarlo.
- Flechas izquierda/derecha: escena anterior/siguiente. Cada escena muestra su número.
- PUNTO ROJO / PUNTO AZUL: suma un punto al marcador del equipo.
- GANA ROJO / GANA AZUL: muestra al equipo ganador en pantalla grande.
- Cuatro botones verdes: cada uno reproduce su propio MP3.
- DETENER CANCIÓN: detiene la música.
- INSTRUCCIONES DEL JUEGO: lee las instrucciones en voz alta.
- Cuadro verde superior: alterna pantalla completa.
- M: abre el menú de configuración.
- ESC: reinicia los pulsadores; si hay pantalla de ganador la cierra.

## Menú secreto M
- Pestaña MÚSICA: asigna un MP3 distinto a cada uno de los cuatro botones verdes.
- Pestaña LOGOTIPO: selecciona el logotipo PNG de Desafíos Locos.
- Pestaña ESCENAS: agrega escenas y edita el fondo propio de cada escena, sus dos fotografías y su respuesta.
- Las flechas recorren las escenas en ciclo: después de la última vuelve a la primera y antes de la primera vuelve a la última. Los fondos se pueden volver a usar en distintas escenas.
- SALIR: cierra el menú y regresa al juego.
- CERRAR JUEGO: cierra el programa.

Los archivos seleccionados y la configuración se copian a la carpeta local de datos de la aplicación, por lo que permanecen disponibles al volver a abrir el juego en esa computadora.

## Obtener los instaladores
En GitHub abre Actions, espera a que termine el flujo "Compilar instaladores Windows" y descarga el artefacto "Instaladores-AdivinaLaPalabra-Windows".

Copia el instalador a una memoria USB y ejecútalo en cada computadora:
- x86 para Windows de 32 bits.
- x64 para Windows de 64 bits.

## Tecnología
C# Windows Forms, .NET 8 autocontenido, NAudio para MP3 y System.Speech para instrucciones habladas.
