# Documento técnico del proyecto de interfaz de realidad mixta para Meta Quest 3S

## 1. Introducción

Este proyecto consiste en una interfaz de realidad mixta desarrollada en Unity para Meta Quest 3S, orientada al control y supervisión de un robot y de su entorno mediante ROS 2. La aplicación integra visualización estereoscópica, teleoperación manual, representación de mapas y trayectorias, lectura de datos de telemetría y feedback háptico. El objetivo general es ofrecer una capa de interacción inmersiva que permita al usuario trabajar con información robótica y geoespacial dentro de un entorno XR, manteniendo una comunicación bidireccional con el sistema ROS 2.

La solución no se limita a mostrar información en un visor. El proyecto combina varios subsistemas que trabajan de forma coordinada: recepción de imágenes comprimidas desde ROS 2, cálculo de referencias espaciales a partir de TF, publicación de comandos de movimiento y poses objetivo, renderizado de nubes de puntos en GPU, control de paneles y ventanas flotantes, y herramientas específicas para trabajar con un mapa 3D basado en Cesium. Todo ello está pensado para ejecutarse dentro del ecosistema de Meta Quest y aprovechar tanto el seguimiento de manos/mandos como la infraestructura de Unity.

## 2. Objetivo funcional

La finalidad del sistema es proporcionar una interfaz XR para supervisión y control remoto de un robot o vehículo robótico. Desde el visor, el usuario puede:

1. Ver vídeo en tiempo real procedente de cámaras ROS 2.
2. Cambiar topics de cámara durante la ejecución.
3. Activar o desactivar distintos módulos de interacción mediante botones de la UI.
4. Controlar la posición o la velocidad del efector final con el mando derecho.
5. Publicar trayectorias y referencias al stack ROS 2.
6. Visualizar y seguir el estado del robot en un mapa 3D georreferenciado.
7. Colocar waypoints sobre el minimapa.
8. Mostrar nubes de puntos provenientes de sensores 3D.
9. Recibir retroalimentación háptica en función de fuerzas medidas en el robot.

La filosofía de diseño es modular: cada función se implementa en un componente independiente, lo que permite activar o desactivar capacidades según la escena o el experimento concreto.

## 3. Arquitectura general

La aplicación se organiza como un conjunto de scripts de Unity que se comunican con ROS 2 mediante ROS2ForUnity. La estructura lógica puede dividirse en cuatro capas:

### 3.1. Capa de comunicación ROS 2

Esta capa se encarga de crear nodos ROS, suscribirse a topics y publicar mensajes. Incluye componentes como:

1. `QuestTeleopROS2` para publicar `Twist`.
2. `QuestVelocityControl` para publicar `TwistStamped` y `PoseStamped`.
3. `QuestPositionControl` para publicar `PoseStamped` relativa al movimiento del mando.
4. `TF_Suscriber` para leer y almacenar el grafo TF.
5. `Stereo_EyeDisplay`, `Stereo_OVR_SBS`, `StereoRobotViewer`, `SingleCameraViewer` y `StereoRobotUIViewer` para imágenes de cámara.
6. `PointCloudSubscriberGPU` para nubes de puntos.
7. `RobotTelemetryController` para GPS y orientación.
8. `ForceHapticFeedback` para fuerza y vibración.

### 3.2. Capa de interacción XR

Esta capa recoge la entrada del usuario desde el sistema XR de Unity y desde los mandos Meta Quest. Se utiliza principalmente para:

1. Leer botones y joysticks.
2. Activar o desactivar control mediante eventos de UI.
3. Generar rayos para navegación, selección y colocación de objetos.
4. Mostrar vibración háptica cuando el robot aplica fuerza.

### 3.3. Capa de visualización

La visualización incluye tanto imágenes como geometría 3D:

1. Vídeo monocular o estereoscópico.
2. Nubes de puntos renderizadas por GPU.
3. Representación de robot, efector final y objetivo mediante TF.
4. Mapas y paneles flotantes.
5. Visuales auxiliares de control, líneas de referencia y marcadores.

### 3.4. Capa de interfaz de usuario

La UI permite cambiar topics, alternar vistas, mostrar iconos de estado y abrir/cerrar paneles. Hay un patrón común de feedback visual basado en `ToggleIconFeedback`, de forma que el usuario puede identificar rápidamente si una función está activa o no.

## 4. Integración con ROS 2

