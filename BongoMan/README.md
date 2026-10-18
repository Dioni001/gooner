# BongoOverlay

Un gato (o el personaje que quieras) que vive sobre tu escritorio y reacciona a lo que haces:

- **Teclado:** mueve las patas según qué teclas pulses (izquierda, derecha o ambas).
- **Ratón:** una mano sigue el cursor con un movimiento elástico.
- **Fondo:** cuanto más rápido tecleas, más "enojado" se pone.

La ventana es transparente, siempre está encima de todo y **deja pasar los clics**, así que funciona mientras juegas o usas otras aplicaciones. Solo Windows.

> **Privacidad:** el programa lee las pulsaciones de teclas únicamente para animar al personaje. No guarda, registra ni envía nada. El código está completo en este repositorio para que puedas comprobarlo.

---

## Descargar y usar

### Opción A: descargar el programa (recomendada)

1. Ve a la página de **Releases** del repositorio y descarga el `.zip`.
2. Descomprímelo en cualquier carpeta.
3. Ejecuta `BongoOverlay.exe`.

No necesitas instalar nada más. Windows SmartScreen puede avisar porque el programa no está firmado: pulsa "Más información" y luego "Ejecutar de todos modos". Algunos antivirus pueden sospechar porque el programa escucha el teclado de forma global (es necesario para que funcione en juegos y otras apps).

**Descarga por línea de comandos (PowerShell):** cambia `USUARIO/REPO` por el del repositorio.

```powershell
$repo = "USUARIO/REPO"
$rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
$asset = $rel.assets | Where-Object name -like "*.zip" | Select-Object -First 1
Invoke-WebRequest $asset.browser_download_url -OutFile BongoOverlay.zip
Expand-Archive BongoOverlay.zip -DestinationPath BongoOverlay -Force
.\BongoOverlay\BongoOverlay.exe
```

### Opción B: compilarlo tú

Necesitas el SDK de .NET 8:

```powershell
winget install Microsoft.DotNet.SDK.8
```

Después, dentro de la carpeta del proyecto:

```powershell
dotnet run
```

Para generar un ejecutable que funcione sin instalar nada más:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

