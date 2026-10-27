Es el sistema de mejoras de mi juego multijugador en Unity (Cosmic Cold. https://null-mentalcoffee.itch.io/cosmic-cold), hecho con Netcode for Gameobjects. 
El servidor decide quién compra qué, y los clientes solo ven el resultado.

Hay dos tipos de mejoras:

Individuales: cada jugador tiene su propio nivel.
Globales: un único nivel compartido por todos.

En el juego el dinero es compartido entre todos los jugadores por lo que cada mejora únicamente resta dinero de una cuenta global

Características Principales de la Clase: 
Instance -> Singleton: acceso desde cualquier script con UpgradesManager.Instance.
OnMejorasActualizadas	-> Evento estático. La UI se suscribe para refrescarse cuando algo cambia.
database -> Un MejorasDatabase (ScriptableObject) con las definiciones: precio, nivel máximo, ámbito, etc.
registroIndividual / registroGlobal -> NetworkList con entradas como (ClientId, MejoraId, Nivel) para las individuales y las globales sin el ClientId.
_pendientesIndividuales / _pendienteGlobal ->	Datos cargados de un guardado antes de que el objeto esté spawneado en red.


Ciclo de Vida
Awake: configura el singleton y crea las dos NetworkList. Deben crearse aquí, antes del spawn, porque Netcode las necesita registradas al inicio.

OnNetworkSpawn:
Todos (host y clientes) se suscriben a los cambios de las listas.
Si es el servidor:
Se suscribe a GameManager.OnJugadorDesconectado para borrar las mejoras de quien se va.
Aplica las cargas pendientes que llegaron antes del spawn.
Lanza OnMejorasActualizadas para que la UI se pinte.

OnNetworkDespawn: se desuscribe de todo para evitar fugas o llamadas a objetos destruidos.

OnDestroy: limpia Instance.

Para Comprar una Mejora
Cliente → SolicitarCompra → CompraMejoraRpc (servidor) → valida y cobra → modifica la NetworkList → se replica → OnListChanged → UI

Dentro de CompraMejoraRpc el servidor comprueba, en orden:

Que existan database y EconomyManager.
Identifica al comprador con SenderClientId. Se lee del propio mensaje de red, así que un cliente no puede hacerse pasar por otro.
Que la mejora exista en la base de datos.
Si es global o individual, y su nivel actual.
Que no esté ya al nivel máximo.
Que el precio sea válido.
Que IntentarGastarDineroServidorDirecto(precio) tenga éxito.
Solo entonces sube el nivel.

SubirNivelIndividual / SubirNivelGlobal buscan en la lista:
Si existe la entrada: la copian a una variable local, suben Nivel++ y la reescriben en la misma posición. 
Esto hace falta porque las structs son tipos por valor: registro[i].Nivel++ directamente no compila ni funcionaría. Reasignar registro[i] = entrada es lo que dispara OnListChanged.
Si no existe: añaden una nueva con Nivel = 1.

Consultas
ObtenerNivel / ObtenerNivelGlobal: recorren la lista y devuelven el nivel, o 0 si nunca se compró.
SePuedeComprar: lo usa la UI en el cliente para activar o desactivar el botón. Replica las validaciones, pero solo es informativa: la validación real ocurre en el servidor.
ObtenerTodasLasEntradasDe(clientId) y ObtenerTodasLasEntradasGlobales(): devuelven copias en List<> para recorrerlas cómodamente.

Guardado y carga
Las funciones Cargar.. reciben listas de EntradaMejoraGuardada:

Si aún no está spawneado (!IsSpawned): guarda los datos en _pendientes para aplicarlos después, en OnNetworkSpawn. 
Resuelve el problema de que el sistema de guardado cargue antes de que arranque la red.
Si ya está spawneado y es servidor: aplica directamente.
Si es cliente: no hace nada (solo el servidor puede escribir).

Desconexión
BorrarMejorasDeJugador recorre la lista de atrás hacia delante y elimina las entradas de ese ClientId.