El proyecto depende de una integración continua con ROS 2. En términos prácticos, cada módulo crea un nodo cuando ROS está listo y mantiene sus propias suscripciones o publishers. Esto evita acoplar toda la lógica a un único script monolítico.

Los mensajes utilizados son los habituales del ecosistema robótico:

1. `sensor_msgs/CompressedImage` para vídeo.
2. `sensor_msgs/PointCloud2` para nube de puntos.
3. `geometry_msgs/Twist` y `geometry_msgs/TwistStamped` para control cinemático.
4. `geometry_msgs/PoseStamped` para enviar objetivos espaciales.
5. `tf2_msgs/TFMessage` para transformaciones entre frames.
6. `sensor_msgs/NavSatFix` para posición geográfica.
7. `geometry_msgs/Quaternion` y `geometry_msgs/WrenchStamped` para orientación y fuerza.

La comunicación se gestiona con un enfoque de baja latencia. En varios visualizadores se mantiene solo el último frame recibido para evitar colas largas y reducir el retardo percibido. En otros casos se limita la frecuencia de publicación para no saturar la red o el consumidor del lado ROS.

## 5. Visualización de cámaras

El proyecto incorpora varias variantes para mostrar cámaras, adaptadas a distintas formas de presentación en XR.

### 5.1. Visualización estereoscópica con una sola imagen SBS

El script `Stereo_EyeDisplay` recibe una imagen comprimida que contiene dos vistas lado a lado. La textura se carga en un `Texture2D`, se divide en mitad izquierda y mitad derecha, y cada mitad se aplica a una cámara distinta del rig XR. El componente está preparado para trabajar con `OVRCameraRig`, de forma que si no se asignan las cámaras manualmente, se buscan las cámaras de ojo izquierdo y derecho automáticamente.

También existe soporte para dos modos de aplicación:

1. Como textura de fondo mediante `OVROverlay`.
2. Como material aplicado a la cámara o a su superficie asociada.

Este módulo incluye un sistema de activación, cambio de topic y feedback visual por icono.

### 5.2. Visualización SBS con overlay OVR

`Stereo_OVR_SBS` es una versión especializada para `OVROverlay`. En lugar de separar la imagen en dos texturas distintas, toma la imagen estéreo y la asigna directamente al overlay, configurando rectángulos de origen y destino para controlar cómo se mapea la imagen. Es útil cuando se quiere aprovechar el renderizado por overlay de Meta Quest para reducir artefactos o mejorar la composición en visor.

### 5.3. Visualización de dos cámaras independientes

`StereoRobotViewer` suscribe dos topics distintos, uno para el ojo izquierdo y otro para el derecho. Cada stream se almacena en su propia cola y se actualiza en el material estereoscópico del quad de visualización. Este enfoque es adecuado cuando el sistema ROS publica dos cámaras independientes en lugar de una imagen SBS.

### 5.4. Visualización monocular

`SingleCameraViewer` muestra una sola cámara sobre un `RawImage` de UI. El script está optimizado para reducir carga visual y de memoria: reutiliza una textura persistente, mantiene el último frame recibido y limita la frecuencia de refresco. Además, incluye una política explícita de limpieza de memoria mediante `GC.Collect()` cada cierto número de frames, lo que refleja una preocupación práctica por el rendimiento en visor autónomo.

### 5.5. Cambiadores de topic por teclado virtual

`ROSTopicSelector`, `StereoOVRTopicSelector` y la parte de UI de `StereoRobotUIViewer` permiten editar el topic desde un campo de texto. Cuando el usuario selecciona el input, se abre el teclado virtual del sistema Meta y, al confirmar, el visualizador cambia de topic sin necesidad de recompilar ni reiniciar la escena.

## 6. Teleoperación y control del robot

Uno de los bloques más importantes del proyecto es el control remoto del efector final y del robot mediante los mandos de Meta Quest.

### 6.1. Control de velocidad global

`QuestTeleopROS2` publica `geometry_msgs/Twist` en `/cmd_vel` o en el topic introducido por el usuario. La entrada se recoge desde los thumbsticks de ambos mandos:

1. El stick izquierdo controla el desplazamiento lineal.
2. El stick derecho controla el giro angular.

El script limita la frecuencia de publicación para mantener una tasa estable y evitar saturación. Cuando se desactiva el control, envía un mensaje de parada para evitar que el robot mantenga el último comando recibido.

