# Piratas a la Vista 

**Piratas a la Vista** es una herramienta en realidad virtual diseñada para la recolección y análisis de datos oculares durante la ejecución de tareas cognitivas en la nube. Desarrollada en Unity y optimizada para el dispositivo HTC Vive Focus 3, esta aplicación implementa un juego serio basado en el paradigma **Go/No-Go**, orientado a contextos de evaluación cognitiva, investigación y análisis del comportamiento visual.

Este proyecto es la continuación de un Trabajo Fin de Grado (TFG) previo. Respecto a la versión original, esta versión amplía el conjunto de métricas capturadas (diámetro pupilar, apertura ocular, posición y rotación de cabeza, eventos contextuales del estímulo) y sustituye el almacenamiento local por una infraestructura cloud en AWS, desplegada mediante Terraform en el repositorio [AppVR-TFM-AWS](https://github.com/AlejandroCB-23/AppVR-TFM-AWS.git).

---

## 🧠 ¿Qué hace esta app?

La aplicación integra:

- **Seguimiento ocular en tiempo real** utilizando las capacidades del HTC Vive Focus 3, incluyendo dirección de la mirada, posición de los ojos, diámetro pupilar, apertura ocular y vergencia.
- Un juego **Go/No-Go en VR**, donde el usuario debe disparar a barcos piratas (estímulo *Go*) e inhibir la respuesta ante barcos pesqueros (estímulo *No-Go*).
- **Registro automático de métricas contextuales**: identificación del objeto observado, clasificación del estímulo, tiempos de fijación, disparos y contadores de partida.
- **Envío automático de los datos a AWS** mediante *presigned URLs* al finalizar cada sesión, sin necesidad de almacenar credenciales en el dispositivo.

Esta solución permite evaluar el comportamiento atencional y el control inhibitorio de forma inmersiva y precisa, siendo útil tanto en entornos clínicos como de investigación.

---

## 🛠 Instalación

Sigue estos pasos para instalar y ejecutar la aplicación:

1. **Clona este repositorio.**
2. **Abre el proyecto en Unity**, usando la versión especificada (recomendado: `Unity 2022.3.62f3`).
3. **Elimina los objetos `Main Camera` y `Directional Light`:**
   - Arrastra las escenas desde `Assets/Scenes` al panel `Hierarchy`.
   - Elimina la escena por defecto haciendo clic en los tres puntos (`⋮`) a la derecha del nombre y seleccionando `Remove Scene`.
4. **Activa el seguimiento ocular:**
   - Ve a `Edit → Project Settings → XR Plug-in Management`.
   - En la pestaña `Android`, selecciona `WaveXR`.
5. **Configura las opciones de WaveXR en `WaveXRSettings`:**
   Activa las siguientes características:
   - `Tracker`
   - `Natural Hand`
   - `Eye Tracking`
   - `Eye Expression`
   - `Lip Expression`
   - `Body Tracking`
   - `Scene Perception`
   - `Scene Mesh`
   - `Marker`
6. **Configura el endpoint de tu infraestructura AWS:**
   Antes de desplegar el juego, despliega primero la infraestructura cloud siguiendo las instrucciones del repositorio [AppVR-TFM-AWS](https://github.com/AlejandroCB-23/AppVR-TFM-AWS.git) y copia la URL del *API Gateway* resultante. En Unity, actualiza el campo `uploadApiEndpoint` en el Inspector de los siguientes componentes:
   - Escena `ModoTest` → Objeto con el componente `HeatMapDataAWS`
   - Escena `ModoTest` → Objeto con el componente `StatsSavedAWS`
   - (Repetir igualmente en la escena `ModoAleatorio` si se desea usar ese modo)
8. **Conecta el dispositivo VR al ordenador mediante cable USB.**
9. **Compila y ejecuta el proyecto:**
   - Ve a `File → Build Settings`
   - Cambia la plataforma a `Android`
   - Haz clic en `Build And Run`