El resultado queda en `bin\Release\net8.0-windows\win-x64\publish\`. Allí estará `BongoOverlay.exe` y la carpeta `assets`; entrégalas juntas.

---

## Cerrar el programa

Como la ventana deja pasar los clics, se cierra desde el **icono de la bandeja del sistema** (junto al reloj; si no lo ves, pulsa la flecha `^`): clic derecho y **Salir**.

Alternativa desde una terminal:

```powershell
taskkill /im BongoOverlay.exe /f
```

---

## Cambiar el personaje

Todo el arte está en la carpeta `assets`. Para usar tu propio personaje, reemplaza los PNG por los tuyos, sin tocar el código. Si falta algún archivo, el programa usa alternativas (ver más abajo); si no hay ninguno, dibuja formas de prueba.

### Reglas obligatorias

1. **Mismo tamaño de lienzo en todos los archivos** (por ejemplo, 800x800 píxeles).
2. **Lienzo completo, sin recortar.** Aunque el PNG solo contenga un detalle pequeño (como la mano del ratón), debe exportarse con el tamaño total y el resto transparente. Nunca uses herramientas de "recortar al contenido".
3. **PNG de 32 bits con canal alfa** (transparencia real).
4. Todos se dibujan en la coordenada (0, 0); el espacio transparente hace de marco invisible y cada elemento queda en su sitio.

### Los archivos

| Archivo | Capa | Cuándo se muestra |
|---|---|---|
| `bg_normal.png` | 3 (fondo) | Siempre, al fondo |
| `bg_angry.png` | 3 (fondo) | Encima de `bg_normal`, con más o menos opacidad según lo rápido que tecleas |
| `body_idle.png` | 1 (cuerpo y teclado) | Sin teclas pulsadas |
| `body_left.png` | 1 | Solo teclas del lado izquierdo |
| `body_right.png` | 1 | Solo teclas del lado derecho |
| `body_both.png` | 1 | Teclas de ambos lados a la vez |
| `mouse_hand.png` | 2 (ratón) | Siempre, encima de todo |

El orden de dibujo es de atrás hacia delante: **fondo, cuerpo, mano del ratón**.

### Cómo funciona cada capa (y qué dibujar)

**Fondo (capa 3).** Cada tecla pulsada suma "energía" que decae con el tiempo. `bg_angry` se mezcla sobre `bg_normal` según esa energía, de forma gradual. Dibuja `bg_angry` como *la misma escena* que `bg_normal`, pero en versión intensa (tonos rojizos, efectos, etc.). Si no hay `bg_normal`, el enojado se desvanece hacia el escritorio.

**Cuerpo y teclado (capa 1).** Cambia entre los 4 PNG según las teclas pulsadas en ese instante. Los cuatro deben tener el cuerpo y el teclado **idénticos**; solo deben variar las manos. Si algo del cuerpo cambia de un PNG a otro, se verá un salto. Importante: el cuerpo se dibuja *encima* del fondo, así que si `body_idle` (u otro) tiene zonas opacas donde debería ser transparente, tapará el fondo.

**Mano del ratón (capa 2).** Es la única capa que se mueve. Dibújala en el lugar donde debe estar **cuando no mueves el ratón**; el programa la desplaza (hasta unos 80 px del lienzo) hacia donde apunte el cursor y la devuelve con un rebote elástico.

### Si falta algún archivo

- Sin `body_both.png`: se superponen `body_left` y `body_right` (los bordes pueden verse algo más gruesos).
- Sin `body_left`, `body_right` o `body_both`: se usa `body_idle`.
- Sin `bg_normal.png`: no se dibuja nada debajo del fondo enojado.
- Sin ningún PNG: se muestran formas de prueba.

### Consejos para el arte

- Trabaja con capas (Photoshop, Krita, Clip Studio...): una capa por PNG, exportadas con el lienzo completo. Comparte también el archivo fuente.
- Deja libre la zona de la mano del ratón en el cuerpo, o dibuja el cuerpo sin ese brazo, para que no haya dos manos.
- Mantén espacio libre alrededor de la mano del ratón, porque se desplaza sobre el resto.
- Evita el halo blanco en los bordes (restos de fondo al exportar).
- Para comprobar la transparencia, activa en tu editor la vista de cuadrícula/ajedrez y revisa que el fondo no sea opaco.

---

## Ajustes

Al inicio de `Program.cs` hay constantes que puedes cambiar (requiere compilar):

| Constante | Qué controla |
|---|---|
| `Scale` | Tamaño en pantalla (0.5 = la mitad del lienzo) |
| `EnergyPerKey` | Cuánto enoja cada pulsación |
| `EnergyDecayPerSec` | Qué tan rápido se calma |
| `EnergyMax` | Energía necesaria para el fondo 100% enojado |
| `HoldSeconds` | Segundos que se mantiene el enojo tras la última tecla |
| `SpringK` y `SpringDamping` | Rigidez y rebote de la mano del ratón |
| `MouseRange` | Cuánto se desplaza la mano del ratón |

La mano izquierda corresponde a las teclas del lado izquierdo del teclado (Q W E R T, A S D F G, Z X C V B, 1-5, Tab, Bloq Mayús, Shift/Ctrl/Alt izquierdos, Esc, F1-F5). Todo lo demás, incluida la barra espaciadora, cuenta como mano derecha.

---

## Problemas frecuentes

- **El fondo enojado solo se ve cuando hay una tecla pulsada:** revisa que `body_idle.png` no tenga zonas opacas que tapen el fondo.
- **Al pulsar Shift no cambia nada:** comprueba que `body_left`/`body_right` sean distintos de `body_idle`.
- **No encuentro cómo cerrarlo:** usa el icono de la bandeja del sistema o `taskkill` (ver arriba).