### 6.2. Control de velocidad con `TwistStamped`

`QuestVelocityControl` amplía la idea anterior y publica `TwistStamped` en un topic de feedforward. Además, al soltar el grip, envía una orden de parada y una `PoseStamped` de retención para conservar la postura de referencia del efector final.

Este script es más rico que una simple teleoperación lineal porque:

1. Usa una pose ancla cuando el usuario empieza a controlar.
2. Convierte del espacio XR al espacio del robot.
3. Gestiona velocidad lineal y angular con umbrales muertos.
4. Mantiene visualmente una línea entre la posición ancla y la pose actual del mando.
5. Añade vibración breve al iniciar el control como feedback de confirmación.

### 6.3. Control de posición del efector final

`QuestPositionControl` permite enviar una `PoseStamped` al mover físicamente el mando derecho. El control comienza cuando se pulsa el grip derecho. En ese momento se captura una pose ancla y se calcula la transformación relativa conforme el usuario mueve la mano.

El script incluye varias ideas relevantes:

1. Limita la frecuencia de publicación a una tasa configurable.
2. Usa `QuestTransformCalculator` para traducir el desplazamiento XR al sistema de frames del robot.
3. Puede exigir que exista una TF de mundo al iniciar, o trabajar provisionalmente en modo local mientras llega la información.
4. Muestra un visual de origen, un visual del gripper y una línea de control.
5. Actualiza el estado visual del gripper en función del trigger del mando.
6. Notifica por texto los valores publicados y el frame final usado.

### 6.4. Cálculo de transformaciones del control

`QuestTransformCalculator` es el núcleo matemático que ayuda a convertir la interacción XR a coordenadas del robot. Su función principal es calcular una pose objetivo coherente a partir de:

1. La pose ancla capturada al iniciar el control.
2. La pose actual del mando.
3. El frame de trabajo local.
4. La posible referencia global obtenida desde TF.

Si hay TF disponible entre `world_ned` y el frame del efector, el script reconstruye la referencia global para que la primera publicación sea consistente con la convención del robot. Si la TF no está disponible, mantiene el cálculo en el espacio local hasta que pueda resolverse.

Este componente también puede invertir la transformación si la relación se encuentra al revés en el grafo TF, y dispone de trazas de depuración para investigar discrepancias de frames.

## 7. Gestión de TF y referencia espacial

`TF_Suscriber` es uno de los elementos más importantes del sistema, porque sirve de base para sincronizar el mundo de Unity con el mundo del robot.

### 7.1. Suscripción a `/tf` y `/tf_static`

El script crea suscripciones a ambos topics y almacena los links recibidos en diccionarios internos. Cada transformación se guarda con:

1. Frame padre.
2. Frame hijo.
3. Traslación.
4. Rotación.
5. Marca temporal.

### 7.2. Resolución de transformaciones

No se limita a consultar links directos. Si no existe una transformación explícita entre dos frames, el script construye un grafo y busca una ruta compuesta para resolverla. Esto permite consultar relaciones TF indirectas sin depender de que el enlace exacto esté publicado de forma directa.

### 7.3. Inversión y composición

El componente implementa inversión de transformaciones y composición de forma explícita. Eso le permite reconstruir el grafo en ambas direcciones y resolver consultas aunque el sentido publicado en ROS sea el contrario al pedido desde Unity.

### 7.4. Diagnóstico

El módulo incorpora funciones de depuración para comprobar si el suscriptor está listo, cuántos mensajes ha recibido, cuántas transformaciones únicas conoce y qué ruta ha usado para resolver una consulta. Esto es especialmente útil en un entorno XR, donde una diferencia de convención entre ROS y Unity puede producir errores de orientación muy visibles.

## 8. Control del mapa y del minimapa

La aplicación también incluye un bloque de navegación geoespacial basado en Cesium.

### 8.1. Navegación del minimapa

`MinimapNavigationController` permite hacer zoom y desplazamiento sobre un mapa 3D. Mientras el usuario mantiene pulsado un botón del mando derecho, puede:

1. Escalar el terreno.
2. Desplazar el origen geográfico.
3. Mantener fijo el punto de foco durante el zoom.
4. Refrescar el overlay solo cuando el cambio es suficientemente significativo.

El foco de navegación se obtiene mediante raycast desde la cabeza o desde el controlador, según la configuración. Si no hay intersección con el minimapa, el sistema usa un plano horizontal de respaldo.

