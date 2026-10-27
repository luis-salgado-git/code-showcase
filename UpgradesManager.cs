using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class UpgradesManager : NetworkBehaviour
{
    public static UpgradesManager Instance { get; private set; }

    public static event Action OnMejorasActualizadas;

    public MejorasDatabase database;

    public NetworkList<EntradaMejoraIndividual> registroIndividual { get; private set; }
    public NetworkList<EntradaMejoraGlobal> registroGlobal { get; private set; }

    private readonly Dictionary<ulong, List<EntradaMejoraGuardada>> _pendientesIndividuales = new Dictionary<ulong, List<EntradaMejoraGuardada>>();
    private List<EntradaMejoraGuardada> _pendienteGlobal;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        registroIndividual = new NetworkList<EntradaMejoraIndividual>();
        registroGlobal = new NetworkList<EntradaMejoraGlobal>();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this)
            Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        registroIndividual.OnListChanged += AlCambiarListaIndividual;
        registroGlobal.OnListChanged += AlCambiarListaGlobal;

        if (IsServer)
        {
            GameManager.OnJugadorDesconectado += BorrarMejorasDeJugador;

            if (_pendienteGlobal != null)
            {
                AplicarCargaGlobal(_pendienteGlobal);
                _pendienteGlobal = null;
            }

            foreach (var kvp in _pendientesIndividuales)
            {
                AplicarCargaIndividual(kvp.Key, kvp.Value);
            }
            _pendientesIndividuales.Clear();
        }

        OnMejorasActualizadas?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        if (registroIndividual != null)
            registroIndividual.OnListChanged -= AlCambiarListaIndividual;

        if (registroGlobal != null)
            registroGlobal.OnListChanged -= AlCambiarListaGlobal;

        if (IsServer)
        {
            GameManager.OnJugadorDesconectado -= BorrarMejorasDeJugador;
        }
    }
    private void AlCambiarListaIndividual(NetworkListEvent<EntradaMejoraIndividual> changeEvent)
    {
        OnMejorasActualizadas?.Invoke();
    }

    private void AlCambiarListaGlobal(NetworkListEvent<EntradaMejoraGlobal> changeEvent)
    {
        OnMejorasActualizadas?.Invoke();
    }

    private void BorrarMejorasDeJugador(ulong clientId)
    {
        if (!IsServer) return;

        for (int i = registroIndividual.Count - 1; i >= 0; i--)
        {
            if (registroIndividual[i].ClientId == clientId)
            {
                registroIndividual.RemoveAt(i);
            }
        }
    }

    public void SolicitarCompra(int mejoraId)
    {
        CompraMejoraRpc(mejoraId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CompraMejoraRpc(int mejoraId, RpcParams rpcParams = default)
    {
        if (database == null || EconomyManager.Instance == null) return;

        ulong idCliente = rpcParams.Receive.SenderClientId;

        MejoraDefinicionSO definicion = database.ObtenerPorId(mejoraId);
        if (definicion == null)
        {
            return;
        }

        bool esGlobal = definicion.ambito == MejoraDefinicionSO.AmbitoMejora.Global;
        int nivelActual = esGlobal ? ObtenerNivelGlobal(mejoraId) : ObtenerNivel(mejoraId, idCliente);

        if (nivelActual >= definicion.NivelMaximo) return;

        int precio = definicion.ObtenerPrecio(nivelActual);
        if (precio < 0) return;

        if (!EconomyManager.Instance.IntentarGastarDineroServidorDirecto(precio)) return;

        if (esGlobal)
            SubirNivelGlobal(mejoraId);
        else
            SubirNivelIndividual(mejoraId, idCliente);
    }

    private void SubirNivelIndividual(int mejoraId, ulong clientId)
    {
        for (int i = 0; i < registroIndividual.Count; i++)
        {
            if (registroIndividual[i].ClientId == clientId && registroIndividual[i].MejoraId == mejoraId)
            {
                EntradaMejoraIndividual entrada = registroIndividual[i];
                entrada.Nivel++;
                registroIndividual[i] = entrada;
                return;
            }
        }

        registroIndividual.Add(new EntradaMejoraIndividual { ClientId = clientId, MejoraId = mejoraId, Nivel = 1 });
    }

    private void SubirNivelGlobal(int mejoraId)
    {
        for (int i = 0; i < registroGlobal.Count; i++)
        {
            if (registroGlobal[i].MejoraId == mejoraId)
            {
                EntradaMejoraGlobal entrada = registroGlobal[i];
                entrada.Nivel++;
                registroGlobal[i] = entrada;
                return;
            }
        }

        registroGlobal.Add(new EntradaMejoraGlobal { MejoraId = mejoraId, Nivel = 1 });
    }

    public int ObtenerNivel(int mejoraId, ulong clientId)
    {
        for (int i = 0; i < registroIndividual.Count; i++)
        {
            if (registroIndividual[i].ClientId == clientId && registroIndividual[i].MejoraId == mejoraId)
            {
                return registroIndividual[i].Nivel;
            }
        }
        return 0;
    }

    public int ObtenerNivelGlobal(int mejoraId)
    {
        for (int i = 0; i < registroGlobal.Count; i++)
        {
            if (registroGlobal[i].MejoraId == mejoraId)
            {
                return registroGlobal[i].Nivel;
            }
        }
        return 0;
    }

    public bool SePuedeComprar(int mejoraId, ulong clientId)
    {
        if (database == null || EconomyManager.Instance == null) return false;

        MejoraDefinicionSO definicion = database.ObtenerPorId(mejoraId);
        if (definicion == null) return false;

        bool esGlobal = definicion.ambito == MejoraDefinicionSO.AmbitoMejora.Global;
        int nivelActual = esGlobal ? ObtenerNivelGlobal(mejoraId) : ObtenerNivel(mejoraId, clientId);

        if (nivelActual >= definicion.NivelMaximo) return false;

        int precio = definicion.ObtenerPrecio(nivelActual);
        return precio >= 0 && EconomyManager.Instance.ObtenerDineroActual() >= precio;
    }

    public List<EntradaMejoraIndividual> ObtenerTodasLasEntradasDe(ulong clientId)
    {
        List<EntradaMejoraIndividual> resultado = new List<EntradaMejoraIndividual>();
        for (int i = 0; i < registroIndividual.Count; i++)
        {
            if (registroIndividual[i].ClientId == clientId)
            {
                resultado.Add(registroIndividual[i]);
            }
        }
        return resultado;
    }

    public List<EntradaMejoraGlobal> ObtenerTodasLasEntradasGlobales()
    {
        List<EntradaMejoraGlobal> resultado = new List<EntradaMejoraGlobal>();
        for (int i = 0; i < registroGlobal.Count; i++)
        {
            resultado.Add(registroGlobal[i]);
        }
        return resultado;
    }

    public void CargarMejorasIndividuales(ulong clientId, List<EntradaMejoraGuardada> mejoras)
    {
        if (!IsSpawned)
        {
            _pendientesIndividuales[clientId] = mejoras;
            return;
        }
        if (IsServer) AplicarCargaIndividual(clientId, mejoras);
    }

    private void AplicarCargaIndividual(ulong clientId, List<EntradaMejoraGuardada> mejoras)
    {
        for (int i = registroIndividual.Count - 1; i >= 0; i--)
        {
            if (registroIndividual[i].ClientId == clientId)
            {
                registroIndividual.RemoveAt(i);
            }
        }

        if (mejoras == null) return;

        foreach (EntradaMejoraGuardada m in mejoras)
        {
            if (m.nivel <= 0) continue;
            registroIndividual.Add(new EntradaMejoraIndividual { ClientId = clientId, MejoraId = m.mejoraId, Nivel = m.nivel });
        }
    }

    public void CargarMejorasGlobales(List<EntradaMejoraGuardada> mejoras)
    {
        if (!IsSpawned)
        {
            _pendienteGlobal = mejoras;
            return;
        }
        if (IsServer) AplicarCargaGlobal(mejoras);
    }

    private void AplicarCargaGlobal(List<EntradaMejoraGuardada> mejoras)
    {
        registroGlobal.Clear();
        if (mejoras == null) return;

        foreach (EntradaMejoraGuardada m in mejoras)
        {
            if (m.nivel <= 0) continue;
            registroGlobal.Add(new EntradaMejoraGlobal { MejoraId = m.mejoraId, Nivel = m.nivel });
        }
    }
}

public struct EntradaMejoraIndividual : IEquatable<EntradaMejoraIndividual>, INetworkSerializable
{
    public ulong ClientId;
    public int MejoraId;
    public int Nivel;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref MejoraId);
        serializer.SerializeValue(ref Nivel);
    }

    public bool Equals(EntradaMejoraIndividual other)
    {
        return ClientId == other.ClientId && MejoraId == other.MejoraId && Nivel == other.Nivel;
    }
}

public struct EntradaMejoraGlobal : IEquatable<EntradaMejoraGlobal>, INetworkSerializable
{
    public int MejoraId;
    public int Nivel;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref MejoraId);
        serializer.SerializeValue(ref Nivel);
    }

    public bool Equals(EntradaMejoraGlobal other)
    {
        return MejoraId == other.MejoraId && Nivel == other.Nivel;
    }
}