### 8.2. Seguimiento del robot en el mapa

`MapTfRobotFollower` toma las transformaciones TF y las aplica a distintos objetos del minimapa:

1. Raíz visual del robot.
2. Eje del `base_link`.
3. Eje del efector final.
4. Eje del objetivo.

Además, controla la visibilidad del mapa y puede ocultar los ejes cuando no hay TF disponible. Su lógica incluye una conversión de coordenadas de ROS a Unity y una protección para no mover por error ancestros de la cámara XR, lo que evitaría desplazar el mundo o el jugador de forma accidental.

### 8.3. Alternancia de mapa

`PanelWindowManager` y `MapToggleController` permiten activar o desactivar la parte cartográfica o la raíz del mapa desde la interfaz. `PanelWindowManager` también se encarga de mostrar paneles flotantes y de mantener sincronizado el icono de estado del mapa.

## 9. Colocación de waypoints

`WaypointPlacementController` implementa una herramienta de anotación espacial dentro del minimapa.

### 9.1. Modo de creación

Cuando se activa, el usuario puede apuntar al minimapa con el rayo del mando derecho y pulsar el trigger para crear un waypoint. El script realiza un raycast contra una capa configurable, calcula la posición de impacto y instancia el prefab del waypoint en un contenedor común.

### 9.2. Visualización de ayuda

El componente puede dibujar un rayo de colocación y un indicador de impacto. También puede desactivar otros scripts u objetos mientras dura el modo creación, para evitar interferencias entre la interacción de waypoint y el resto de la escena.

### 9.3. Gestión de la colección

Internamente mantiene una lista de los waypoints creados, lo que facilita borrarlos, consultarlos o extender el comportamiento con lógica posterior de navegación o misión.

## 10. Nube de puntos en GPU

`PointCloudSubscriberGPU` es el módulo más avanzado de visualización 3D del proyecto.

### 10.1. Suscripción y preparación

El script se suscribe a `sensor_msgs/PointCloud2` y prepara un pipeline de renderizado con `GraphicsBuffer` y `RenderParams`. El objetivo es dibujar la nube directamente en GPU en lugar de crear miles de objetos en CPU.

### 10.2. Procesado de datos

Cuando llega un mensaje `PointCloud2`, el script:

1. Valida que existan los campos necesarios.
2. Detecta los offsets de `x`, `y`, `z` y color/intensidad.
3. Calcula el número total de puntos.
4. Reduce la densidad si supera el límite de visualización.
5. Empaqueta los puntos en un formato propio de 16 bytes por punto.
6. Calcula bounds para centrar o escalar correctamente la nube.

### 10.3. Renderizado

El render se realiza con un shader específico, `Unlit/ROS/Point`, y con una malla base tipo quad. Cada punto se dibuja como una primitiva, lo que permite renderizar grandes conjuntos de datos con bastante más eficiencia que un sistema basado en GameObjects individuales.

### 10.4. Transformaciones y ajuste espacial

El sistema puede:

1. Aplicar una transformación ROS a Unity.
2. Escalar la nube.
3. Centrarla respecto a sus bounds.
4. Aplicar offsets locales.
5. Anclarla a un transform de referencia.

### 10.5. Control de visibilidad

Dispone de métodos para activar o desactivar la visualización, y enlaza ese estado con un icono de feedback. Esto facilita integrarlo en menús o paneles de control sin depender de la lógica interna del render.

## 11. Telemetría del robot

`RobotTelemetryController` conecta la información geográfica y de orientación de un vehículo con elementos de Unity y Cesium.

### 11.1. GPS

Se suscribe a un topic `NavSatFix` y actualiza un `CesiumGlobeAnchor` con la latitud, longitud y altitud recibidas. Esto permite situar visualmente el objeto en el mapa global con coherencia geográfica.

### 11.2. Orientación

También se suscribe a un mensaje de cuaternión. A partir de ese cuaternión extrae el yaw y lo aplica como rotación local o global, según la configuración. Incluye opción para invertir el signo del yaw y sumar un offset manual, lo que es útil cuando el frame real del sensor no coincide exactamente con la convención visual de Unity.

### 11.3. Frecuencia de actualización

Para reducir carga y evitar cambios excesivamente frecuentes, el controlador actualiza el estado a una frecuencia limitada. Esto hace más estable el comportamiento sobre visor autónomo.

## 12. Feedback háptico

`ForceHapticFeedback` traduce una fuerza medida en ROS a vibración en los mandos Meta Quest.

### 12.1. Entrada desde ROS

El script lee `WrenchStamped` y usa principalmente la componente de fuerza en Z. Esa lectura se puede invertir si la convención del sistema lo requiere.

### 12.2. Mapeo a vibración

La fuerza se convierte en una intensidad de vibración entre un mínimo y un máximo configurables. Si la fuerza cambia o si ha pasado cierto tiempo, el sistema refresca la vibración para que Quest la mantenga activa.

### 12.3. Seguridad y limpieza

Cuando el módulo se desactiva o se destruye, se detiene la vibración en ambos controladores para evitar que queden activados por error.

## 13. Gestión de paneles y ventanas

`PanelWindowManager` actúa como controlador de ventanas flotantes y paneles de la interfaz.

### 13.1. Paneles mono y estéreo

Puede instanciar paneles mono y estéreo a partir de prefabs. Si no hay un punto de spawn configurado, los crea delante de la cámara principal y orienta el panel hacia el usuario.

### 13.2. Carga de escenas o modelos de apoyo

Además del mapa, puede instanciar prefabs asociados a distintas entidades o vistas, como el modelo de Girona, CirteSub o Catamaran. Esto sugiere que la interfaz está pensada para reutilizarse con diferentes plataformas o escenarios.

### 13.3. Sincronización visual

El gestor también mantiene sincronizado el icono de estado del mapa, de modo que la UI refleja si la visualización cartográfica está activa o no.

## 14. Feedback visual de estado

La interfaz usa dos scripts simples pero muy útiles para comunicar estados al usuario:

1. `ToggleIconFeedback`, que cambia el sprite del icono entre activo e inactivo.
2. `ToggleVisualFeedback`, que cambia el color del texto de un botón.

Esto aporta consistencia visual a todos los módulos del proyecto, porque los usuarios pueden identificar con rapidez qué herramientas están activadas.

## 15. Resumen de funcionalidades implementadas

De forma sintética, el proyecto ya incorpora estas funciones principales:

1. Recepción y visualización de vídeo comprimido desde ROS 2.
2. Soporte para visualización monocular y estereoscópica.
3. Cambio dinámico de topics de cámara desde UI.
4. Publicación de velocidades del robot mediante joystick.
5. Publicación de poses objetivo del efector final.
6. Conversión de coordenadas XR a frames ROS/TF.
7. Lectura, almacenamiento y consulta de TF en tiempo real.
8. Representación del robot y de sus referencias en un mapa Cesium.
9. Navegación interactiva sobre el minimapa con zoom y panning.
10. Creación de waypoints en el minimapa.
11. Renderizado de nubes de puntos en GPU.
12. Telemetría geográfica y orientación para objetos en el mapa.
13. Feedback háptico basado en fuerza recibida.
14. Gestión de paneles y ventanas flotantes.
15. Feedback visual por iconos y color de texto.

## 16. Consideraciones de diseño técnico

El código muestra varias decisiones de ingeniería pensadas para un entorno XR real:

1. Se evita crear y destruir objetos innecesariamente en cada frame.
2. Se limitan tasas de publicación y refresco para no sobrecargar el visor.
3. Se prioriza la última muestra recibida cuando la fuente es más rápida que la visualización.
4. Se incorporan mecanismos de limpieza de recursos en `OnDestroy`.
5. Se usan mensajes de depuración explícitos para diagnosticar TF, renderizado y suscripciones.
6. Se mantiene la modularidad para que cada función pueda usarse de forma independiente.

Estas decisiones son especialmente importantes en Meta Quest 3S, donde el rendimiento, la latencia y la estabilidad visual son críticos.

## 17. Conclusión

El proyecto constituye una base bastante completa de interfaz de realidad mixta para robótica. No solo presenta información, sino que cierra el ciclo de interacción entre el usuario, el visor y el robot: recibe sensores, interpreta transformaciones, muestra datos espaciales y devuelve comandos y referencias al sistema ROS 2. La combinación de teleoperación, mapa 3D, vídeo estéreo, nubes de puntos, telemetría y háptica lo convierte en una plataforma sólida para experimentación, demostración o desarrollo posterior de una memoria de prácticas.

Como texto base para la memoria definitiva, este documento ya resume tanto la finalidad del proyecto como la manera en que está construido y las funcionalidades que tiene activas en el código actual.